using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using My.Client.Services;
using My.Shared;
using My.Shared.Constants;
using My.Shared.Dtos.Intranet;
using My.Shared.Rules;

namespace My.Client.Components.Intranet
{
    public enum ContentInsertKind
    {
        Image,
        Link
    }

    public partial class IntranetContentInsertDialog
    {
        [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
        [Inject] private ISnackbar Snackbar { get; set; } = null!;
        [Inject] private NavigationManager Navigation { get; set; } = null!;
        [Inject] private UserSettingsService UserSettings { get; set; } = null!;
        [Inject] private IntranetMediaPolicyService MediaPolicy { get; set; } = null!;

        [Parameter] public bool Visible { get; set; }
        [Parameter] public EventCallback<bool> VisibleChanged { get; set; }
        [Parameter] public ContentInsertKind Kind { get; set; }
        /// <summary>Which tab to show when the dialog opens (0 = first tab).</summary>
        [Parameter] public int InitialTab { get; set; }
        [Parameter] public string? PageId { get; set; }
        [Parameter] public string? SelectedLinkText { get; set; }
        [Parameter] public EventCallback<IntranetDocumentDto> OnFileInserted { get; set; }
        [Parameter] public EventCallback<(string Url, string Text)> OnUrlInserted { get; set; }

        private int activeTab;
        private bool isBusy;
        private string? errorMessage;

        // Web URL tab
        private string linkUrl = "https://";
        private string linkText = "";

        // Upload tab
        private string? uploadFileName;
        private string uploadDisplayName = "";
        private long uploadFileSize;
        private string? uploadBase64;
        private string? uploadMimeType;

        // Create Google file tab
        private string googleFileName = "";
        private string googleFileKind = "document";

        private IntranetMediaPolicyDto _mediaPolicy = new();
        private string? filePolicySummary;

        private bool CanUploadWithCurrentFileName =>
            !string.IsNullOrWhiteSpace(uploadDisplayName) &&
            IntranetFileHelper.IsValidUploadFileName(IntranetFileHelper.NormalizeUploadFileName(uploadDisplayName));

        private bool uploadFileNameInvalid =>
            !string.IsNullOrWhiteSpace(uploadDisplayName) &&
            !IntranetFileHelper.IsValidUploadFileName(IntranetFileHelper.NormalizeUploadFileName(uploadDisplayName));

        private bool CanCreateGoogleFile =>
            !string.IsNullOrWhiteSpace(googleFileName) &&
            IntranetFileHelper.IsValidUploadFileName(googleFileName);

        private bool googleFileNameInvalid =>
            !string.IsNullOrWhiteSpace(googleFileName) && !CanCreateGoogleFile;

        private Dictionary<string, object> UploadNameInputAttributes => new()
        {
            ["onfocusout"] = EventCallback.Factory.Create(this, NormalizeUploadDisplayNameOnBlur)
        };

        private Dictionary<string, object> GoogleNameInputAttributes => new()
        {
            ["onfocusout"] = EventCallback.Factory.Create(this, NormalizeGoogleFileNameOnBlur)
        };

        private string? UploadPreviewDataUrl =>
            string.IsNullOrEmpty(uploadBase64)
                ? null
                : $"data:{IntranetFileHelper.InferMimeType(uploadDisplayName, uploadMimeType)};base64,{uploadBase64}";

        private bool ShowImageUploadPreview =>
            Kind == ContentInsertKind.Image
            && !string.IsNullOrEmpty(uploadBase64)
            && IntranetFileHelper.ClassifyMimeType(uploadMimeType, uploadDisplayName) == IntranetFileHelper.FileTypeImage;

        private void NormalizeUploadDisplayNameOnBlur() =>
            uploadDisplayName = IntranetFileHelper.NormalizeUploadFileName(uploadDisplayName);

        private void NormalizeGoogleFileNameOnBlur() =>
            googleFileName = IntranetFileHelper.NormalizeUploadFileName(googleFileName);

        private void ClearUploadSelection()
        {
            uploadFileName = null;
            uploadDisplayName = "";
            uploadBase64 = null;
            uploadFileSize = 0;
            uploadMimeType = null;
        }

        protected override async Task OnParametersSetAsync()
        {
            if (Visible)
            {
                activeTab = Math.Max(0, InitialTab);
                linkText = SelectedLinkText ?? "";
                linkUrl = "https://";
                errorMessage = null;
                uploadFileName = null;
                uploadDisplayName = "";
                uploadBase64 = null;
                uploadFileSize = 0;
                uploadMimeType = null;
                googleFileName = "";
                googleFileKind = "document";

                _mediaPolicy = await MediaPolicy.GetAsync();
                filePolicySummary = IntranetMediaPolicyService.FormatPolicySummary(_mediaPolicy);
            }
        }

        private async Task CloseAsync()
        {
            Visible = false;
            await VisibleChanged.InvokeAsync(false);
        }

        private async Task EnsureGoogleConnectedAsync()
        {
            if (!UserSettings.IsGoogleCalendarConnected)
            {
                try { await UserSettings.GetSettingsAsync(); } catch { }
            }
            if (!UserSettings.IsGoogleCalendarConnected)
            {
                await CloseAsync();
                await UserSettings.InitiateGoogleConnectAsync(Navigation.Uri);
                throw new InvalidOperationException("Google not connected");
            }
        }

        private async Task InsertUrlAsync()
        {
            if (string.IsNullOrWhiteSpace(linkUrl))
            {
                errorMessage = "Enter a web address.";
                return;
            }

            var url = linkUrl.Trim();
            var text = string.IsNullOrWhiteSpace(linkText) ? url : linkText.Trim();
            await OnUrlInserted.InvokeAsync((url, text));
            await CloseAsync();
        }

        private Task OnFilePickedAsync(InputFileChangeEventArgs e)
        {
            // File bytes are loaded via OnFileReady / ApplyUploadFileAsync.
            return Task.CompletedTask;
        }

        private async Task ApplyUploadFileAsync(FileDropPayload payload)
        {
            uploadFileName = payload.FileName;
            uploadDisplayName = IntranetFileHelper.SuggestUploadFileName(payload.FileName);
            uploadFileSize = payload.Size;
            uploadMimeType = IntranetFileHelper.InferMimeType(payload.FileName, payload.ContentType);
            uploadBase64 = payload.Base64;
            errorMessage = null;

            var policy = IntranetMediaPolicyService.ToPolicy(_mediaPolicy);
            if (!IntranetMediaPolicyRules.TryValidateUpload(
                    uploadDisplayName,
                    uploadMimeType,
                    uploadFileSize,
                    policy,
                    out var policyError))
            {
                errorMessage = policyError;
                uploadBase64 = null;
                uploadFileName = null;
                uploadDisplayName = "";
                uploadFileSize = 0;
                uploadMimeType = null;
            }

            await InvokeAsync(StateHasChanged);
        }

        private async Task UploadAndInsertAsync()
        {
            if (string.IsNullOrWhiteSpace(uploadBase64))
            {
                errorMessage = "Choose a file to upload.";
                return;
            }

            var normalizedFileName = IntranetFileHelper.NormalizeUploadFileName(uploadDisplayName);
            if (string.IsNullOrWhiteSpace(normalizedFileName))
            {
                errorMessage = "Enter a file name.";
                return;
            }

            if (Kind == ContentInsertKind.Image &&
                !IntranetFileHelper.ClassifyMimeType(uploadMimeType, normalizedFileName).Equals(IntranetFileHelper.FileTypeImage, StringComparison.Ordinal))
            {
                errorMessage = "Please choose an image file (PNG, JPG, GIF, etc.).";
                return;
            }

            var uploadPolicy = IntranetMediaPolicyService.ToPolicy(_mediaPolicy);
            if (!IntranetMediaPolicyRules.TryValidateUpload(
                    normalizedFileName,
                    IntranetFileHelper.InferMimeType(normalizedFileName, uploadMimeType),
                    uploadFileSize,
                    uploadPolicy,
                    out var uploadPolicyError))
            {
                errorMessage = uploadPolicyError;
                return;
            }

            isBusy = true;
            errorMessage = null;
            try
            {
                await EnsureGoogleConnectedAsync();
                var client = ClientFactory.CreateClient(Constants.API.ClientName);
                var body = new
                {
                    FileName = normalizedFileName,
                    MimeType = IntranetFileHelper.InferMimeType(normalizedFileName, uploadMimeType),
                    ContentBase64 = uploadBase64,
                    PageId = PageId,
                    Caption = (string?)null
                };

                var resp = await client.PostAsJsonAsync(Constants.API.Intranet.Documents.Upload, body);
                if (!resp.IsSuccessStatusCode)
                {
                    errorMessage = await resp.Content.ReadAsStringAsync();
                    return;
                }

                var result = await resp.Content.ReadFromJsonAsync<UploadLibraryDocResultDto>();
                if (result?.Document != null)
                {
                    await CloseAsync();
                    await OnFileInserted.InvokeAsync(result.Document);
                }
            }
            catch (Exception ex) when (ex.Message != "Google not connected")
            {
                errorMessage = ex.Message;
            }
            finally
            {
                isBusy = false;
            }
        }

        private async Task HandleBrowserSelectionAsync(IntranetDocumentDto doc)
        {
            await CloseAsync();
            await OnFileInserted.InvokeAsync(doc);
        }

        private async Task CreateGoogleAndInsertAsync()
        {
            var normalizedGoogleName = IntranetFileHelper.NormalizeUploadFileName(googleFileName);
            if (string.IsNullOrWhiteSpace(normalizedGoogleName))
            {
                errorMessage = "Enter a name for the new file.";
                return;
            }

            if (string.IsNullOrWhiteSpace(PageId))
            {
                errorMessage = "Save the page first before creating linked files.";
                return;
            }

            isBusy = true;
            errorMessage = null;
            try
            {
                await EnsureGoogleConnectedAsync();
                var client = ClientFactory.CreateClient(Constants.API.ClientName);
                var body = new
                {
                    Name = Path.GetFileNameWithoutExtension(normalizedGoogleName),
                    Kind = googleFileKind,
                    Caption = (string?)null
                };

                var resp = await client.PostAsJsonAsync(
                    $"{Constants.API.Intranet.Pages.Api}/{PageId}/documents/create", body);
                if (!resp.IsSuccessStatusCode)
                {
                    errorMessage = await resp.Content.ReadAsStringAsync();
                    return;
                }

                var attached = await resp.Content.ReadFromJsonAsync<IntranetPageDocumentDto>();
                if (attached != null)
                {
                    await CloseAsync();
                    await OnFileInserted.InvokeAsync(new IntranetDocumentDto
                    {
                        DocumentId = attached.DocumentId,
                        DriveFileId = attached.DriveFileId,
                        Name = attached.Name,
                        MimeType = attached.MimeType,
                        WebViewLink = attached.WebViewLink,
                        ThumbnailLink = attached.ThumbnailLink
                    });
                }
            }
            catch (Exception ex) when (ex.Message != "Google not connected")
            {
                errorMessage = ex.Message;
            }
            finally
            {
                isBusy = false;
            }
        }
    }
}