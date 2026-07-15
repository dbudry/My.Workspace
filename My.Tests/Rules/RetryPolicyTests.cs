using System.Net;
using System.Net.Http;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Exercises <see cref="RetryPolicy.ShouldRetry"/>. This is the gate that
/// decides whether <c>RetryDelegatingHandler</c> will replay an HTTP request.
/// Two failure modes drive the test list:
///
///   1. Replaying a POST whose server-side code may have already applied
///      side effects — the original incident: the Google OAuth callback's
///      slow first attempt got retried, by which point the auth code was
///      consumed, and the retry surfaced as a 400 invalid_grant.
///   2. Failing to retry a transient gateway error (502/503/504) where the
///      function host never received the request, leaving the user
///      to manually click again for what should have been auto-recovered.
/// </summary>
public class RetryPolicyTests
{
    // ---------- Idempotent methods retry on every 5xx ----------

    public static TheoryData<HttpMethod> IdempotentMethods => new()
    {
        HttpMethod.Get,
        HttpMethod.Head,
        HttpMethod.Options,
        HttpMethod.Put,
        HttpMethod.Delete,
    };

    [Theory]
    [MemberData(nameof(IdempotentMethods))]
    public void Idempotent_methods_retry_on_500(HttpMethod method)
    {
        Assert.True(RetryPolicy.ShouldRetry(method, HttpStatusCode.InternalServerError));
    }

    [Theory]
    [MemberData(nameof(IdempotentMethods))]
    public void Idempotent_methods_retry_on_502(HttpMethod method)
    {
        Assert.True(RetryPolicy.ShouldRetry(method, HttpStatusCode.BadGateway));
    }

    [Theory]
    [MemberData(nameof(IdempotentMethods))]
    public void Idempotent_methods_retry_on_503(HttpMethod method)
    {
        Assert.True(RetryPolicy.ShouldRetry(method, HttpStatusCode.ServiceUnavailable));
    }

    [Theory]
    [MemberData(nameof(IdempotentMethods))]
    public void Idempotent_methods_retry_on_504(HttpMethod method)
    {
        Assert.True(RetryPolicy.ShouldRetry(method, HttpStatusCode.GatewayTimeout));
    }

    // ---------- POST and PATCH skip 500 retries (the OAuth-code-burn fix) ----------

    public static TheoryData<HttpMethod> NonIdempotentMethods => new()
    {
        HttpMethod.Post,
        HttpMethod.Patch,
    };

    [Theory]
    [MemberData(nameof(NonIdempotentMethods))]
    public void Non_idempotent_methods_do_not_retry_on_500(HttpMethod method)
    {
        Assert.False(RetryPolicy.ShouldRetry(method, HttpStatusCode.InternalServerError));
    }

    // ---------- POST and PATCH still retry on gateway-level failures ----------
    //
    // 502/503/504 indicate the request never reached the function code — so
    // the server can't have applied any side effects, and retry is safe even
    // for non-idempotent methods. This recovers transient cold-start failures
    // without burning OAuth codes.

    [Theory]
    [MemberData(nameof(NonIdempotentMethods))]
    public void Non_idempotent_methods_retry_on_502(HttpMethod method)
    {
        Assert.True(RetryPolicy.ShouldRetry(method, HttpStatusCode.BadGateway));
    }

    [Theory]
    [MemberData(nameof(NonIdempotentMethods))]
    public void Non_idempotent_methods_retry_on_503(HttpMethod method)
    {
        Assert.True(RetryPolicy.ShouldRetry(method, HttpStatusCode.ServiceUnavailable));
    }

    [Theory]
    [MemberData(nameof(NonIdempotentMethods))]
    public void Non_idempotent_methods_retry_on_504(HttpMethod method)
    {
        Assert.True(RetryPolicy.ShouldRetry(method, HttpStatusCode.GatewayTimeout));
    }

    // ---------- Success and 4xx never trigger retries ----------

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public void Success_codes_do_not_retry(HttpStatusCode status)
    {
        Assert.False(RetryPolicy.ShouldRetry(HttpMethod.Get, status));
        Assert.False(RetryPolicy.ShouldRetry(HttpMethod.Post, status));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void Client_error_codes_do_not_retry(HttpStatusCode status)
    {
        Assert.False(RetryPolicy.ShouldRetry(HttpMethod.Get, status));
        Assert.False(RetryPolicy.ShouldRetry(HttpMethod.Post, status));
    }

    // ---------- 505+ are uncommon but should NOT retry (not in the recoverable set) ----------

    [Theory]
    [InlineData((HttpStatusCode)505)] // HTTP Version Not Supported
    [InlineData((HttpStatusCode)511)] // Network Authentication Required
    public void Other_5xx_codes_do_not_retry(HttpStatusCode status)
    {
        Assert.False(RetryPolicy.ShouldRetry(HttpMethod.Get, status));
        Assert.False(RetryPolicy.ShouldRetry(HttpMethod.Post, status));
    }
}
