using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using My.DAL.Models;
using My.Shared.Rules;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace My.Functions.Services
{
    /// <summary>
    /// Handles Google Drive operations (create, upload, metadata) using the user's
    /// Google refresh token after Intranet Drive consent (incremental on Calendar).
    /// </summary>
    public class GoogleDriveService
    {
        private static readonly string[] DriveScopes = GoogleDriveOAuthRules.ConnectScopes;

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
        /// Admin Connect for the App Shared Drive. Full Drive scope.
        /// </summary>
        public string BuildAppDriveAuthorizationUrl(string redirectUri, string state, string? hostedDomain = null)
        {
            var flow = CreateFlow(GoogleAppDriveOAuthRules.ConnectScopes);
            var req = flow.CreateAuthorizationCodeRequest(redirectUri);
            req.State = state;
            if (req is GoogleAuthorizationCodeRequestUrl google)
            {
                google.AccessType = "offline";
                google.Prompt = "consent";
                google.IncludeGrantedScopes = "true";
            }
            return GoogleCalendarOAuthRules.AppendHostedDomainHint(req.Build().ToString(), hostedDomain);
        }

        public Task<(string? refreshToken, string? email)> ExchangeAppDriveCodeAsync(
            string code, string redirectUri, CancellationToken ct = default)
            => ExchangeCodeAsync(code, redirectUri, GoogleAppDriveOAuthRules.ConnectScopes, ct);

        /// <summary>
        /// Drive-only consent for Intranet. Uses include_granted_scopes so an existing
        /// Calendar grant is kept on the same refresh token. No login_hint.
        /// </summary>
        public string BuildAuthorizationUrl(string redirectUri, string state, string? hostedDomain = null)
        {
            var flow = CreateFlow();
            var req = flow.CreateAuthorizationCodeRequest(redirectUri);
            req.State = state;
            if (req is GoogleAuthorizationCodeRequestUrl google)
            {
                google.AccessType = "offline";
                google.Prompt = "consent";
                google.IncludeGrantedScopes = "true";
            }
            return GoogleCalendarOAuthRules.AppendHostedDomainHint(req.Build().ToString(), hostedDomain);
        }

        /// <summary>
        /// Exchanges a Drive consent code. Refresh token may be null on incremental
        /// auth when Google does not re-issue one.
        /// </summary>
        public Task<(string? refreshToken, string? email)> ExchangeCodeAsync(
            string code, string redirectUri, CancellationToken ct = default)
            => ExchangeCodeAsync(code, redirectUri, DriveScopes, ct);

        private async Task<(string? refreshToken, string? email)> ExchangeCodeAsync(
            string code, string redirectUri, string[] scopes, CancellationToken ct)
        {
            var flow = CreateFlow(scopes);
            var token = await flow.ExchangeCodeForTokenAsync(
                userId: "unused",
                code: code,
                redirectUri: redirectUri,
                taskCancellationToken: ct);

            string? email = null;
            if (!string.IsNullOrEmpty(token.IdToken))
            {
                try
                {
                    var payload = await GoogleJsonWebSignature.ValidateAsync(token.IdToken,
                        new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
                    email = payload.Email;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not validate Google id_token for Drive email extraction.");
                }
            }

            return (token.RefreshToken, email);
        }

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

        /// <summary>
        /// Renames a file and/or moves it to another folder (shared-drive aware).
        /// No-ops when the name and parent are already correct.
        /// </summary>
        public Task<DriveFile> MoveAndRenameFileAsync(
            string encryptedRefreshToken,
            string fileId,
            string? newParentId,
            string? newName,
            CancellationToken ct = default)
            => ExecuteAsync(encryptedRefreshToken, async svc =>
            {
                var get = svc.Files.Get(fileId);
                get.Fields = "id, name, parents";
                get.SupportsAllDrives = true;
                var current = await get.ExecuteAsync(ct);

                var rename = !string.IsNullOrWhiteSpace(newName)
                    && !string.Equals(current.Name, newName, StringComparison.Ordinal);
                var parents = current.Parents ?? new List<string>();
                var move = !string.IsNullOrWhiteSpace(newParentId)
                    && !parents.Contains(newParentId);

                if (!rename && !move)
                    return current;

                var body = new DriveFile();
                if (rename)
                    body.Name = newName;

                var request = svc.Files.Update(body, fileId);
                request.SupportsAllDrives = true;
                request.Fields = "id, name, parents";
                if (move)
                {
                    request.AddParents = newParentId;
                    if (parents.Count > 0)
                        request.RemoveParents = string.Join(",", parents);
                }

                return await request.ExecuteAsync(ct);
            });

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
        /// True when <paramref name="fileId"/> is <paramref name="ancestorFolderId"/>
        /// or nested under it (Shared Drive parents).
        /// </summary>
        public async Task<bool> IsUnderFolderAsync(
            string encryptedRefreshToken, string fileId, string ancestorFolderId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(ancestorFolderId))
                return false;
            if (string.Equals(fileId, ancestorFolderId, StringComparison.Ordinal))
                return true;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = fileId;
            for (var i = 0; i < DriveFolderAncestryRules.MaxWalkDepth; i++)
            {
                if (!seen.Add(current))
                    return false;
                DriveFile file;
                try
                {
                    file = await GetFileAsync(encryptedRefreshToken, current, ct);
                }
                catch (Google.GoogleApiException ex) when (
                    ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound
                    || ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return false;
                }

                var parents = file.Parents;
                if (parents == null || parents.Count == 0)
                    return false;
                if (DriveFolderAncestryRules.DirectParentIsAncestor(parents, ancestorFolderId))
                    return true;
                current = parents[0];
            }

            return false;
        }

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
        public async Task<Google.Apis.Drive.v3.Data.Drive?> FindSharedDriveByNameAsync(
            string encryptedRefreshToken, string name, CancellationToken ct = default)
        {
            var svc = await CreateServiceAsync(encryptedRefreshToken);
            string? pageToken = null;
            do
            {
                var request = svc.Drives.List();
                request.PageSize = 100;
                if (!string.IsNullOrEmpty(pageToken))
                    request.PageToken = pageToken;
                var page = await request.ExecuteAsync(ct);
                var match = page.Drives?.FirstOrDefault(d =>
                    string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
                pageToken = page.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));
            return null;
        }

        public async Task<Google.Apis.Drive.v3.Data.Drive?> GetSharedDriveAsync(
            string encryptedRefreshToken, string driveId, CancellationToken ct = default)
        {
            var id = AppDriveLayoutRules.ParseDriveId(driveId);
            if (string.IsNullOrEmpty(id))
                return null;

            var svc = await CreateServiceAsync(encryptedRefreshToken);
            try
            {
                var request = svc.Drives.Get(id);
                request.Fields = "id,name,restrictions";
                return await request.ExecuteAsync(ct);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException(AppDriveLayoutRules.NotFoundMessage, ex);
            }
        }

        /// <summary>
        /// Uses the workspace Shared Drive by ID, then by name. Does not create a new Drive.
        /// </summary>
        public async Task<Google.Apis.Drive.v3.Data.Drive> ResolveSharedDriveAsync(
            string encryptedRefreshToken,
            string preferredId,
            string name,
            CancellationToken ct = default)
        {
            var byId = await GetSharedDriveAsync(encryptedRefreshToken, preferredId, ct);
            if (byId != null)
                return byId;

            var byName = await FindSharedDriveByNameAsync(encryptedRefreshToken, name, ct);
            if (byName != null)
                return byName;

            throw new InvalidOperationException(AppDriveLayoutRules.NotFoundMessage);
        }

        /// <summary>
        /// Applies the locked Shared Drive restrictions, including contributor download
        /// lock (<c>downloadRestriction.restrictedForWriters</c>) which the typed 1.68
        /// client does not model.
        /// </summary>
        public async Task ApplySharedDriveRestrictionsAsync(
            string encryptedRefreshToken, string driveId, CancellationToken ct = default)
        {
            var id = AppDriveLayoutRules.ParseDriveId(driveId);
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException(AppDriveLayoutRules.NotFoundMessage);

            var json = """
                {
                  "restrictions": {
                    "domainUsersOnly": true,
                    "driveMembersOnly": true,
                    "sharingFoldersRequiresOrganizerPermission": true,
                    "copyRequiresWriterPermission": true,
                    "downloadRestriction": {
                      "restrictedForReaders": true,
                      "restrictedForWriters": true
                    }
                  }
                }
                """;
            await SendDriveJsonAsync(encryptedRefreshToken, HttpMethod.Patch, id, json, ct);
        }

        public async Task<SharedDriveInfo?> GetSharedDriveInfoAsync(
            string encryptedRefreshToken, string driveId, CancellationToken ct = default)
        {
            var id = AppDriveLayoutRules.ParseDriveId(driveId);
            if (string.IsNullOrEmpty(id))
                return null;

            try
            {
                var json = await SendDriveJsonAsync(encryptedRefreshToken, HttpMethod.Get, id, body: null, ct);
                return SharedDriveInfo.Parse(json);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException(AppDriveLayoutRules.NotFoundMessage, ex);
            }
        }

        private async Task<string> SendDriveJsonAsync(
            string encryptedRefreshToken,
            HttpMethod method,
            string driveId,
            string? body,
            CancellationToken ct)
        {
            var svc = await CreateServiceAsync(encryptedRefreshToken);
            var url =
                $"https://www.googleapis.com/drive/v3/drives/{Uri.EscapeDataString(driveId)}?fields=id,name,restrictions";
            using var request = new HttpRequestMessage(method, url);
            if (body != null)
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await svc.HttpClient.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Drive {Method} {DriveId} failed: {Status} {Body}",
                    method, driveId, (int)response.StatusCode, json);
                response.EnsureSuccessStatusCode();
            }

            return json;
        }

        public async Task<DriveFile?> FindChildFolderAsync(
            string encryptedRefreshToken, string parentId, string name, CancellationToken ct = default)
        {
            var escapedName = EscapeDriveQueryValue(name);
            var escapedParent = EscapeDriveQueryValue(parentId);
            var query =
                $"name = '{escapedName}' and '{escapedParent}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            var matches = await ListFilesAsync(encryptedRefreshToken, query, pageSize: 10, ct: ct);
            return matches.FirstOrDefault();
        }

        /// <summary>Any child (file or folder) with this exact name under parent.</summary>
        public async Task<DriveFile?> FindChildByNameAsync(
            string encryptedRefreshToken, string parentId, string name, CancellationToken ct = default)
        {
            var escapedName = EscapeDriveQueryValue(name);
            var escapedParent = EscapeDriveQueryValue(parentId);
            var query =
                $"name = '{escapedName}' and '{escapedParent}' in parents and trashed = false";
            var matches = await ListFilesAsync(encryptedRefreshToken, query, pageSize: 1, ct: ct);
            return matches.FirstOrDefault();
        }

        public async Task<DriveFile> FindOrCreateFolderAsync(
            string encryptedRefreshToken, string parentId, string name, CancellationToken ct = default)
        {
            var existing = await FindChildFolderAsync(encryptedRefreshToken, parentId, name, ct);
            if (existing != null)
                return existing;
            return await CreateFileAsync(
                encryptedRefreshToken, name, "application/vnd.google-apps.folder", parentId, ct);
        }

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
                request.Corpora = "allDrives";
                if (!string.IsNullOrEmpty(query)) request.Q = query;
                if (!string.IsNullOrEmpty(currentPageToken)) request.PageToken = currentPageToken;

                var page = await request.ExecuteAsync(ct);
                if (page.Files != null) results.AddRange(page.Files);
                currentPageToken = page.NextPageToken;
            }
            while (!string.IsNullOrEmpty(currentPageToken) && results.Count < 1000); // safety cap

            return results;
        }

        private static string EscapeDriveQueryValue(string value) =>
            value.Replace("\\", "\\\\").Replace("'", "\\'");

        private async Task<T> ExecuteAsync<T>(string encryptedRefreshToken, Func<DriveService, Task<T>> work)
        {
            var svc = await CreateServiceAsync(encryptedRefreshToken);
            return await work(svc);
        }

        private async Task<DriveService> CreateServiceAsync(string encryptedRefreshToken)
        {
            var refresh = encryptor.Decrypt(encryptedRefreshToken);
            // Do not send Intranet drive.file scopes on refresh. App Drive is the
            // App Shared Drive Shared Drive (full `drive` grant); passing employee
            // scopes here lets Google downscope that token and later 403 folder work.
            var flow = CreateRefreshFlow();

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

        private GoogleAuthorizationCodeFlow CreateFlow() => CreateFlow(DriveScopes);

        private GoogleAuthorizationCodeFlow CreateRefreshFlow()
        {
            if (!IsConfigured)
                throw new InvalidOperationException("Google__ClientId / Google__ClientSecret not configured.");

            // Omit Scopes entirely (do not send []). Google keeps the original grant —
            // full drive for App Drive Shared Drive, drive.file for leftover Intranet tokens.
            return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret }
            });
        }

        private GoogleAuthorizationCodeFlow CreateFlow(string[] scopes)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("Google__ClientId / Google__ClientSecret not configured.");

            return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
                Scopes = scopes
            });
        }
    }

    public sealed class SharedDriveInfo
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public bool? DomainUsersOnly { get; init; }
        public bool? DriveMembersOnly { get; init; }
        public bool? SharingFoldersRequiresOrganizerPermission { get; init; }
        public bool? CopyRequiresWriterPermission { get; init; }
        public bool? RestrictedForWriters { get; init; }

        public static SharedDriveInfo Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement restrictions = default;
            var hasRestrictions = root.TryGetProperty("restrictions", out restrictions);

            return new SharedDriveInfo
            {
                Id = root.TryGetProperty("id", out var id) ? id.GetString() : null,
                Name = root.TryGetProperty("name", out var name) ? name.GetString() : null,
                DomainUsersOnly = hasRestrictions ? ReadBool(restrictions, "domainUsersOnly") : null,
                DriveMembersOnly = hasRestrictions ? ReadBool(restrictions, "driveMembersOnly") : null,
                SharingFoldersRequiresOrganizerPermission = hasRestrictions
                    ? ReadBool(restrictions, "sharingFoldersRequiresOrganizerPermission")
                    : null,
                CopyRequiresWriterPermission = hasRestrictions
                    ? ReadBool(restrictions, "copyRequiresWriterPermission")
                    : null,
                RestrictedForWriters = hasRestrictions ? ReadWriterDownloadRestriction(restrictions) : null
            };
        }

        private static bool? ReadWriterDownloadRestriction(JsonElement restrictions)
        {
            if (!restrictions.TryGetProperty("downloadRestriction", out var download))
                return null;
            return ReadBool(download, "restrictedForWriters");
        }

        private static bool? ReadBool(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value))
                return null;
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
    }
}