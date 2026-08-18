using System.Net.Http.Json;
using System.Text.Json;
using My.Shared.Constants;
using My.Shared.Dtos.Intranet;
using My.Shared.Rules;

namespace My.Client.Services;

public class IntranetMediaPolicyService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _clientFactory;
    private IntranetMediaPolicyDto? _cached;
    private bool _lastFetchFailed;

    public IntranetMediaPolicyService(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public bool LastFetchFailed => _lastFetchFailed;

    public async Task<IntranetMediaPolicyDto> GetAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cached != null)
            return _cached;

        try
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var response = await client.GetAsync(Constants.API.Intranet.Documents.MediaPolicy);
            if (!response.IsSuccessStatusCode)
            {
                _lastFetchFailed = true;
                return _cached ?? new IntranetMediaPolicyDto();
            }

            var loaded = await response.Content.ReadFromJsonAsync<IntranetMediaPolicyDto>(SerializerOptions);
            _cached = loaded ?? new IntranetMediaPolicyDto();
            _lastFetchFailed = false;
        }
        catch
        {
            _lastFetchFailed = true;
            return _cached ?? new IntranetMediaPolicyDto();
        }

        return _cached;
    }

    public void Invalidate()
    {
        _cached = null;
        _lastFetchFailed = false;
    }

    public static IntranetMediaPolicy ToPolicy(IntranetMediaPolicyDto dto) => new()
    {
        AllowedExtensions = dto.AllowedExtensions ?? new List<string>(),
        MaxUploadBytesByExtension = dto.MaxUploadBytesByExtension ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
    };

    public static string FormatPolicySummary(IntranetMediaPolicyDto dto)
    {
        var policy = ToPolicy(dto);
        return policy.IsConfigured
            ? $"{dto.AllowedExtensionsDisplay} · {dto.MaxUploadSizeDisplay}"
            : IntranetMediaPolicyRules.NotConfiguredMessage;
    }

    public static string FormatLoadFailureMessage() =>
        "Could not load editor file policy from the API. Restart the Functions host, confirm App Settings are saved, then try again.";
}