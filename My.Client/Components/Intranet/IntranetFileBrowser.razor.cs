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
    public enum IntranetFileBrowserMode
    {
        Library,
        Picker
    }

    public partial class IntranetFileBrowser
    {
        [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
        [Inject] private ISnackbar Snackbar { get; set; } = null!;
        [Inject] private NavigationManager Navigation { get; set; } = null!;
        [Inject] private IDialogService DialogService { get; set; } = null!;
        [Inject] private IntranetMediaPolicyService MediaPolicy { get; set; } = null!;

        private IntranetMediaPolicyDto _mediaPolicy = new();

        [Parameter] public IntranetFileBrowserMode Mode { get; set; } = IntranetFileBrowserMode.Library;
        [Parameter] public string? PageId { get; set; }
        [Parameter] public EventCallback<IntranetDocumentDto> OnDocumentSelected { get; set; }
        /// <summary>When set, locks the type filter (e.g. image-only picker).</summary>
        [Parameter] public string? FixedFileTypeFilter { get; set; }
        [Parameter] public bool HideUploadButton { get; set; }


        private List<IntranetDriveLibraryItemDto> files = new();
        /// <summary>Start true so Library mode paints PageLoader on first frame (Tyme-style).</summary>
        private bool isLoading = true;
        private bool isBusy;

        private string search = "";
        private string fileTypeFilter = IntranetFileHelper.FileTypeAll;
        private string sortBy = "name";
        private string sortDir = "asc";

        private bool showUploadDialog;
        private bool showEditDialog;
        private IntranetDriveLibraryItemDto? editingFile;
        private UpdateIntranetDocumentDto editModel = new();

        private string? uploadFileName;
        private string uploadDisplayName = "";
        private long uploadFileSize;
        private string? uploadBase64;
        private string? uploadMimeType;
        private bool CanUploadWithCurrentFileName =>
            !string.IsNullOrWhiteSpace(uploadDisplayName) &&
            IntranetFileHelper.IsValidUploadFileName(IntranetFileHelper.NormalizeUploadFileName(uploadDisplayName));

        private bool uploadFileNameInvalid =>
            !string.IsNullOrWhiteSpace(uploadDisplayName) &&
            !IntranetFileHelper.IsValidUploadFileName(IntranetFileHelper.NormalizeUploadFileName(uploadDisplayName));

        private Dictionary<string, object> UploadNameInputAttributes => new()
        {
            ["onfocusout"] = EventCallback.Factory.Create(this, NormalizeUploadDisplayNameOnBlur)
        };

        private string? UploadPreviewDataUrl =>
            string.IsNullOrEmpty(uploadBase64)
                ? null
                : $"data:{IntranetFileHelper.InferMimeType(uploadDisplayName, uploadMimeType)};base64,{uploadBase64}";

        private bool ShowImageUploadPreview =>
            !string.IsNullOrEmpty(uploadBase64)
            && IntranetFileHelper.ClassifyMimeType(uploadMimeType, uploadDisplayName) == IntranetFileHelper.FileTypeImage;

        private void NormalizeUploadDisplayNameOnBlur() =>
            uploadDisplayName = IntranetFileHelper.NormalizeUploadFileName(uploadDisplayName);

        private void ClearUploadSelection()
        {
            uploadFileName = null;
            uploadDisplayName = "";
            uploadBase64 = null;
            uploadFileSize = 0;
            uploadMimeType = null;
        }

        private static readonly (string Value, string Label)[] FileTypeOptions =
        {
            (IntranetFileHelper.FileTypeAll, "All types"),
            (IntranetFileHelper.FileTypeImage, "Images"),
            (IntranetFileHelper.FileTypeDocument, "Documents"),
            (IntranetFileHelper.FileTypeVideo, "Videos")
        };

        private static readonly (string Value, string Label)[] SortOptions =
        {
            ("name", "Name"),
            ("modified", "Last modified"),
            ("featured", "Featured"),
            ("usage", "Used on pages"),
            ("size", "Size")
        };

        protected override async Task OnInitializedAsync()
        {
            if (!string.IsNullOrWhiteSpace(FixedFileTypeFilter))
                fileTypeFilter = FixedFileTypeFilter!;
            _mediaPolicy = await MediaPolicy.GetAsync();
            await LoadFilesAsync();
        }

        private async Task LoadFilesAsync()
        {
            isLoading = true;
            try
            {
                var client = ClientFactory.CreateClient(Constants.API.ClientName);
                var parts = new List<string> { "includeUsage=true" };
                if (!string.IsNullOrWhiteSpace(search)) parts.Add($"search={Uri.EscapeDataString(search.Trim())}");
                if (!string.IsNullOrWhiteSpace(fileTypeFilter)) parts.Add($"fileType={Uri.EscapeDataString(fileTypeFilter)}");
                parts.Add($"sort={Uri.EscapeDataString(sortBy)}");
                parts.Add($"sortDir={Uri.EscapeDataString(sortDir)}");
                var url = $"{Constants.API.Intranet.Documents.DriveBrowse}?{string.Join("&", parts)}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(FormatDriveBrowseError(err), Severity.Error);
                    files = new();
                    return;
                }

                files = await response.Content.ReadFromJsonAsync<List<IntranetDriveLibraryItemDto>>() ?? new();
            }
            catch (Exception ex)
            {
                Snackbar.Add(FormatDriveBrowseError(ex.Message), Severity.Error);
                files = new();
            }
            finally
            {
                isLoading = false;
            }
        }

        private static string FormatDriveBrowseError(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "Failed to load files from Google Drive.";

            if (message.Contains("Drive API has not been used", StringComparison.OrdinalIgnoreCase)
                || message.Contains("accessNotConfigured", StringComparison.OrdinalIgnoreCase))
            {
                return "Google Drive API is not enabled for this app. In Google Cloud Console, enable the Drive API for your OAuth project, wait a few minutes, then reconnect Google in Settings if needed.";
            }

            if (message.Contains("Intranet Drive folder", StringComparison.OrdinalIgnoreCase)
                || message.Contains("File not found", StringComparison.OrdinalIgnoreCase)
                || message.Contains("notFound", StringComparison.OrdinalIgnoreCase))
            {
                return "The Intranet Drive folder in App Settings was not found or you no longer have access. " +
                       "Set Intranet Drive Parent Folder ID to the root Intranet folder you can open in Drive.";
            }

            if (message.StartsWith("Failed to browse Drive folder:", StringComparison.OrdinalIgnoreCase))
                return message;

            return $"Failed to load files: {message}";
        }

        private async Task ApplyFiltersAsync() => await LoadFilesAsync();

        private async Task OnFileTypeFilterChanged(string value)
        {
            fileTypeFilter = value;
            await ApplyFiltersAsync();
        }

        private async Task OnSortByChanged(string value)
        {
            sortBy = value;
            await ApplyFiltersAsync();
        }

        private async Task ToggleSortDirectionAsync()
        {
            sortDir = sortDir == "desc" ? "asc" : "desc";
            await ApplyFiltersAsync();
        }

        private async Task SelectFileAsync(IntranetDriveLibraryItemDto file)
        {
            isBusy = true;
            try
            {
                var client = ClientFactory.CreateClient(Constants.API.ClientName);
                IntranetDocumentDto doc;

                if (!string.IsNullOrWhiteSpace(PageId))
                {
                    var attachResp = await client.PostAsJsonAsync(
                        $"{Constants.API.Intranet.Pages.Api}/{PageId}/documents",
                        new
                        {
                            DriveFileId = file.DriveFileId,
                            Name = file.Name,
                            MimeType = file.MimeType,
                            WebViewLink = file.WebViewLink,
                            ThumbnailLink = file.ThumbnailLink,
                            SizeBytes = file.SizeBytes,
                            DriveLastModified = file.DriveLastModified
                        });
                    if (!attachResp.IsSuccessStatusCode)
                    {
                        Snackbar.Add(await attachResp.Content.ReadAsStringAsync(), Severity.Error);
                        return;
                    }

                    var attached = await attachResp.Content.ReadFromJsonAsync<IntranetPageDocumentDto>();
                    doc = IntranetFileHelper.ToDocumentDto(file);
                    if (attached != null)
                        doc.DocumentId = attached.DocumentId;
                }
                else if (string.IsNullOrWhiteSpace(file.DocumentId))
                {
                    var regResp = await client.PostAsJsonAsync(
                        Constants.API.Intranet.Documents.Register,
                        new CreateIntranetDocumentDto
                        {
                            DriveFileId = file.DriveFileId,
                            Name = file.Name,
                            MimeType = file.MimeType,
                            WebViewLink = file.WebViewLink,
                            ThumbnailLink = file.ThumbnailLink,
                            SizeBytes = file.SizeBytes,
                            DriveLastModified = file.DriveLastModified,
                            DriveOwnerEmail = file.DriveOwnerEmail,
                            DriveOwnerName = file.DriveOwnerName,
                            Category = file.Category,
                            Description = file.Description,
                            IsFeatured = file.IsFeatured
                        });
                    if (!regResp.IsSuccessStatusCode)
                    {
                        Snackbar.Add(await regResp.Content.ReadAsStringAsync(), Severity.Error);
                        return;
                    }
                    doc = await regResp.Content.ReadFromJsonAsync<IntranetDocumentDto>() ?? IntranetFileHelper.ToDocumentDto(file);
                }
                else
                {
                    doc = IntranetFileHelper.ToDocumentDto(file);
                }

                if (Mode == IntranetFileBrowserMode.Picker)
                    await OnDocumentSelected.InvokeAsync(doc);
                else
                    Snackbar.Add($"Selected \"{file.Name}\".", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add(ex.Message, Severity.Error);
            }
            finally
            {
                isBusy = false;
            }
        }

        private void OpenEditDialog(IntranetDriveLibraryItemDto file)
        {
            editingFile = file;
            editModel = new UpdateIntranetDocumentDto
            {
                Name = file.Name,
                IsFeatured = file.IsFeatured
            };
            showEditDialog = true;
        }

        private async Task SaveEditAsync()
        {
            if (editingFile == null) return;
            isBusy = true;
            try
            {
                var client = ClientFactory.CreateClient(Constants.API.ClientName);
                IntranetDocumentDto? saved;

                if (string.IsNullOrWhiteSpace(editingFile.DocumentId))
                {
                    var regResp = await client.PostAsJsonAsync(
                        Constants.API.Intranet.Documents.Register,
                        new CreateIntranetDocumentDto
                        {
                            DriveFileId = editingFile.DriveFileId,
                            Name = editModel.Name ?? editingFile.Name,
                            Description = editModel.Description,
                            Category = editModel.Category,
                            IsFeatured = editModel.IsFeatured ?? false
                        });
                    if (!regResp.IsSuccessStatusCode)
                    {
                        Snackbar.Add(await regResp.Content.ReadAsStringAsync(), Severity.Error);
                        return;
                    }
                    saved = await regResp.Content.ReadFromJsonAsync<IntranetDocumentDto>();
                    if (saved != null && !string.IsNullOrWhiteSpace(editModel.Name) && editModel.Name != saved.Name)
                    {
                        var updateResp = await client.PutAsJsonAsync(
                            $"{Constants.API.Intranet.Documents.GetById}{saved.DocumentId}",
                            editModel);
                        if (!updateResp.IsSuccessStatusCode)
                        {
                            Snackbar.Add(await updateResp.Content.ReadAsStringAsync(), Severity.Error);
                            return;
                        }
                        saved = await updateResp.Content.ReadFromJsonAsync<IntranetDocumentDto>();
                    }
                }
                else
                {
                    var resp = await client.PutAsJsonAsync(
                        $"{Constants.API.Intranet.Documents.GetById}{editingFile.DocumentId}",
                        editModel);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Snackbar.Add(await resp.Content.ReadAsStringAsync(), Severity.Error);
                        return;
                    }
                    saved = await resp.Content.ReadFromJsonAsync<IntranetDocumentDto>();
                }

                showEditDialog = false;
                Snackbar.Add("File details updated.", Severity.Success);
                await LoadFilesAsync();
            }
            catch (Exception ex)
            {
                Snackbar.Add(ex.Message, Severity.Error);
            }
            finally
            {
                isBusy = false;
            }
        }

        private async Task DeleteLibraryFileAsync(IntranetDriveLibraryItemDto file)
        {
            if (string.IsNullOrWhiteSpace(file.DriveFileId)) return;

            var message = IntranetFileHelper.BuildLibraryDeleteMessage(file.Name, file.UsedOnPages);

            var confirm = await DialogService.ShowMessageBoxAsync(
                "Delete file?",
                message,
                yesText: "Delete", cancelText: "Cancel");
            if (confirm != true) return;

            isBusy = true;
            try
            {
                var client = ClientFactory.CreateClient(Constants.API.ClientName);
                var url = !string.IsNullOrWhiteSpace(file.DocumentId)
                    ? $"{Constants.API.Intranet.Documents.GetById}{file.DocumentId}"
                    : $"{Constants.API.Intranet.Documents.DeleteDrive}{Uri.EscapeDataString(file.DriveFileId)}";
                var resp = await client.DeleteAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    Snackbar.Add(await resp.Content.ReadAsStringAsync(), Severity.Error);
                    return;
                }

                var result = await resp.Content.ReadFromJsonAsync<PurgeIntranetDocumentResultDto>();
                var detail = result?.PagesStripped > 0
                    ? $" Removed from {result.PagesStripped} page{(result.PagesStripped == 1 ? "" : "s")}."
                    : string.Empty;
                Snackbar.Add($"\"{file.Name}\" deleted from Drive and library.{detail}", Severity.Success);
                await LoadFilesAsync();
            }
            catch (Exception ex)
            {
                Snackbar.Add(ex.Message, Severity.Error);
            }
            finally
            {
                isBusy = false;
            }
        }

        private void OpenUploadDialog()
        {
            uploadFileName = null;
            uploadDisplayName = "";
            uploadBase64 = null;
            uploadFileSize = 0;
            uploadMimeType = null;
            showUploadDialog = true;
        }

        private Task OnUploadFileSelected(InputFileChangeEventArgs e) => Task.CompletedTask;

        private async Task ApplyUploadFileAsync(FileDropPayload payload)
        {
            uploadFileName = payload.FileName;
            uploadDisplayName = IntranetFileHelper.SuggestUploadFileName(payload.FileName);
            uploadFileSize = payload.Size;
            uploadMimeType = IntranetFileHelper.InferMimeType(payload.FileName, payload.ContentType);
            uploadBase64 = payload.Base64;

            var policy = IntranetMediaPolicyService.ToPolicy(_mediaPolicy);
            if (!IntranetMediaPolicyRules.TryValidateUpload(
                    uploadDisplayName,
                    uploadMimeType,
                    uploadFileSize,
                    policy,
                    out var policyError))
            {
                Snackbar.Add(policyError ?? "File is not allowed.", Severity.Warning);
                uploadBase64 = null;
                uploadFileName = null;
                uploadDisplayName = "";
                uploadFileSize = 0;
                uploadMimeType = null;
            }

            await InvokeAsync(StateHasChanged);
        }

        private async Task ExecuteUploadAsync()
        {
            if (string.IsNullOrWhiteSpace(uploadBase64)) return;

            var normalizedFileName = IntranetFileHelper.NormalizeUploadFileName(uploadDisplayName);
            if (string.IsNullOrWhiteSpace(normalizedFileName))
            {
                Snackbar.Add("Enter a file name.", Severity.Error);
                return;
            }

            var policy = IntranetMediaPolicyService.ToPolicy(_mediaPolicy);
            if (!IntranetMediaPolicyRules.TryValidateUpload(
                    normalizedFileName,
                    IntranetFileHelper.InferMimeType(normalizedFileName, uploadMimeType),
                    uploadFileSize,
                    policy,
                    out var policyError))
            {
                Snackbar.Add(policyError ?? "File is not allowed.", Severity.Warning);
                return;
            }

            isBusy = true;
            try
            {
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
                    Snackbar.Add(await resp.Content.ReadAsStringAsync(), Severity.Error);
                    return;
                }

                var result = await resp.Content.ReadFromJsonAsync<UploadLibraryDocResultDto>();
                showUploadDialog = false;

                if (result?.Document != null && Mode == IntranetFileBrowserMode.Picker)
                    await OnDocumentSelected.InvokeAsync(result.Document);
                else
                    Snackbar.Add($"Uploaded \"{normalizedFileName}\".", Severity.Success);

                await LoadFilesAsync();
            }
            catch (Exception ex)
            {
                Snackbar.Add(ex.Message, Severity.Error);
            }
            finally
            {
                isBusy = false;
            }
        }

        private void OpenDriveLink(string? url)
        {
            if (!string.IsNullOrEmpty(url))
                Navigation.NavigateTo(url, true);
        }

        private string GetTypeIcon(string? mimeType)
        {
            return IntranetFileHelper.ClassifyMimeType(mimeType) switch
            {
                IntranetFileHelper.FileTypeImage => Icons.Material.Filled.Image,
                IntranetFileHelper.FileTypeVideo => Icons.Material.Filled.VideoFile,
                IntranetFileHelper.FileTypeFolder => Icons.Material.Filled.Folder,
                _ => Icons.Material.Filled.Description
            };
        }
    }
}