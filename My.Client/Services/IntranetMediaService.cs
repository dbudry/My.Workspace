using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.JSInterop;
using My.Shared.Constants;

namespace My.Client.Services;

/// <summary>
/// Loads private Google Drive images in the intranet editor and page view via the authenticated media API.
/// </summary>
public class IntranetMediaService
{
    private readonly IJSRuntime _js;
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly IHttpClientFactory _clientFactory;

    public IntranetMediaService(
        IJSRuntime js,
        IAccessTokenProvider tokenProvider,
        IHttpClientFactory clientFactory)
    {
        _js = js;
        _tokenProvider = tokenProvider;
        _clientFactory = clientFactory;
    }

    public async Task HydrateAsync(string containerElementId)
    {
        if (string.IsNullOrWhiteSpace(containerElementId))
            return;

        try
        {
            var tokenResult = await _tokenProvider.RequestAccessToken();
            if (!tokenResult.TryGetToken(out var token))
                return;

            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var apiBase = client.BaseAddress?.ToString();
            if (string.IsNullOrEmpty(apiBase))
                return;

            await HydrateContainerAsync(containerElementId, token.Value, apiBase);
            // Second pass covers late-rendered images (Quill attr restore, MarkupString on page view).
            await Task.Delay(150);
            await HydrateContainerAsync(containerElementId, token.Value, apiBase);
        }
        catch (AccessTokenNotAvailableException)
        {
            // User not signed in — images stay as placeholders.
        }
        catch (JSException)
        {
            // intranet-media.js not loaded yet.
        }
    }

    private async Task HydrateContainerAsync(string containerElementId, string accessToken, string apiBase)
    {
        try
        {
            var quillHydrated = await _js.InvokeAsync<bool>(
                "quillEditor.hydrateIntranetImages", containerElementId, accessToken, apiBase);
            if (quillHydrated)
                return;
        }
        catch (JSException)
        {
            // quill-editor.js not loaded or editor not mounted — fall through.
        }

        await _js.InvokeVoidAsync("intranetMedia.hydrate", containerElementId, accessToken, apiBase);
    }
}