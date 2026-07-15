using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using My.DAL.Models;

using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace My.Functions.Services
{
    /// <summary>
    /// Handles Google Drive operations (create, upload, metadata) using the user's refresh token
    /// from the existing Google Calendar connect flow. This enables easy "create Google doc or upload"
    /// directly from the intranet page builder.
    /// </summary>
    public class GoogleDriveService
    {
        private static readonly string[] DriveScopes = new[]
        {
            DriveService.Scope.DriveFile,
            DriveService.Scope.DriveReadonly, // Browse/attach existing files in the company shared Drive folder
            "https://www.googleapis.com/auth/userinfo.email",
            "openid"
        };

        private readonly GoogleTokenEncryptor encryptor;
        private readonly ILogger<GoogleDriveService> logger;
        private readonly string clientId;
        private readonly string clientSecret;

        public GoogleDriveService(GoogleTokenEncryptor encryptor, ILogger<GoogleDriveService> logger)
        {
            this.encryptor = encryptor;
            this.logger = logger;
            clientId = Environment.GetEnvironmentVariable("Google__ClientId") ?? "";
            clientSecret = Environment.GetEnvironmentVariable("Google__ClientSecret") ?? "";
        }

        public bool IsConfigured => !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret);

        /// <summary>
        /// Creates a new Google Drive file (e.g. Google Doc, Sheet, Slide) using the user's token.
        /// The file is owned by the user but can be placed in a parent folder if provided.
        /// Returns the created File metadata.
        /// </summary>
        public Task<DriveFile> CreateFileAsync(string encryptedRefreshToken, string name, string mimeType, string? parentFolderId = null, CancellationToken ct = default)
            => ExecuteAsync(encryptedRefreshToken, async svc =>
            {
                var fileMetadata = new DriveFile
                {
                    Name = name,
                    MimeType = mimeType
                };

                if (!string.IsNullOrEmpty(parentFolderId))
                {
                    fileMetadata.Parents = new[] { parentFolderId };
                }

                var request = svc.Files.Create(fileMetadata);
                request.Fields = "id, name, mimeType, webViewLink, thumbnailLink, size, modifiedTime, owners, parents";
                request.SupportsAllDrives = true;
                return await request.ExecuteAsync(ct);
            });

        /// <summary>
        /// Uploads a local file to Google Drive as a new file (e.g. PDF, DOCX, image).
        /// Returns the created File metadata.
        /// </summary>
        public Task<DriveFile> UploadFileAsync(string encryptedRefreshToken, Stream content, string fileName, string mimeType, string? parentFolderId = null, CancellationToken ct = default)
            => ExecuteAsync(encryptedRefreshToken, async svc =>
            {
                var fileMetadata = new DriveFile
                {
                    Name = fileName
                };

                if (!string.IsNullOrEmpty(parentFolderId))
                {
                    fileMetadata.Parents = new[] { parentFolderId };
                }

                var request = svc.Files.Create(fileMetadata, content, mimeType);
                request.Fields = "id, name, mimeType, webViewLink, thumbnailLink, size, modifiedTime, owners, parents";
                request.SupportsAllDrives = true;
                return await request.UploadAsync(ct).ContinueWith(t => request.ResponseBody);
            });

        /// <summary>
        /// Downloads file bytes for streaming to authenticated intranet clients (e.g. private Drive images).
        /// </summary>
        public async Task<(byte[] Content, string MimeType)> DownloadFileContentAsync(
            string encryptedRefreshToken, string fileId, CancellationToken ct = default)
        {
            var svc = await CreateServiceAsync(encryptedRefreshToken);
            var metaRequest = svc.Files.Get(fileId);
            metaRequest.Fields = "mimeType";
            metaRequest.SupportsAllDrives = true;
            var meta = await metaRequest.ExecuteAsync(ct);

            var request = svc.Files.Get(fileId);
            request.SupportsAllDrives = true;
            using var stream = new MemoryStream();
            await request.DownloadAsync(stream, ct);
            return (stream.ToArray(), meta.MimeType ?? "application/octet-stream");
        }

        /// <summary>Permanently deletes a Drive file the caller can access (shared-drive aware).</summary>
        public Task DeleteFileAsync(string encryptedRefreshToken, string fileId, CancellationToken ct = default)
            => ExecuteAsync(encryptedRefreshToken, async svc =>
            {
                var request = svc.Files.Delete(fileId);
                request.SupportsAllDrives = true;
                await request.ExecuteAsync(ct);
                return 0;
            });

        /// <summary>
        /// Gets metadata for a specific Drive file (by ID). The user must have access via their token.
        /// </summary>
        public Task<DriveFile> GetFileAsync(string encryptedRefreshToken, string fileId, CancellationToken ct = default)
            => ExecuteAsync(encryptedRefreshToken, async svc =>
            {
                var request = svc.Files.Get(fileId);
                request.Fields = "id, name, mimeType, webViewLink, thumbnailLink, size, modifiedTime, owners, parents";
                request.SupportsAllDrives = true;
                return await request.ExecuteAsync(ct);
            });

        /// <summary>Lists immediate child folders under a Drive folder.</summary>
        public async Task<List<DriveFile>> ListChildFoldersAsync(
            string encryptedRefreshToken, string parentFolderId, CancellationToken ct = default)
        {
            var escaped = parentFolderId.Replace("'", "\\'");
            var query =
                $"'{escaped}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            return await ListFilesAsync(encryptedRefreshToken, query, pageSize: 100, ct: ct);
        }

        /// <summary>
        /// Lists files the user has access to (simple query support). Used for picker/attach existing.
        /// </summary>
        public async Task<List<DriveFile>> ListFilesAsync(string encryptedRefreshToken, string? query = null, string? pageToken = null, int pageSize = 50, CancellationToken ct = default)
        {
            var svc = await CreateServiceAsync(encryptedRefreshToken);
            var results = new List<DriveFile>();
            string? currentPageToken = pageToken;

            do
            {
                var request = svc.Files.List();
                request.PageSize = pageSize;
                request.Fields = "nextPageToken, files(id, name, mimeType, webViewLink, thumbnailLink, size, modifiedTime, owners)";
                request.SupportsAllDrives = true;
                request.IncludeItemsFromAllDrives = true;
                if (!string.IsNullOrEmpty(query)) request.Q = query;
                if (!string.IsNullOrEmpty(currentPageToken)) request.PageToken = currentPageToken;

                var page = await request.ExecuteAsync(ct);
                if (page.Files != null) results.AddRange(page.Files);
                currentPageToken = page.NextPageToken;
            }
            while (!string.IsNullOrEmpty(currentPageToken) && results.Count < 1000); // safety cap

            return results;
        }

        private async Task<T> ExecuteAsync<T>(string encryptedRefreshToken, Func<DriveService, Task<T>> work)
        {
            var svc = await CreateServiceAsync(encryptedRefreshToken);
            return await work(svc);
        }

        private async Task<DriveService> CreateServiceAsync(string encryptedRefreshToken)
        {
            var refresh = encryptor.Decrypt(encryptedRefreshToken);
            var flow = CreateFlow();

            var token = new TokenResponse { RefreshToken = refresh };
            var credential = new UserCredential(flow, "user", token);

            // Force-refresh so we always have a valid access token
            await credential.RefreshTokenAsync(CancellationToken.None);

            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "My.Workspace Intranet"
            });
        }

        private GoogleAuthorizationCodeFlow CreateFlow()
        {
            if (!IsConfigured)
                throw new InvalidOperationException("Google__ClientId / Google__ClientSecret not configured.");

            return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
                Scopes = DriveScopes
            });
        }
    }
}