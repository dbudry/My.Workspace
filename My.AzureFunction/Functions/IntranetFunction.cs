using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.Functions.Helpers;
using My.Shared;
using My.Shared.Constants;
using My.Shared.Dtos.Intranet;
using My.Shared.Rules;
using My.Shared.Validation;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Repository;
using My.Functions.Authorization;
using My.Functions.Services;
using System.Net;
using System.Net.Http.Headers;

namespace My.Functions
{
    /// <summary>
    /// Intranet module endpoints: pages + curated Drive documents + admin-managed navigation.
    /// The Drive-integrated actions (create Google native file, upload, attach-by-id) are the
    /// core of the "make the user's job easier" UX so page builders never have to leave the
    /// editor to add/reference company docs stored in our Google Drive.
    /// </summary>
    public class IntranetFunction
    {
        private readonly ApplicationDbContext dbContext;
        private readonly IRepositoryFactory repositoryFactory;
        private readonly GoogleDriveService drive;
        private readonly GoogleTokenEncryptor encryptor;
        private readonly ILogger<IntranetFunction> logger;
        private readonly IValidator<CreateIntranetPageDto> createPageValidator;
        private readonly IValidator<UpdateIntranetPageDto> updatePageValidator;
        private readonly IValidator<CreateGoogleDocRequest> createGoogleDocValidator;
        private readonly IValidator<UploadDocRequest> uploadDocValidator;
        private readonly IValidator<AttachExistingRequest> attachExistingValidator;
        private readonly ReorderPagesRequestValidator reorderPagesValidator;
        private readonly ReorderRequestValidator reorderNavValidator;
        private readonly IValidator<MovePageRequest> movePageValidator;
        private readonly IValidator<CreateIntranetNavigationItemDto> createNavValidator;
        private readonly IValidator<UpdateIntranetNavigationItemDto> updateNavValidator;
        private readonly IValidator<CreateIntranetDocumentDto> createDocumentValidator;
        private readonly IValidator<UpdateIntranetDocumentDto> updateDocumentValidator;
        private readonly IValidator<UploadLibraryDocRequest> uploadLibraryDocValidator;
        private readonly IValidator<FetchExternalImageRequest> fetchExternalImageValidator;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly DriveFileIdValidator driveFileIdValidator;

        public IntranetFunction(
            ApplicationDbContext dbContext,
            IRepositoryFactory repositoryFactory,
            GoogleDriveService drive,
            GoogleTokenEncryptor encryptor,
            ILogger<IntranetFunction> logger,
            IValidator<CreateIntranetPageDto> createPageValidator,
            IValidator<UpdateIntranetPageDto> updatePageValidator,
            IValidator<CreateGoogleDocRequest> createGoogleDocValidator,
            IValidator<UploadDocRequest> uploadDocValidator,
            IValidator<AttachExistingRequest> attachExistingValidator,
            ReorderPagesRequestValidator reorderPagesValidator,
            ReorderRequestValidator reorderNavValidator,
            IValidator<MovePageRequest> movePageValidator,
            IValidator<CreateIntranetNavigationItemDto> createNavValidator,
            IValidator<UpdateIntranetNavigationItemDto> updateNavValidator,
            IValidator<CreateIntranetDocumentDto> createDocumentValidator,
            IValidator<UpdateIntranetDocumentDto> updateDocumentValidator,
            IValidator<UploadLibraryDocRequest> uploadLibraryDocValidator,
            IValidator<FetchExternalImageRequest> fetchExternalImageValidator,
            IHttpClientFactory httpClientFactory,
            DriveFileIdValidator driveFileIdValidator)
        {
            this.dbContext = dbContext;
            this.repositoryFactory = repositoryFactory;
            this.drive = drive;
            this.encryptor = encryptor;
            this.logger = logger;
            this.createPageValidator = createPageValidator;
            this.updatePageValidator = updatePageValidator;
            this.createGoogleDocValidator = createGoogleDocValidator;
            this.uploadDocValidator = uploadDocValidator;
            this.attachExistingValidator = attachExistingValidator;
            this.reorderPagesValidator = reorderPagesValidator;
            this.reorderNavValidator = reorderNavValidator;
            this.movePageValidator = movePageValidator;
            this.createNavValidator = createNavValidator;
            this.updateNavValidator = updateNavValidator;
            this.createDocumentValidator = createDocumentValidator;
            this.updateDocumentValidator = updateDocumentValidator;
            this.uploadLibraryDocValidator = uploadLibraryDocValidator;
            this.fetchExternalImageValidator = fetchExternalImageValidator;
            this.httpClientFactory = httpClientFactory;
            this.driveFileIdValidator = driveFileIdValidator;
        }

        // ---------- Page + Drive document actions (the approved "next" UX focus) ----------

        /// <summary>
        /// Page slugs are workspace-wide unique (enforced by the filtered index
        /// IX_IntranetPages_Slug). Pre-check so a collision returns a friendly 400 instead
        /// of a raw DbUpdateException/500. Null slugs are exempt (the index is filtered).
        /// </summary>
        private async Task<bool> IsPageSlugUniqueAsync(string slug, string? excludePageId, CancellationToken ct)
        {
            return !await dbContext.IntranetPages.AsNoTracking()
                .AnyAsync(p => p.Slug == slug && p.PageId != excludePageId, ct);
        }

        /// <summary>
        /// True if re-parenting <paramref name="pageId"/> under <paramref name="newParentId"/>
        /// would make the page its own ancestor (a self-parent or a descendant cycle), which
        /// would otherwise loop the ChildPages tree rendering forever.
        /// </summary>
        private async Task<bool> WouldCreatePageCycleAsync(string pageId, string newParentId, CancellationToken ct)
        {
            var parentById = await dbContext.IntranetPages.AsNoTracking()
                .Select(p => new { p.PageId, p.ParentPageId })
                .ToDictionaryAsync(p => p.PageId, p => p.ParentPageId, ct);

            return PageHierarchyRules.WouldCreateCycle(pageId, newParentId, parentById);
        }

        [Function("CreateIntranetPage")]
        public async Task<IActionResult> CreatePageAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intranet/pages")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, createPageValidator);
            if (validationError != null)
                return validationError;

            var slug = string.IsNullOrWhiteSpace(dto!.Slug) ? null : dto.Slug.Trim();
            if (slug != null && !await IsPageSlugUniqueAsync(slug, excludePageId: null, req.FunctionContext.CancellationToken))
                return new BadRequestObjectResult($"Slug \"{slug}\" is already in use by another page.");

            var now = DateTime.UtcNow;
            var page = new IntranetPage
            {
                PageId = Guid.NewGuid().ToString("N"),
                Title = dto.Title.Trim(),
                Slug = slug,
                ParentPageId = dto.ParentPageId,
                ContentMarkdown = IntranetHtmlSanitizer.SanitizeForStorage(dto.ContentMarkdown),
                SortOrder = dto.SortOrder,
                IsPublished = dto.IsPublished,
                CreatedByUserId = userId,
                CreatedAt = now,
                UpdatedByUserId = userId,
                UpdatedAt = now,
                RestrictEditingToOwner = false,
                Visibility = "Default"
            };

            dbContext.IntranetPages.Add(page);
            await dbContext.SaveChangesAsync(req.FunctionContext.CancellationToken);

            // Return a lightweight shape the client can use immediately (full hydration on GET).
            return new OkObjectResult(new IntranetPageDto
            {
                PageId = page.PageId,
                Title = page.Title,
                Slug = page.Slug,
                ParentPageId = page.ParentPageId,
                ContentMarkdown = page.ContentMarkdown,
                SortOrder = page.SortOrder,
                IsPublished = page.IsPublished,
                CreatedByUserId = page.CreatedByUserId,
                CreatedAt = page.CreatedAt,
                UpdatedByUserId = page.UpdatedByUserId,
                UpdatedAt = page.UpdatedAt,
                Documents = new List<IntranetPageDocumentDto>(),
                ChildPages = new List<IntranetPageSummaryDto>()
            });
        }

        [Function("GetIntranetPage")]
        public async Task<IActionResult> GetPageAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "intranet/pages/{pageId}")] HttpRequestData req,
            string pageId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.User) is { } gateFail)
                return gateFail;

            var page = await dbContext.IntranetPages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.PageId == pageId, req.FunctionContext.CancellationToken);

            if (page == null)
                return new NotFoundObjectResult("Page not found.");

            if (EnsurePageIsViewable(principal, page) is { } viewFail)
                return viewFail;

            // Load attached documents for this page (ordered)
            var docLinks = await (
                from link in dbContext.IntranetPageDocuments.AsNoTracking()
                join doc in dbContext.IntranetDocuments.AsNoTracking() on link.DocumentId equals doc.DocumentId
                where link.PageId == pageId
                orderby link.SortOrder
                select new { link, doc }
            ).ToListAsync(req.FunctionContext.CancellationToken);

            var documents = docLinks.Select(x => new IntranetPageDocumentDto
            {
                DocumentId = x.doc.DocumentId,
                DriveFileId = x.doc.DriveFileId,
                Name = x.doc.Name,
                MimeType = x.doc.MimeType,
                WebViewLink = x.doc.WebViewLink,
                ThumbnailLink = x.doc.ThumbnailLink,
                SortOrder = x.link.SortOrder,
                Caption = x.link.Caption
            }).ToList();

            var ct = req.FunctionContext.CancellationToken;
            var parentPageTitle = await ResolveParentPageTitleAsync(page.ParentPageId, ct);
            var dto = new IntranetPageDto
            {
                PageId = page.PageId,
                Title = page.Title,
                Slug = page.Slug,
                ParentPageId = page.ParentPageId,
                ParentPageTitle = parentPageTitle,
                ContentMarkdown = page.ContentMarkdown,
                SortOrder = page.SortOrder,
                IsPublished = page.IsPublished,
                CreatedByUserId = page.CreatedByUserId,
                CreatedAt = page.CreatedAt,
                UpdatedByUserId = page.UpdatedByUserId,
                UpdatedAt = page.UpdatedAt,
                Documents = documents,
                ChildPages = new List<IntranetPageSummaryDto>()
            };

            return new OkObjectResult(dto);
        }

        [Function("UpdateIntranetPage")]
        public async Task<IActionResult> UpdatePageAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "intranet/pages/{pageId}")] HttpRequestData req,
            string pageId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, updatePageValidator);
            if (validationError != null)
                return validationError;

            var pageCheck = await EnsureCanEditPageAsync(principal, pageId, userId);
            if (pageCheck.Error is { } permError) return permError;
            var page = pageCheck.Page!;

            var slug = string.IsNullOrWhiteSpace(dto!.Slug) ? null : dto.Slug.Trim();
            if (slug != null && !await IsPageSlugUniqueAsync(slug, excludePageId: pageId, req.FunctionContext.CancellationToken))
                return new BadRequestObjectResult($"Slug \"{slug}\" is already in use by another page.");

            page.Title = dto.Title.Trim();
            page.Slug = slug;
            page.ParentPageId = dto.ParentPageId;
            page.ContentMarkdown = IntranetHtmlSanitizer.SanitizeForStorage(dto.ContentMarkdown);
            page.SortOrder = dto.SortOrder;
            page.IsPublished = dto.IsPublished;
            page.UpdatedByUserId = userId;
            page.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(req.FunctionContext.CancellationToken);

            // Reload documents (same as Get) so caller gets a consistent IntranetPageDto
            var docLinks = await (
                from link in dbContext.IntranetPageDocuments.AsNoTracking()
                join doc in dbContext.IntranetDocuments.AsNoTracking() on link.DocumentId equals doc.DocumentId
                where link.PageId == pageId
                orderby link.SortOrder
                select new { link, doc }
            ).ToListAsync(req.FunctionContext.CancellationToken);

            var documents = docLinks.Select(x => new IntranetPageDocumentDto
            {
                DocumentId = x.doc.DocumentId,
                DriveFileId = x.doc.DriveFileId,
                Name = x.doc.Name,
                MimeType = x.doc.MimeType,
                WebViewLink = x.doc.WebViewLink,
                ThumbnailLink = x.doc.ThumbnailLink,
                SortOrder = x.link.SortOrder,
                Caption = x.link.Caption
            }).ToList();

            var parentPageTitle = await ResolveParentPageTitleAsync(page.ParentPageId, req.FunctionContext.CancellationToken);
            var result = new IntranetPageDto
            {
                PageId = page.PageId,
                Title = page.Title,
                Slug = page.Slug,
                ParentPageId = page.ParentPageId,
                ParentPageTitle = parentPageTitle,
                ContentMarkdown = page.ContentMarkdown,
                SortOrder = page.SortOrder,
                IsPublished = page.IsPublished,
                CreatedByUserId = page.CreatedByUserId,
                CreatedAt = page.CreatedAt,
                UpdatedByUserId = page.UpdatedByUserId,
                UpdatedAt = page.UpdatedAt,
                Documents = documents,
                ChildPages = new List<IntranetPageSummaryDto>()
            };

            return new OkObjectResult(result);
        }

        // --- The three Drive actions that fulfill the "create a google doc, or upload a doc ... from that page" requirement ---

        [Function("CreateGoogleDocumentForPage")]
        public async Task<IActionResult> CreateGoogleDocumentForPageAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intranet/pages/{pageId}/documents/create")] HttpRequestData req,
            string pageId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var pageCheck = await EnsureCanEditPageAsync(principal, pageId, userId);
            if (pageCheck.Error is { } permError) return permError;
            var page = pageCheck.Page!;

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, createGoogleDocValidator);
            if (validationError != null)
                return validationError;

            string mimeType = ResolveMimeType(body!.MimeType, body.Kind);

            var tokenResult = await GetUserDriveTokenAsync(userId);
            if (tokenResult.Error != null) return tokenResult.Error;
            var encrypted = tokenResult.Encrypted;

            string? configuredParentId = await GetIntranetDriveParentFolderIdAsync();
            var layout = await ResolveDriveFolderLayoutAsync(encrypted, configuredParentId, req.FunctionContext.CancellationToken);
            var targetFolderId = layout.ResolveUploadFolderId(mimeType, body.Name.Trim());

            try
            {
                var createdFile = await drive.CreateFileAsync(
                    encrypted,
                    body.Name.Trim(),
                    mimeType,
                    string.IsNullOrEmpty(targetFolderId) ? null : targetFolderId,
                    req.FunctionContext.CancellationToken);

                var attached = await RegisterDriveFileAndAttachAsync(page!.PageId, createdFile, body.Caption, req.FunctionContext.CancellationToken);
                return new OkObjectResult(attached);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CreateGoogleDocumentForPage failed for page {PageId}", pageId);
                return new ObjectResult("Failed to create Google document. Please try again.") { StatusCode = 502 };
            }
        }

        [Function("UploadDocumentForPage")]
        public async Task<IActionResult> UploadDocumentForPageAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intranet/pages/{pageId}/documents/upload")] HttpRequestData req,
            string pageId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var pageCheck = await EnsureCanEditPageAsync(principal, pageId, userId);
            if (pageCheck.Error is { } permError) return permError;
            var page = pageCheck.Page!;

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req,
                uploadDocValidator,
                invalidBodyMessage: "Invalid body. Expect { fileName, mimeType, contentBase64, caption? }.");
            if (validationError != null)
                return validationError;

            var bytes = Convert.FromBase64String(body!.ContentBase64);
            var ct = req.FunctionContext.CancellationToken;
            var uploadMime = IntranetFileHelper.InferMimeType(body.FileName.Trim(), body.MimeType);
            var policy = await LoadIntranetMediaPolicyAsync(ct);
            if (!IntranetMediaPolicyRules.TryValidateUpload(
                    body.FileName.Trim(), uploadMime, bytes.Length, policy, out var policyError, bytes))
                return new BadRequestObjectResult(policyError);

            var tokenResult = await GetUserDriveTokenAsync(userId);
            if (tokenResult.Error != null) return tokenResult.Error;
            var encrypted = tokenResult.Encrypted;

            string? configuredParentId = await GetIntranetDriveParentFolderIdAsync();
            var layout = await ResolveDriveFolderLayoutAsync(encrypted, configuredParentId, ct);
            var targetFolderId = layout.ResolveUploadFolderId(uploadMime, body.FileName.Trim());

            try
            {
                using var stream = new MemoryStream(bytes);
                var uploaded = await drive.UploadFileAsync(
                    encrypted,
                    stream,
                    body.FileName.Trim(),
                    uploadMime,
                    string.IsNullOrEmpty(targetFolderId) ? null : targetFolderId,
                    ct);

                var attached = await RegisterDriveFileAndAttachAsync(page!.PageId, uploaded, body.Caption, ct);
                return new OkObjectResult(attached);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "UploadDocumentForPage failed for page {PageId}", pageId);
                return new ObjectResult(ApiErrorMessages.DriveOperationFailed) { StatusCode = 502 };
            }
        }

        [Function("AttachDocumentToPage")]
        public async Task<IActionResult> AttachDocumentToPageAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intranet/pages/{pageId}/documents")] HttpRequestData req,
            string pageId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var pageCheck = await EnsureCanEditPageAsync(principal, pageId, userId);
            if (pageCheck.Error is { } permError) return permError;
            var page = pageCheck.Page!;

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req,
                attachExistingValidator,
                invalidBodyMessage: "Invalid body. Expect { driveFileId, caption? }.");
            if (validationError != null)
                return validationError;

            var attachBody = body!;
            var tokenResult = await GetUserDriveTokenAsync(userId);
            if (tokenResult.Error != null) return tokenResult.Error;
            var encrypted = tokenResult.Encrypted;

            var ct = req.FunctionContext.CancellationToken;
            try
            {
                Google.Apis.Drive.v3.Data.File meta;
                if (!string.IsNullOrWhiteSpace(attachBody.Name))
                {
                    // Library picker already has metadata — avoid a redundant Drive GET that can 502 on shared drives.
                    meta = BuildDriveFileFromAttachRequest(attachBody);
                }
                else
                {
                    meta = await drive.GetFileAsync(encrypted, attachBody.DriveFileId.Trim(), ct);
                }

                var attached = await RegisterDriveFileAndAttachAsync(page!.PageId, meta, attachBody.Caption, ct);
                return new OkObjectResult(attached);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AttachDocumentToPage failed for page {PageId} driveFile {DriveFileId}", pageId, attachBody.DriveFileId);
                return new ObjectResult("Failed to attach Drive file. Please try again.") { StatusCode = 502 };
            }
        }

        [Function("DetachDocumentFromPage")]
        public async Task<IActionResult> DetachDocumentFromPageAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "intranet/pages/{pageId}/documents/{documentId}")] HttpRequestData req,
            string pageId,
            string documentId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var pageCheck = await EnsureCanEditPageAsync(principal, pageId, userId);
            if (pageCheck.Error is { } permError) return permError;

            var ct = req.FunctionContext.CancellationToken;
            var link = await dbContext.IntranetPageDocuments
                .FirstOrDefaultAsync(l => l.PageId == pageId && l.DocumentId == documentId, ct);
            if (link == null)
                return new NotFoundObjectResult("Attachment not found on this page.");

            dbContext.IntranetPageDocuments.Remove(link);
            await dbContext.SaveChangesAsync(ct);
            return new OkResult();
        }

        // ---------- Additional Pages endpoints (list, slug, delete, reorder, move) ----------

        [Function("GetIntranetPages")]
        public async Task<IActionResult> GetPagesAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "intranet/pages")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.User) is { } gateFail)
                return gateFail;

            var pages = await dbContext.IntranetPages
                .AsNoTracking()
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.Title)
                .ToListAsync(req.FunctionContext.CancellationToken);

            var result = pages.Select(p => new IntranetPageSummaryDto
            {
                PageId = p.PageId,
                Title = p.Title,
                Slug = p.Slug,
                SortOrder = p.SortOrder,
                IsPublished = p.IsPublished
            }).ToList();

            return new OkObjectResult(result);
        }

        [Function("SearchIntranetPages")]
        public async Task<IActionResult> SearchPagesAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "intranet/search/pages")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out _, Constants.Roles.User) is { } gateFail)
                return gateFail;

            var q = req.Query["q"];
            if (string.IsNullOrWhiteSpace(q))
                return new OkObjectResult(new List<IntranetPageSearchResultDto>());

            var limit = IntranetSearchHelper.DefaultResultLimit;
            if (int.TryParse(req.Query["limit"], out var requestedLimit))
                limit = Math.Clamp(requestedLimit, 1, IntranetSearchHelper.MaxResultLimit);

            var terms = IntranetSearchHelper.ParseQueryTerms(q);
            if (terms.Length == 0 || q.Trim().Length < IntranetSearchHelper.MinQueryLength)
                return new OkObjectResult(new List<IntranetPageSearchResultDto>());

            try
            {
                var canViewDrafts = CanViewUnpublishedIntranetPages(principal);
                var ct = req.FunctionContext.CancellationToken;
                var pageRows = await dbContext.IntranetPages
                    .AsNoTracking()
                    .Where(p => canViewDrafts || p.IsPublished)
                    .Select(p => new
                    {
                        p.PageId,
                        p.Title,
                        p.Slug,
                        p.ContentMarkdown,
                        p.IsPublished,
                        p.UpdatedAt
                    })
                    .ToListAsync(ct);

                var pageIds = pageRows.Select(p => p.PageId).ToList();
                var attachmentRows = pageIds.Count == 0
                    ? []
                    : await (
                        from link in dbContext.IntranetPageDocuments.AsNoTracking()
                        join doc in dbContext.IntranetDocuments.AsNoTracking() on link.DocumentId equals doc.DocumentId
                        where pageIds.Contains(link.PageId) && doc.IsActive
                        select new { link.PageId, doc.Name, doc.MimeType }
                    ).ToListAsync(ct);

                var attachmentTextByPageId = attachmentRows
                    .GroupBy(r => r.PageId)
                    .ToDictionary(
                        g => g.Key,
                        g => IntranetSearchHelper.BuildAttachmentSearchText(
                            g.Select(r => (r.Name, (string?)r.MimeType))));

                var pages = pageRows.Select(p => new IntranetSearchHelper.PageSearchRecord(
                    p.PageId,
                    p.Title,
                    p.Slug,
                    p.ContentMarkdown,
                    p.IsPublished,
                    p.UpdatedAt,
                    attachmentTextByPageId.GetValueOrDefault(p.PageId, ""))).ToList();

                var hits = IntranetSearchHelper.SearchPages(pages, terms, limit);
                var result = hits.Select(h => new IntranetPageSearchResultDto
                {
                    PageId = h.Page.PageId,
                    Title = h.Page.Title,
                    Slug = h.Page.Slug,
                    Excerpt = h.Excerpt,
                    IsPublished = h.Page.IsPublished,
                    Score = h.Score
                }).ToList();

                return new OkObjectResult(result);
            }
            catch (OperationCanceledException) when (req.FunctionContext.CancellationToken.IsCancellationRequested)
            {
                // Client aborted a superseded search request while typing — not an error.
                return new OkObjectResult(new List<IntranetPageSearchResultDto>());
            }
        }

        [Function("GetIntranetPageBySlug")]
        public async Task<IActionResult> GetPageBySlugAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "intranet/pages/slug/{slug}")] HttpRequestData req,
            string slug)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.User) is { } gateFail)
                return gateFail;

            var page = await dbContext.IntranetPages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Slug == slug, req.FunctionContext.CancellationToken);

            if (page == null)
                return new NotFoundObjectResult("Page not found.");

            if (EnsurePageIsViewable(principal, page) is { } viewFail)
                return viewFail;

            // Reuse the documents load from Get (simplified here for brevity; in real could extract helper)
            var docLinks = await (
                from link in dbContext.IntranetPageDocuments.AsNoTracking()
                join doc in dbContext.IntranetDocuments.AsNoTracking() on link.DocumentId equals doc.DocumentId
                where link.PageId == page.PageId
                orderby link.SortOrder
                select new { link, doc }
            ).ToListAsync(req.FunctionContext.CancellationToken);

            var documents = docLinks.Select(x => new IntranetPageDocumentDto
            {
                DocumentId = x.doc.DocumentId,
                DriveFileId = x.doc.DriveFileId,
                Name = x.doc.Name,
                MimeType = x.doc.MimeType,
                WebViewLink = x.doc.WebViewLink,
                ThumbnailLink = x.doc.ThumbnailLink,
                SortOrder = x.link.SortOrder,
                Caption = x.link.Caption
            }).ToList();

            var dto = new IntranetPageDto
            {
                PageId = page.PageId,
                Title = page.Title,
                Slug = page.Slug,
                ParentPageId = page.ParentPageId,
                ContentMarkdown = page.ContentMarkdown,
                SortOrder = page.SortOrder,
                IsPublished = page.IsPublished,
                CreatedByUserId = page.CreatedByUserId,
                CreatedAt = page.CreatedAt,
                UpdatedByUserId = page.UpdatedByUserId,
                UpdatedAt = page.UpdatedAt,
                Documents = documents,
                ChildPages = new List<IntranetPageSummaryDto>()
            };

            return new OkObjectResult(dto);
        }

        [Function("DeleteIntranetPage")]
        public async Task<IActionResult> DeletePageAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "intranet/pages/{pageId}")] HttpRequestData req,
            string pageId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var page = await dbContext.IntranetPages.FirstOrDefaultAsync(p => p.PageId == pageId);
            if (page == null)
                return new NotFoundObjectResult("Page not found.");

            // Permission: owner or admin if restricted
            if (page.RestrictEditingToOwner && !string.Equals(page.CreatedByUserId, userId, StringComparison.Ordinal))
            {
                if (!Constants.Roles.HasScopedAccess(principal, Constants.Scopes.Intranet, Constants.Roles.Admin))
                    return new StatusCodeResult(403);
            }

            // Reparent direct children (or could fail if strict hierarchy desired)
            var children = await dbContext.IntranetPages.Where(p => p.ParentPageId == pageId).ToListAsync();
            foreach (var child in children)
            {
                child.ParentPageId = null;
                child.UpdatedAt = DateTime.UtcNow;
            }

            dbContext.IntranetPages.Remove(page);
            await dbContext.SaveChangesAsync(req.FunctionContext.CancellationToken);

            return new NoContentResult();
        }

        [Function("ReorderIntranetPages")]
        public async Task<IActionResult> ReorderPagesAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intranet/pages/reorder")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req,
                reorderPagesValidator,
                invalidBodyMessage: "Invalid body. Expect { orderedIds: string[] }");
            if (validationError != null)
                return validationError;

            var pages = await dbContext.IntranetPages
                .Where(p => body!.OrderedIds.Contains(p.PageId))
                .ToListAsync(req.FunctionContext.CancellationToken);

            int order = 0;
            foreach (var id in body!.OrderedIds)
            {
                var p = pages.FirstOrDefault(x => x.PageId == id);
                if (p != null)
                {
                    p.SortOrder = order++;
                    p.UpdatedAt = DateTime.UtcNow;
                    p.UpdatedByUserId = userId;
                }
            }

            await dbContext.SaveChangesAsync(req.FunctionContext.CancellationToken);
            return new OkResult();
        }

        [Function("MoveIntranetPage")]
        public async Task<IActionResult> MovePageAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intranet/pages/move")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, movePageValidator);
            if (validationError != null)
                return validationError;

            var moveBody = body!;
            var page = await dbContext.IntranetPages.FirstOrDefaultAsync(p => p.PageId == moveBody.PageId);
            if (page == null)
                return new NotFoundObjectResult("Page not found.");

            // Permission check
            if (page.RestrictEditingToOwner && !string.Equals(page.CreatedByUserId, userId, StringComparison.Ordinal))
            {
                if (!Constants.Roles.HasScopedAccess(principal, Constants.Scopes.Intranet, Constants.Roles.Admin))
                    return new StatusCodeResult(403);
            }

            if (!string.IsNullOrEmpty(moveBody.NewParentPageId)
                && await WouldCreatePageCycleAsync(moveBody.PageId, moveBody.NewParentPageId, req.FunctionContext.CancellationToken))
            {
                return new BadRequestObjectResult("Cannot move a page underneath itself or one of its descendants.");
            }

            page.ParentPageId = moveBody.NewParentPageId;
            if (moveBody.NewSortOrder.HasValue)
                page.SortOrder = moveBody.NewSortOrder.Value;

            page.UpdatedByUserId = userId;
            page.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(req.FunctionContext.CancellationToken);
            return new OkResult();
        }

        // ---------- Navigation (admin only for mutations) ----------

        [Function("GetIntranetNavigation")]
        public async Task<IActionResult> GetNavigationAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "intranet/navigation")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out _, Constants.Roles.User) is { } gateFail)
                return gateFail;

            var items = await dbContext.IntranetNavigationItems
                .AsNoTracking()
                .Include(i => i.Page)
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.Title)
                .ToListAsync(req.FunctionContext.CancellationToken);

            // Build full parent/child tree (supports nested sub-menus at any depth).
            var byId = items.ToDictionary(i => i.Id);
            var roots = items
                .Where(i => i.ParentId == null || !byId.ContainsKey(i.ParentId))
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.Title)
                .Select(i => MapNavItemWithChildren(i, byId, items))
                .ToList();

            var canViewDrafts = CanViewUnpublishedIntranetPages(principal);
            var visible = FilterNavigationForViewer(roots, canViewDrafts);

            var isNavAdmin = Constants.Roles.HasScopedAccess(principal, Constants.Scopes.Intranet, Constants.Roles.Admin);
            if (!isNavAdmin)
            {
                var maxDepth = await GetIntranetNavigationMaxDepthAsync(req.FunctionContext.CancellationToken);
                visible = IntranetNavigationRules.TrimToMaxDepth(visible, maxDepth);
            }

            return new OkObjectResult(visible);
        }

        private async Task<int> GetIntranetNavigationMaxDepthAsync(CancellationToken ct)
        {
            var row = await dbContext.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.IntranetNavigationMaxDepth, ct);
            return IntranetNavigationRules.ParseMaxDepth(row?.Value);
        }

        private async Task<(Dictionary<string, string?> ParentById, Dictionary<string, List<string>> ChildrenById)> GetNavigationMapsAsync(
            CancellationToken ct,
            string? excludeItemId = null)
        {
            var rows = await dbContext.IntranetNavigationItems.AsNoTracking()
                .Select(i => new { i.Id, i.ParentId })
                .ToListAsync(ct);

            var tuples = rows
                .Where(r => excludeItemId == null || !string.Equals(r.Id, excludeItemId, StringComparison.Ordinal))
                .Select(r => (r.Id, r.ParentId))
                .ToList();

            return (
                IntranetNavigationRules.BuildParentById(tuples),
                IntranetNavigationRules.BuildChildrenById(tuples));
        }

        private static IntranetNavigationItemDto MapNavItem(My.DAL.Models.IntranetNavigationItem item, Dictionary<string, My.DAL.Models.IntranetNavigationItem> lookup)
        {
            return new IntranetNavigationItemDto
            {
                Id = item.Id,
                Title = item.Title,
                Icon = item.Icon,
                PageId = item.PageId,
                PageTitle = item.Page?.Title,
                PageSlug = item.Page?.Slug,
                PageIsPublished = item.Page == null || item.Page.IsPublished,
                ExternalUrl = item.ExternalUrl,
                SortOrder = item.SortOrder,
                IsVisible = item.IsVisible,
                ParentId = item.ParentId,
                Children = new List<IntranetNavigationItemDto>()
            };
        }

        private static IntranetNavigationItemDto MapNavItemWithChildren(
            My.DAL.Models.IntranetNavigationItem item,
            Dictionary<string, My.DAL.Models.IntranetNavigationItem> byId,
            List<My.DAL.Models.IntranetNavigationItem> allItems)
        {
            var dto = MapNavItem(item, byId);
            dto.Children = allItems
                .Where(i => i.ParentId == item.Id)
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.Title)
                .Select(i => MapNavItemWithChildren(i, byId, allItems))
                .ToList();
            return dto;
        }

        private static bool CanViewUnpublishedIntranetPages(ClaimsPrincipal principal) =>
            Constants.Roles.HasScopedAccess(principal, Constants.Scopes.Intranet, Constants.Roles.Editor);

        private static IActionResult? EnsurePageIsViewable(ClaimsPrincipal principal, IntranetPage page)
        {
            if (page.IsPublished || CanViewUnpublishedIntranetPages(principal))
                return null;
            return new NotFoundObjectResult("Page not found.");
        }

        private static List<IntranetNavigationItemDto> FilterNavigationForViewer(
            List<IntranetNavigationItemDto> items, bool canViewDrafts)
        {
            var result = new List<IntranetNavigationItemDto>();
            foreach (var item in items)
            {
                var children = FilterNavigationForViewer(item.Children ?? new(), canViewDrafts);
                var linksToDraft = !string.IsNullOrWhiteSpace(item.PageId) && !item.PageIsPublished && !canViewDrafts;

                if (linksToDraft && children.Count == 0)
                    continue;

                result.Add(new IntranetNavigationItemDto
                {
                    Id = item.Id,
                    Title = item.Title,
                    Icon = item.Icon,
                    PageId = linksToDraft ? null : item.PageId,
                    PageTitle = linksToDraft ? null : item.PageTitle,
                    PageSlug = linksToDraft ? null : item.PageSlug,
                    PageIsPublished = item.PageIsPublished,
                    ExternalUrl = item.ExternalUrl,
                    SortOrder = item.SortOrder,
                    IsVisible = item.IsVisible,
                    ParentId = item.ParentId,
                    Children = children
                });
            }
            return result;
        }

        [Function("CreateIntranetNavigationItem")]
        public async Task<IActionResult> CreateNavigationItemAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intranet/navigation")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Admin) is { } gateFail)
                return gateFail;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, createNavValidator);
            if (validationError != null)
                return validationError;

            var ct = req.FunctionContext.CancellationToken;
            var maxDepth = await GetIntranetNavigationMaxDepthAsync(ct);
            var (parentById, childrenById) = await GetNavigationMapsAsync(ct);
            var depthError = IntranetNavigationRules.ValidatePlacement(dto!.ParentId, parentById, childrenById, maxDepth);
            if (depthError != null)
                return new BadRequestObjectResult(depthError);

            var now = DateTime.UtcNow;
            var item = new IntranetNavigationItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = dto.Title.Trim(),
                Icon = dto.Icon,
                PageId = dto.PageId,
                ExternalUrl = dto.ExternalUrl,
                SortOrder = dto.SortOrder,
                ParentId = dto.ParentId,
                IsVisible = dto.IsVisible,
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.IntranetNavigationItems.Add(item);
            await dbContext.SaveChangesAsync(req.FunctionContext.CancellationToken);

            return new OkObjectResult(new IntranetNavigationItemDto
            {
                Id = item.Id,
                Title = item.Title,
                Icon = item.Icon,
                PageId = item.PageId,
                ExternalUrl = item.ExternalUrl,
                SortOrder = item.SortOrder,
                IsVisible = item.IsVisible
            });
        }

        [Function("UpdateIntranetNavigationItem")]
        public async Task<IActionResult> UpdateNavigationItemAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "intranet/navigation/{id}")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Admin) is { } gateFail)
                return gateFail;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, updateNavValidator);
            if (validationError != null)
                return validationError;

            var item = await dbContext.IntranetNavigationItems.FirstOrDefaultAsync(i => i.Id == id);
            if (item == null)
                return new NotFoundObjectResult("Navigation item not found.");

            var ct = req.FunctionContext.CancellationToken;
            var maxDepth = await GetIntranetNavigationMaxDepthAsync(ct);
            var (parentById, childrenById) = await GetNavigationMapsAsync(ct, excludeItemId: id);
            var depthError = IntranetNavigationRules.ValidatePlacement(
                dto!.ParentId, parentById, childrenById, maxDepth, existingItemId: id);
            if (depthError != null)
                return new BadRequestObjectResult(depthError);

            item.Title = dto.Title.Trim();
            item.Icon = dto.Icon;
            item.PageId = dto.PageId;
            item.ExternalUrl = dto.ExternalUrl;
            item.SortOrder = dto.SortOrder;
            item.ParentId = dto.ParentId;
            item.IsVisible = dto.IsVisible;
            item.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(ct);

            return new OkObjectResult(new IntranetNavigationItemDto
            {
                Id = item.Id,
                Title = item.Title,
                Icon = item.Icon,
                PageId = item.PageId,
                ExternalUrl = item.ExternalUrl,
                SortOrder = item.SortOrder,
                IsVisible = item.IsVisible
            });
        }

        [Function("DeleteIntranetNavigationItem")]
        public async Task<IActionResult> DeleteNavigationItemAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "intranet/navigation/{id}")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out _, Constants.Roles.Admin) is { } gateFail)
                return gateFail;

            var item = await dbContext.IntranetNavigationItems.FirstOrDefaultAsync(i => i.Id == id);
            if (item == null) return new NotFoundObjectResult("Not found.");

            // Reparent children
            var children = await dbContext.IntranetNavigationItems.Where(i => i.ParentId == id).ToListAsync();
            foreach (var c in children) c.ParentId = null;

            dbContext.IntranetNavigationItems.Remove(item);
            await dbContext.SaveChangesAsync(req.FunctionContext.CancellationToken);
            return new NoContentResult();
        }

        [Function("ReorderIntranetNavigation")]
        public async Task<IActionResult> ReorderNavigationAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intranet/navigation/reorder")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Admin) is { } gateFail)
                return gateFail;

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, reorderNavValidator);
            if (validationError != null)
                return validationError;

            var reorderBody = body!;
            var items = await dbContext.IntranetNavigationItems.Where(i => reorderBody.OrderedIds.Contains(i.Id)).ToListAsync();
            int order = 0;
            foreach (var id in reorderBody.OrderedIds)
            {
                var it = items.FirstOrDefault(x => x.Id == id);
                if (it != null) { it.SortOrder = order++; it.UpdatedAt = DateTime.UtcNow; }
            }
            await dbContext.SaveChangesAsync(req.FunctionContext.CancellationToken);
            return new OkResult();
        }

        // ---------- Documents (curated Drive library) ----------

        [Function("GetIntranetDocuments")]
        public async Task<IActionResult> GetDocumentsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "intranet/documents")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out _, Constants.Roles.User) is { } gateFail)
                return gateFail;

            var ct = req.FunctionContext.CancellationToken;
            var includeUsage = string.Equals(req.Query["includeUsage"], "true", StringComparison.OrdinalIgnoreCase);
            var list = await QueryDocumentsAsync(req, ct);
            var dtos = await MapDocumentsToDtosAsync(list, includeUsage, ct);
            return new OkObjectResult(dtos);
        }

        [Function("GetIntranetDocumentById")]
        public async Task<IActionResult> GetDocumentByIdAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "intranet/documents/{documentId}")] HttpRequestData req,
            string documentId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out _, Constants.Roles.User) is { } gateFail)
                return gateFail;

            var ct = req.FunctionContext.CancellationToken;
            var doc = await dbContext.IntranetDocuments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DocumentId == documentId && d.IsActive, ct);
            if (doc == null)
                return new NotFoundObjectResult("Document not found.");

            var dto = (await MapDocumentsToDtosAsync(new[] { doc }, includeUsage: true, ct)).First();
            return new OkObjectResult(dto);
        }

        [Function("RegisterIntranetDocument")]
        public async Task<IActionResult> RegisterDocumentAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intranet/documents")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, createDocumentValidator);
            if (validationError != null)
                return validationError;

            var registerDto = dto!;
            var ct = req.FunctionContext.CancellationToken;
            Google.Apis.Drive.v3.Data.File? driveFile = null;
            if (string.IsNullOrWhiteSpace(registerDto.Name))
            {
                var tokenResult = await GetUserDriveTokenAsync(userId);
                if (tokenResult.Error != null) return tokenResult.Error;
                try
                {
                    driveFile = await drive.GetFileAsync(tokenResult.Encrypted, registerDto.DriveFileId.Trim(), ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "RegisterIntranetDocument metadata fetch failed for {DriveFileId}", registerDto.DriveFileId);
                    return new ObjectResult("Could not read Drive file metadata. Please try again.") { StatusCode = 502 };
                }
            }
            else
            {
                driveFile = new Google.Apis.Drive.v3.Data.File
                {
                    Id = registerDto.DriveFileId.Trim(),
                    Name = registerDto.Name,
                    MimeType = registerDto.MimeType,
                    WebViewLink = registerDto.WebViewLink,
                    ThumbnailLink = registerDto.ThumbnailLink,
                    Size = registerDto.SizeBytes,
                    ModifiedTimeDateTimeOffset = registerDto.DriveLastModified.HasValue
                        ? new DateTimeOffset(registerDto.DriveLastModified.Value, TimeSpan.Zero)
                        : null
                };
            }

            var doc = await UpsertIntranetDocumentAsync(registerDto, driveFile, ct);
            var result = (await MapDocumentsToDtosAsync(new[] { doc }, includeUsage: true, ct)).First();
            return new OkObjectResult(result);
        }

        [Function("UpdateIntranetDocument")]
        public async Task<IActionResult> UpdateDocumentAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "intranet/documents/{documentId}")] HttpRequestData req,
            string documentId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, updateDocumentValidator);
            if (validationError != null)
                return validationError;

            var ct = req.FunctionContext.CancellationToken;
            var doc = await dbContext.IntranetDocuments.FirstOrDefaultAsync(d => d.DocumentId == documentId, ct);
            if (doc == null)
                return new NotFoundObjectResult("Document not found.");

            var updateDto = dto!;
            var previousName = doc.Name;
            if (!string.IsNullOrWhiteSpace(updateDto.Name)) doc.Name = updateDto.Name.Trim();
            if (updateDto.Description != null) doc.Description = updateDto.Description;
            if (updateDto.Category != null) doc.Category = updateDto.Category;
            if (updateDto.IsFeatured.HasValue) doc.IsFeatured = updateDto.IsFeatured.Value;
            if (updateDto.IsActive.HasValue) doc.IsActive = updateDto.IsActive.Value;
            doc.SyncedAt = DateTime.UtcNow;

            if (!string.Equals(previousName, doc.Name, StringComparison.Ordinal))
                await PropagateDriveFileDisplayNameToPagesAsync(doc.DriveFileId, doc.Name, userId, ct);

            dbContext.IntranetDocuments.Update(doc);
            await dbContext.SaveChangesAsync(ct);

            var result = (await MapDocumentsToDtosAsync(new[] { doc }, includeUsage: true, ct)).First();
            return new OkObjectResult(result);
        }

        [Function("DeleteIntranetDocument")]
        public async Task<IActionResult> DeleteDocumentAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "intranet/documents/{documentId}")] HttpRequestData req,
            string documentId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var ct = req.FunctionContext.CancellationToken;
            var doc = await dbContext.IntranetDocuments.FirstOrDefaultAsync(d => d.DocumentId == documentId, ct);
            if (doc == null)
                return new NotFoundObjectResult("Document not found.");

            return await PurgeLibraryFileAsync(userId, doc.DriveFileId, doc, ct);
        }

        [Function("DeleteIntranetDriveLibraryFile")]
        public async Task<IActionResult> DeleteDriveLibraryFileAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "intranet/documents/drive/{driveFileId}")] HttpRequestData req,
            string driveFileId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            if (RequestValidator.BadRequestIfInvalid(driveFileIdValidator, driveFileId) is { } driveIdError)
                return driveIdError;

            var ct = req.FunctionContext.CancellationToken;
            var doc = await dbContext.IntranetDocuments
                .FirstOrDefaultAsync(d => d.DriveFileId == driveFileId && d.IsActive, ct);

            return await PurgeLibraryFileAsync(userId, driveFileId.Trim(), doc, ct);
        }

        private async Task<IActionResult> PurgeLibraryFileAsync(
            string userId, string driveFileId, IntranetDocument? doc, CancellationToken ct)
        {
            var contentUsage = await BuildDriveFileUsageFromContentAsync(new[] { driveFileId }, ct);
            var pagesFromContent = contentUsage.GetValueOrDefault(driveFileId) ?? new List<IntranetDocumentPageUsageDto>();

            var attachmentLinks = doc != null
                ? await dbContext.IntranetPageDocuments.Where(l => l.DocumentId == doc.DocumentId).ToListAsync(ct)
                : new List<IntranetPageDocument>();

            var attachmentPageIds = attachmentLinks.Select(l => l.PageId).ToList();
            var attachmentPageTitles = attachmentPageIds.Count == 0
                ? new List<string>()
                : await dbContext.IntranetPages.AsNoTracking()
                    .Where(p => attachmentPageIds.Contains(p.PageId))
                    .Select(p => p.Title)
                    .ToListAsync(ct);

            var affectedTitles = pagesFromContent
                .Select(p => p.Title)
                .Concat(attachmentPageTitles)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pageIdsToStrip = pagesFromContent.Select(p => p.PageId).ToHashSet(StringComparer.Ordinal);
            var pagesToUpdate = pageIdsToStrip.Count == 0
                ? new List<IntranetPage>()
                : await dbContext.IntranetPages.Where(p => pageIdsToStrip.Contains(p.PageId)).ToListAsync(ct);

            var pagesStripped = 0;
            foreach (var page in pagesToUpdate)
            {
                var stripped = IntranetFileHelper.StripDriveFileReferencesFromHtml(page.ContentMarkdown, driveFileId);
                if (stripped == page.ContentMarkdown) continue;
                page.ContentMarkdown = stripped;
                page.UpdatedAt = DateTime.UtcNow;
                page.UpdatedByUserId = userId;
                pagesStripped++;
            }

            if (attachmentLinks.Count > 0)
                dbContext.IntranetPageDocuments.RemoveRange(attachmentLinks);

            if (doc != null)
            {
                doc.IsActive = false;
                doc.SyncedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync(ct);

            var tokenResult = await GetUserDriveTokenAsync(userId);
            if (tokenResult.Error != null) return tokenResult.Error;

            try
            {
                await drive.DeleteFileAsync(tokenResult.Encrypted, driveFileId, ct);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogWarning(ex, "Drive file {DriveFileId} already gone during library purge", driveFileId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Drive delete failed for {DriveFileId} after DB purge", driveFileId);
                return new ObjectResult(
                    "References were removed from intranet pages, but deleting the Drive file failed. " +
                    "You may need to remove it manually in Drive.")
                { StatusCode = 502 };
            }

            return new OkObjectResult(new PurgeIntranetDocumentResultDto
            {
                DriveFileId = driveFileId,
                PagesStripped = pagesStripped,
                AttachmentsRemoved = attachmentLinks.Count,
                DeletedFromDrive = true,
                AffectedPageTitles = affectedTitles
            });
        }

        /// <summary>
        /// Streams a Drive file through the API so private images can render in the intranet editor and page view.
        /// </summary>
        [Function("GetIntranetMediaPolicy")]
        public async Task<IActionResult> GetIntranetMediaPolicyAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "intranet/media-policy")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out _, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var policy = await LoadIntranetMediaPolicyAsync(req.FunctionContext.CancellationToken);
            return new OkObjectResult(ToMediaPolicyDto(policy));
        }

        [Function("FetchExternalIntranetImage")]
        public async Task<IActionResult> FetchExternalIntranetImageAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intranet/fetch-external-image")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out _, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req,
                fetchExternalImageValidator,
                invalidBodyMessage: "Invalid body. Expect { url }.");
            if (validationError != null)
                return validationError;

            if (!IntranetExternalFetchRules.TryValidateFetchUrl(body!.Url, out var uri, out var urlError))
                return new BadRequestObjectResult(urlError);

            var ct = req.FunctionContext.CancellationToken;
            var policy = await LoadIntranetMediaPolicyAsync(ct);
            if (!policy.IsConfigured)
                return new BadRequestObjectResult(IntranetMediaPolicyRules.NotConfiguredMessage);

            var maxBytes = policy.MaxUploadBytesByExtension.Values.DefaultIfEmpty(0).Max();
            if (maxBytes <= 0)
                maxBytes = IntranetMediaPolicyRules.AbsoluteMaxUploadBytes;

            try
            {
                using var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                string? lastError = null;
                foreach (var candidate in IntranetExternalFetchRules.GetFetchUrlCandidates(uri!, maxBytes))
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                    IntranetExternalFetchRules.ApplyFetchRequestHeaders(request, candidate);

                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        lastError = $"Could not download image (HTTP {(int)response.StatusCode}).";
                        if (response.StatusCode == HttpStatusCode.Forbidden
                            && IntranetExternalFetchRules.IsGoogleHostedImageUrl(candidate))
                        {
                            lastError = IntranetExternalFetchRules.GetGoogleFetchBlockedHint(candidate);
                        }

                        continue;
                    }

                    var contentType = response.Content.Headers.ContentType?.MediaType;
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var ms = new MemoryStream();
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        total += read;
                        if (total > maxBytes)
                        {
                            lastError = $"Image exceeds the maximum allowed size ({IntranetMediaPolicyRules.FormatBytes(maxBytes)}).";
                            ms.SetLength(0);
                            break;
                        }

                        ms.Write(buffer, 0, read);
                    }

                    if (ms.Length == 0)
                        continue;

                    var bytes = ms.ToArray();
                    if (!IntranetExternalFetchRules.LooksLikeImageResponse(contentType, bytes))
                    {
                        lastError = "The URL did not return an image.";
                        continue;
                    }

                    if (!IntranetExternalFetchRules.TryDetectImageMimeFromBytes(bytes, out var detectedMime)
                        || string.IsNullOrWhiteSpace(detectedMime))
                    {
                        detectedMime = contentType ?? "image/jpeg";
                    }

                    var mime = IntranetFileHelper.InferMimeType(null, detectedMime);
                    var suggestedName = IntranetExternalFetchRules.InferFileNameFromUrl(candidate, detectedMime);
                    if (!IntranetMediaPolicyRules.TryValidateUpload(
                            suggestedName, mime, bytes.Length, policy, out var policyError, bytes))
                    {
                        lastError = policyError;
                        continue;
                    }

                    return new OkObjectResult(new FetchExternalImageResultDto
                    {
                        Base64 = Convert.ToBase64String(bytes),
                        MimeType = mime,
                        ByteLength = bytes.Length,
                        SuggestedFileName = suggestedName
                    });
                }

                if (!string.IsNullOrWhiteSpace(lastError))
                    return new BadRequestObjectResult(lastError);

                return new BadRequestObjectResult("Could not download the image from that URL.");
            }
            catch (TaskCanceledException)
            {
                return new BadRequestObjectResult("Timed out downloading the image.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FetchExternalIntranetImage failed for {Url}", uri);
                return new BadRequestObjectResult("Could not download the image from that URL.");
            }
        }

        [Function("GetIntranetDriveMedia")]
        public async Task<IActionResult> GetDriveMediaAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "intranet/documents/drive/{driveFileId}/media")] HttpRequestData req,
            string driveFileId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.User) is { } gateFail)
                return gateFail;

            if (RequestValidator.BadRequestIfInvalid(driveFileIdValidator, driveFileId) is { } driveIdError)
                return driveIdError;

            var ct = req.FunctionContext.CancellationToken;
            var trimmedId = driveFileId.Trim();
            if (!await IsDriveFileAllowedForMediaProxyAsync(trimmedId, ct))
                return new NotFoundObjectResult("Drive file not found or not accessible.");

            var tokenResult = await GetUserDriveTokenAsync(userId);
            if (tokenResult.Error != null) return tokenResult.Error;

            try
            {
                var (content, mimeType) = await drive.DownloadFileContentAsync(tokenResult.Encrypted, trimmedId, ct);
                return new FileContentResult(content, mimeType);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new NotFoundObjectResult("Drive file not found or not accessible.");
            }
            catch (Google.GoogleApiException ex) when (IsDriveApiNotEnabled(ex))
            {
                logger.LogError(ex, "GetIntranetDriveMedia failed: Drive API not enabled");
                return new ObjectResult(
                    "Google Drive API is not enabled for this app's Google Cloud project. " +
                    "Enable the Drive API in Google Cloud Console, wait a few minutes, then reconnect Google in Settings if needed.")
                { StatusCode = 503 };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GetIntranetDriveMedia failed for {DriveFileId}", driveFileId);
                return new ObjectResult("Failed to load Drive media. Please try again.") { StatusCode = 502 };
            }
        }

        /// <summary>
        /// The company Drive folder IS the document library. Lists all files from the configured
        /// parent folder and merges intranet metadata (category, featured) plus page usage.
        /// </summary>
        [Function("BrowseIntranetDriveFolder")]
        public async Task<IActionResult> BrowseDriveFolderAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "intranet/documents/drive")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.User) is { } gateFail)
                return gateFail;

            var ct = req.FunctionContext.CancellationToken;
            var parentFolderId = await GetIntranetDriveParentFolderIdAsync();
            if (string.IsNullOrWhiteSpace(parentFolderId))
                return new BadRequestObjectResult("Intranet Drive parent folder is not configured in App Settings.");

            var tokenResult = await GetUserDriveTokenAsync(userId);
            if (tokenResult.Error != null) return tokenResult.Error;

            var search = req.Query["search"];
            var fileType = req.Query["fileType"];
            var category = req.Query["category"];
            var pageToken = req.Query["pageToken"];
            var sort = (req.Query["sort"] ?? "name").Trim().ToLowerInvariant();
            var sortDir = (req.Query["sortDir"] ?? "asc").Trim().ToLowerInvariant();
            var includeUsage = string.Equals(req.Query["includeUsage"], "true", StringComparison.OrdinalIgnoreCase);

            var layout = await ResolveDriveFolderLayoutAsync(tokenResult.Encrypted, parentFolderId, ct);
            var browseFolderIds = layout.GetBrowseFolderIds();
            var parentClause = browseFolderIds.Count == 1
                ? $"'{browseFolderIds[0].Replace("'", "\\'")}' in parents"
                : "(" + string.Join(" or ", browseFolderIds.Select(id => $"'{id.Replace("'", "\\'")}' in parents")) + ")";
            var queryParts = new List<string> { parentClause, "trashed = false" };
            if (!string.IsNullOrWhiteSpace(search))
            {
                var escaped = search!.Replace("'", "\\'");
                queryParts.Add($"name contains '{escaped}'");
            }

            if (!string.IsNullOrWhiteSpace(fileType) && fileType != IntranetFileHelper.FileTypeAll)
            {
                queryParts.Add(fileType switch
                {
                    IntranetFileHelper.FileTypeImage => "mimeType contains 'image/'",
                    IntranetFileHelper.FileTypeVideo => "(mimeType contains 'video/' or mimeType = 'application/vnd.google-apps.video')",
                    IntranetFileHelper.FileTypeDocument => $"(not mimeType contains 'image/' and not mimeType contains 'video/' and mimeType != 'application/vnd.google-apps.video' and mimeType != '{IntranetFileHelper.DriveFolderMimeType}')",
                    _ => ""
                });
            }

            var query = string.Join(" and ", queryParts.Where(p => !string.IsNullOrEmpty(p)));

            try
            {
                var files = (await drive.ListFilesAsync(tokenResult.Encrypted, query, pageToken, 100, ct))
                    .Where(f => !string.IsNullOrEmpty(f.Id) && !IntranetFileHelper.IsDriveFolder(f.MimeType))
                    .ToList();
                var driveIds = files.Select(f => f.Id!).ToList();

                var metadataByDriveId = await dbContext.IntranetDocuments.AsNoTracking()
                    .Where(d => d.IsActive && driveIds.Contains(d.DriveFileId))
                    .ToDictionaryAsync(d => d.DriveFileId, ct);

                Dictionary<string, List<IntranetDocumentPageUsageDto>> usageByDocId = new();
                if (includeUsage && metadataByDriveId.Count > 0)
                {
                    var docIds = metadataByDriveId.Values.Select(d => d.DocumentId).ToList();
                    var usageRows = await (
                        from link in dbContext.IntranetPageDocuments.AsNoTracking()
                        join page in dbContext.IntranetPages.AsNoTracking() on link.PageId equals page.PageId
                        where docIds.Contains(link.DocumentId)
                        orderby page.Title
                        select new { link.DocumentId, link.PageId, page.Title, link.Caption }
                    ).ToListAsync(ct);

                    usageByDocId = usageRows
                        .GroupBy(x => x.DocumentId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => new IntranetDocumentPageUsageDto
                            {
                                PageId = x.PageId,
                                Title = x.Title,
                                Caption = x.Caption
                            }).ToList());
                }

                Dictionary<string, List<IntranetDocumentPageUsageDto>> usageByDriveId = new();
                if (includeUsage && driveIds.Count > 0)
                    usageByDriveId = await BuildDriveFileUsageFromContentAsync(driveIds, ct);

                var items = files
                    .Where(f => !string.IsNullOrEmpty(f.Id))
                    .Select(f =>
                    {
                        var owner = f.Owners?.FirstOrDefault();
                        metadataByDriveId.TryGetValue(f.Id!, out var meta);
                        IReadOnlyList<IntranetDocumentPageUsageDto>? attachmentPages = null;
                        if (meta != null && usageByDocId.TryGetValue(meta.DocumentId, out var linkedPages))
                            attachmentPages = linkedPages;

                        usageByDriveId.TryGetValue(f.Id!, out var contentPages);
                        var usedOn = IntranetFileHelper.MergePageUsage(attachmentPages, contentPages);
                        var usageCount = usedOn.Count;

                        return new IntranetDriveLibraryItemDto
                        {
                            DriveFileId = f.Id!,
                            Name = meta?.Name ?? f.Name ?? "Untitled",
                            MimeType = f.MimeType,
                            WebViewLink = f.WebViewLink,
                            ThumbnailLink = f.ThumbnailLink,
                            SizeBytes = f.Size,
                            DriveLastModified = f.ModifiedTimeDateTimeOffset?.UtcDateTime,
                            DriveOwnerEmail = owner?.EmailAddress,
                            DriveOwnerName = owner?.DisplayName,
                            DocumentId = meta?.DocumentId,
                            Description = meta?.Description,
                            Category = meta?.Category,
                            IsFeatured = meta?.IsFeatured ?? false,
                            UsageCount = usageCount,
                            UsedOnPages = includeUsage ? usedOn : null
                        };
                    })
                    .ToList();

                if (!string.IsNullOrWhiteSpace(category))
                {
                    var cat = category!.Trim();
                    items = items.Where(i => string.Equals(i.Category, cat, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                var desc = sortDir == "desc";
                items = sort switch
                {
                    "modified" => desc
                        ? items.OrderByDescending(i => i.DriveLastModified).ThenBy(i => i.Name).ToList()
                        : items.OrderBy(i => i.DriveLastModified).ThenBy(i => i.Name).ToList(),
                    "size" => desc
                        ? items.OrderByDescending(i => i.SizeBytes).ThenBy(i => i.Name).ToList()
                        : items.OrderBy(i => i.SizeBytes).ThenBy(i => i.Name).ToList(),
                    "featured" => desc
                        ? items.OrderByDescending(i => i.IsFeatured).ThenByDescending(i => i.Name).ToList()
                        : items.OrderByDescending(i => i.IsFeatured).ThenBy(i => i.Name).ToList(),
                    "usage" => desc
                        ? items.OrderByDescending(i => i.UsageCount).ThenBy(i => i.Name).ToList()
                        : items.OrderBy(i => i.UsageCount).ThenBy(i => i.Name).ToList(),
                    _ => desc
                        ? items.OrderByDescending(i => i.Name).ToList()
                        : items.OrderBy(i => i.Name).ToList()
                };

                return new OkObjectResult(items);
            }
            catch (Google.GoogleApiException ex) when (IsDriveApiNotEnabled(ex))
            {
                logger.LogError(ex, "BrowseIntranetDriveFolder failed: Drive API not enabled");
                return new ObjectResult(
                    "Google Drive API is not enabled for this app's Google Cloud project. " +
                    "Enable the Drive API in Google Cloud Console, wait a few minutes, then reconnect Google in Settings if needed.")
                { StatusCode = 503 };
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogWarning(ex, "BrowseIntranetDriveFolder failed: configured folder not found");
                return new BadRequestObjectResult(
                    "The Intranet Drive folder in App Settings was not found or you no longer have access. " +
                    "Open App Settings and set Intranet Drive Parent Folder ID to the root Intranet folder you can open in Drive.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BrowseIntranetDriveFolder failed");
                return new ObjectResult(FormatDriveBrowseErrorMessage(ex.Message)) { StatusCode = 502 };
            }
        }

        [Function("UploadIntranetDocument")]
        public async Task<IActionResult> UploadDocumentToLibraryAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intranet/documents/upload")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScoped(principal, Constants.Scopes.Intranet, out var userId, Constants.Roles.Editor) is { } gateFail)
                return gateFail;

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req,
                uploadLibraryDocValidator,
                invalidBodyMessage: "Invalid body. Expect { fileName, mimeType, contentBase64, category?, description?, pageId?, caption? }.");
            if (validationError != null)
                return validationError;

            var bytes = Convert.FromBase64String(body!.ContentBase64);

            var ct = req.FunctionContext.CancellationToken;
            var uploadMime = IntranetFileHelper.InferMimeType(body.FileName.Trim(), body.MimeType);
            var policy = await LoadIntranetMediaPolicyAsync(ct);
            if (!IntranetMediaPolicyRules.TryValidateUpload(
                    body.FileName.Trim(), uploadMime, bytes.Length, policy, out var policyError, bytes))
                return new BadRequestObjectResult(policyError);

            var tokenResult = await GetUserDriveTokenAsync(userId);
            if (tokenResult.Error != null) return tokenResult.Error;

            string? configuredParentId = await GetIntranetDriveParentFolderIdAsync();
            var layout = await ResolveDriveFolderLayoutAsync(tokenResult.Encrypted, configuredParentId, ct);
            var targetFolderId = layout.ResolveUploadFolderId(uploadMime, body.FileName.Trim());

            try
            {
                using var stream = new MemoryStream(bytes);
                var uploaded = await drive.UploadFileAsync(
                    tokenResult.Encrypted,
                    stream,
                    body.FileName.Trim(),
                    uploadMime,
                    string.IsNullOrEmpty(targetFolderId) ? null : targetFolderId,
                    ct);

                var createDto = new CreateIntranetDocumentDto
                {
                    DriveFileId = uploaded.Id ?? "",
                    Name = uploaded.Name ?? body.FileName.Trim(),
                    MimeType = IntranetFileHelper.InferMimeType(uploaded.Name ?? body.FileName.Trim(), uploaded.MimeType),
                    WebViewLink = uploaded.WebViewLink,
                    ThumbnailLink = uploaded.ThumbnailLink,
                    SizeBytes = uploaded.Size,
                    DriveLastModified = uploaded.ModifiedTimeDateTimeOffset?.UtcDateTime,
                    DriveOwnerEmail = uploaded.Owners?.FirstOrDefault()?.EmailAddress,
                    DriveOwnerName = uploaded.Owners?.FirstOrDefault()?.DisplayName,
                    Category = body.Category,
                    Description = body.Description
                };

                var doc = await UpsertIntranetDocumentAsync(createDto, uploaded, ct);

                if (!string.IsNullOrWhiteSpace(body.PageId))
                {
                    var pageCheck = await EnsureCanEditPageAsync(principal, body.PageId.Trim(), userId);
                    if (pageCheck.Error is { } permError) return permError;
                    var attached = await RegisterDriveFileAndAttachAsync(body.PageId.Trim(), uploaded, body.Caption, ct);
                    return new OkObjectResult(new UploadLibraryDocResultDto
                    {
                        Document = (await MapDocumentsToDtosAsync(new[] { doc }, includeUsage: true, ct)).First(),
                        PageAttachment = attached
                    });
                }

                return new OkObjectResult(new UploadLibraryDocResultDto
                {
                    Document = (await MapDocumentsToDtosAsync(new[] { doc }, includeUsage: true, ct)).First()
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "UploadIntranetDocument failed");
                return new ObjectResult(ApiErrorMessages.DriveOperationFailed) { StatusCode = 502 };
            }
        }

        // ---------- Helpers ----------

        private async Task<(IntranetPage? Page, IActionResult? Error)> EnsureCanEditPageAsync(ClaimsPrincipal principal, string pageId, string userId)
        {
            var page = await dbContext.IntranetPages
                .FirstOrDefaultAsync(p => p.PageId == pageId);

            if (page == null)
                return (null, new NotFoundObjectResult("Page not found."));

            if (page.RestrictEditingToOwner && !string.Equals(page.CreatedByUserId, userId, StringComparison.Ordinal))
            {
                if (!Constants.Roles.HasScopedAccess(principal, Constants.Scopes.Intranet, Constants.Roles.Admin))
                    return (null, new StatusCodeResult(403));
            }

            return (page, null);
        }

        private async Task<(string Encrypted, IActionResult? Error)> GetUserDriveTokenAsync(string userId)
        {
            if (!drive.IsConfigured)
                return (string.Empty, new ObjectResult("Google integration is not configured on the server.") { StatusCode = 503 });

            var settings = await dbContext.UserSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (settings == null || string.IsNullOrEmpty(settings.GoogleRefreshToken))
                return (string.Empty, new BadRequestObjectResult("Google Drive integration is not active for your account. It is normally set up automatically when you sign in. You can connect or reconnect from Settings."));

            return (settings.GoogleRefreshToken, null);
        }

        private async Task<IntranetMediaPolicy> LoadIntranetMediaPolicyAsync(CancellationToken ct)
        {
            var rows = await dbContext.AppSettings.AsNoTracking()
                .Where(s => s.Key == Constants.SettingKeys.IntranetImageMaxMegabytesByExtension)
                .Select(s => new { s.Key, s.Value })
                .ToListAsync(ct);

            return IntranetMediaPolicyRules.Parse(rows.Select(r => (r.Key, (string?)r.Value)));
        }

        private static IntranetMediaPolicyDto ToMediaPolicyDto(IntranetMediaPolicy policy) => new()
        {
            AllowedExtensions = policy.AllowedExtensions.ToList(),
            MaxUploadBytesByExtension = policy.MaxUploadBytesByExtension
                .ToDictionary(static kv => kv.Key, static kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            IsConfigured = policy.IsConfigured,
            AllowedExtensionsDisplay = IntranetMediaPolicyRules.FormatAllowedExtensions(policy),
            MaxUploadSizeDisplay = IntranetMediaPolicyRules.FormatMaxUploadSizes(policy)
        };

        private async Task<string?> GetIntranetDriveParentFolderIdAsync()
        {
            var row = await dbContext.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "IntranetDriveParentFolderId");

            return string.IsNullOrWhiteSpace(row?.Value) ? null : row.Value.Trim();
        }

        /// <summary>
        /// Resolves the Intranet root folder plus Images / Videos / Documents subfolders.
        /// If App Settings points at a type subfolder (e.g. Videos), climbs to the parent Intranet folder.
        /// </summary>
        private async Task<IntranetDriveFolderLayout> ResolveDriveFolderLayoutAsync(
            string encryptedToken, string? configuredFolderId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(configuredFolderId))
                return new IntranetDriveFolderLayout();

            try
            {
                var children = await drive.ListChildFoldersAsync(encryptedToken, configuredFolderId, ct);
                var childPairs = children
                    .Where(f => !string.IsNullOrEmpty(f.Id))
                    .Select(f => (f.Id!, f.Name ?? ""))
                    .ToList();

                var layout = IntranetDriveFolderLayout.FromParent(configuredFolderId, childPairs);
                if (layout.ImagesFolderId != null || layout.VideosFolderId != null || layout.DocumentsFolderId != null)
                    return layout;

                var meta = await drive.GetFileAsync(encryptedToken, configuredFolderId, ct);
                var folderName = meta.Name ?? "";
                if (!IsIntranetTypeFolderName(folderName))
                    return layout;

                var intranetParentId = meta.Parents?.FirstOrDefault(p => !string.IsNullOrEmpty(p));
                if (string.IsNullOrEmpty(intranetParentId))
                    return layout;

                var siblings = await drive.ListChildFoldersAsync(encryptedToken, intranetParentId, ct);
                var siblingPairs = siblings
                    .Where(f => !string.IsNullOrEmpty(f.Id))
                    .Select(f => (f.Id!, f.Name ?? ""))
                    .ToList();

                return IntranetDriveFolderLayout.FromParent(intranetParentId, siblingPairs);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ResolveDriveFolderLayout failed; using configured folder only.");
                return new IntranetDriveFolderLayout { ParentFolderId = configuredFolderId };
            }
        }

        private static bool IsIntranetTypeFolderName(string name) =>
            string.Equals(name, IntranetDriveFolderLayout.ImagesFolderName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, IntranetDriveFolderLayout.VideosFolderName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, IntranetDriveFolderLayout.DocumentsFolderName, StringComparison.OrdinalIgnoreCase);

        private static string FormatDriveBrowseErrorMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "Failed to browse Drive folder.";

            if (message.Contains("File not found", StringComparison.OrdinalIgnoreCase)
                || message.Contains("notFound", StringComparison.OrdinalIgnoreCase))
            {
                return "The Intranet Drive folder in App Settings was not found or you no longer have access. " +
                       "Update Intranet Drive Parent Folder ID to a valid folder.";
            }

            return "Failed to browse Drive folder. Check application logs for details.";
        }

        /// <summary>
        /// Media proxy is only for library files or images embedded in intranet page content —
        /// not arbitrary Drive IDs the caller's Google token might otherwise reach.
        /// </summary>
        private async Task<bool> IsDriveFileAllowedForMediaProxyAsync(string driveFileId, CancellationToken ct)
        {
            if (await dbContext.IntranetDocuments.AsNoTracking()
                    .AnyAsync(d => d.DriveFileId == driveFileId && d.IsActive, ct))
                return true;

            var contentUsage = await BuildDriveFileUsageFromContentAsync(new[] { driveFileId }, ct);
            return contentUsage.ContainsKey(driveFileId);
        }

        private async Task<Dictionary<string, List<IntranetDocumentPageUsageDto>>> BuildDriveFileUsageFromContentAsync(
            IReadOnlyList<string> driveFileIds,
            CancellationToken ct)
        {
            if (driveFileIds.Count == 0)
                return new Dictionary<string, List<IntranetDocumentPageUsageDto>>();

            var idSet = driveFileIds.ToHashSet(StringComparer.Ordinal);
            var pages = await dbContext.IntranetPages.AsNoTracking()
                .Select(p => new { p.PageId, p.Title, p.ContentMarkdown })
                .ToListAsync(ct);

            var map = new Dictionary<string, List<IntranetDocumentPageUsageDto>>(StringComparer.Ordinal);
            foreach (var page in pages)
            {
                var content = page.ContentMarkdown ?? "";
                foreach (var driveId in idSet)
                {
                    if (!IntranetFileHelper.IsDriveFileReferencedInHtml(content, driveId))
                        continue;

                    if (!map.TryGetValue(driveId, out var list))
                    {
                        list = new List<IntranetDocumentPageUsageDto>();
                        map[driveId] = list;
                    }

                    if (list.All(p => p.PageId != page.PageId))
                    {
                        list.Add(new IntranetDocumentPageUsageDto
                        {
                            PageId = page.PageId,
                            Title = page.Title,
                            IsReferencedInContent = true
                        });
                    }
                }
            }

            return map;
        }

        private async Task PropagateDriveFileDisplayNameToPagesAsync(
            string driveFileId,
            string newDisplayName,
            string? userId,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(driveFileId) || string.IsNullOrWhiteSpace(newDisplayName))
                return;

            var contentUsage = await BuildDriveFileUsageFromContentAsync(new[] { driveFileId }, ct);
            var pageIds = contentUsage.GetValueOrDefault(driveFileId)?
                .Select(p => p.PageId)
                .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
            if (pageIds.Count == 0)
                return;

            var pages = await dbContext.IntranetPages.Where(p => pageIds.Contains(p.PageId)).ToListAsync(ct);
            var changed = false;
            foreach (var page in pages)
            {
                var updatedHtml = IntranetFileHelper.UpdateDriveFileDisplayNameInHtml(
                    page.ContentMarkdown, driveFileId, newDisplayName);
                if (updatedHtml == page.ContentMarkdown)
                    continue;

                page.ContentMarkdown = updatedHtml;
                page.UpdatedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(userId))
                    page.UpdatedByUserId = userId;
                changed = true;
            }

            if (changed)
                await dbContext.SaveChangesAsync(ct);
        }

        private async Task<List<IntranetDocument>> QueryDocumentsAsync(HttpRequestData req, CancellationToken ct)
        {
            var q = dbContext.IntranetDocuments.AsNoTracking().Where(d => d.IsActive);

            var search = req.Query["search"];
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search!.Trim();
                q = q.Where(d => d.Name.Contains(term) ||
                                 (d.Category != null && d.Category.Contains(term)) ||
                                 (d.Description != null && d.Description.Contains(term)));
            }

            var category = req.Query["category"];
            if (!string.IsNullOrWhiteSpace(category))
                q = q.Where(d => d.Category == category!.Trim());

            var fileType = req.Query["fileType"];
            if (!string.IsNullOrWhiteSpace(fileType) && fileType != IntranetFileHelper.FileTypeAll)
            {
                q = fileType switch
                {
                    IntranetFileHelper.FileTypeImage => q.Where(d => d.MimeType != null && d.MimeType.StartsWith("image/")),
                    IntranetFileHelper.FileTypeVideo => q.Where(d => d.MimeType != null &&
                        (d.MimeType.StartsWith("video/") || d.MimeType == "application/vnd.google-apps.video")),
                    IntranetFileHelper.FileTypeDocument => q.Where(d => d.MimeType == null ||
                        (!d.MimeType.StartsWith("image/") && !d.MimeType.StartsWith("video/") && d.MimeType != "application/vnd.google-apps.video")),
                    _ => q
                };
            }

            var sort = (req.Query["sort"] ?? "featured").Trim().ToLowerInvariant();
            var sortDir = (req.Query["sortDir"] ?? "asc").Trim().ToLowerInvariant();
            var desc = sortDir == "desc";

            q = sort switch
            {
                "modified" => desc ? q.OrderByDescending(d => d.DriveLastModified).ThenBy(d => d.Name)
                                   : q.OrderBy(d => d.DriveLastModified).ThenBy(d => d.Name),
                "size" => desc ? q.OrderByDescending(d => d.SizeBytes).ThenBy(d => d.Name)
                               : q.OrderBy(d => d.SizeBytes).ThenBy(d => d.Name),
                "name" => desc ? q.OrderByDescending(d => d.Name)
                               : q.OrderBy(d => d.Name),
                _ => desc ? q.OrderByDescending(d => d.IsFeatured).ThenByDescending(d => d.Name)
                          : q.OrderByDescending(d => d.IsFeatured).ThenBy(d => d.Name)
            };

            return await q.ToListAsync(ct);
        }

        private async Task<List<IntranetDocumentDto>> MapDocumentsToDtosAsync(
            IEnumerable<IntranetDocument> docs,
            bool includeUsage,
            CancellationToken ct)
        {
            var docList = docs.ToList();
            var docIds = docList.Select(d => d.DocumentId).ToList();
            Dictionary<string, List<IntranetDocumentPageUsageDto>> usageMap = new();

            Dictionary<string, List<IntranetDocumentPageUsageDto>> contentUsageByDriveId = new();
            if (includeUsage && docIds.Count > 0)
            {
                var usageRows = await (
                    from link in dbContext.IntranetPageDocuments.AsNoTracking()
                    join page in dbContext.IntranetPages.AsNoTracking() on link.PageId equals page.PageId
                    where docIds.Contains(link.DocumentId)
                    orderby page.Title
                    select new
                    {
                        link.DocumentId,
                        link.PageId,
                        page.Title,
                        link.Caption
                    }).ToListAsync(ct);

                usageMap = usageRows
                    .GroupBy(x => x.DocumentId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => new IntranetDocumentPageUsageDto
                        {
                            PageId = x.PageId,
                            Title = x.Title,
                            Caption = x.Caption,
                            IsReferencedInContent = false
                        }).ToList());

                var driveIds = docList.Select(d => d.DriveFileId).Distinct(StringComparer.Ordinal).ToList();
                contentUsageByDriveId = await BuildDriveFileUsageFromContentAsync(driveIds, ct);
            }

            return docList.Select(d =>
            {
                usageMap.TryGetValue(d.DocumentId, out var attachmentPages);
                contentUsageByDriveId.TryGetValue(d.DriveFileId, out var contentPages);
                var usedOn = IntranetFileHelper.MergePageUsage(attachmentPages, contentPages);
                return new IntranetDocumentDto
                {
                    DocumentId = d.DocumentId,
                    DriveFileId = d.DriveFileId,
                    Name = d.Name,
                    MimeType = d.MimeType,
                    WebViewLink = d.WebViewLink,
                    ThumbnailLink = d.ThumbnailLink,
                    SizeBytes = d.SizeBytes,
                    DriveLastModified = d.DriveLastModified,
                    DriveOwnerEmail = d.DriveOwnerEmail,
                    DriveOwnerName = d.DriveOwnerName,
                    Description = d.Description,
                    Category = d.Category,
                    IsFeatured = d.IsFeatured,
                    IsActive = d.IsActive,
                    SyncedAt = d.SyncedAt,
                    UsageCount = usedOn.Count,
                    UsedOnPages = includeUsage ? usedOn : null
                };
            }).ToList();
        }

        private async Task<IntranetDocument> UpsertIntranetDocumentAsync(
            CreateIntranetDocumentDto dto,
            Google.Apis.Drive.v3.Data.File? driveFile,
            CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var existing = await dbContext.IntranetDocuments.FirstOrDefaultAsync(d => d.DriveFileId == dto.DriveFileId, ct);
            var owner = driveFile?.Owners?.FirstOrDefault();

            if (existing != null)
            {
                var previousName = existing.Name;
                existing.Name = !string.IsNullOrWhiteSpace(dto.Name)
                    ? dto.Name.Trim()
                    : driveFile?.Name ?? existing.Name;
                existing.MimeType = driveFile?.MimeType ?? dto.MimeType ?? existing.MimeType;
                existing.WebViewLink = driveFile?.WebViewLink ?? dto.WebViewLink ?? existing.WebViewLink;
                existing.ThumbnailLink = driveFile?.ThumbnailLink ?? dto.ThumbnailLink ?? existing.ThumbnailLink;
                existing.SizeBytes = driveFile?.Size ?? dto.SizeBytes ?? existing.SizeBytes;
                existing.DriveLastModified = driveFile?.ModifiedTimeDateTimeOffset?.UtcDateTime ?? dto.DriveLastModified ?? existing.DriveLastModified;
                existing.DriveOwnerEmail = owner?.EmailAddress ?? dto.DriveOwnerEmail ?? existing.DriveOwnerEmail;
                existing.DriveOwnerName = owner?.DisplayName ?? dto.DriveOwnerName ?? existing.DriveOwnerName;
                if (dto.Description != null) existing.Description = dto.Description;
                if (dto.Category != null) existing.Category = dto.Category;
                existing.IsFeatured = dto.IsFeatured;
                existing.IsActive = true;
                existing.SyncedAt = now;
                dbContext.IntranetDocuments.Update(existing);
                await dbContext.SaveChangesAsync(ct);

                if (!string.IsNullOrWhiteSpace(dto.Name)
                    && !string.Equals(previousName, existing.Name, StringComparison.Ordinal))
                {
                    await PropagateDriveFileDisplayNameToPagesAsync(existing.DriveFileId, existing.Name, null, ct);
                }

                return existing;
            }

            var doc = new IntranetDocument
            {
                DocumentId = Guid.NewGuid().ToString("N"),
                DriveFileId = dto.DriveFileId.Trim(),
                Name = !string.IsNullOrWhiteSpace(dto.Name)
                    ? dto.Name.Trim()
                    : driveFile?.Name ?? "Untitled",
                MimeType = driveFile?.MimeType ?? dto.MimeType,
                WebViewLink = driveFile?.WebViewLink ?? dto.WebViewLink,
                ThumbnailLink = driveFile?.ThumbnailLink ?? dto.ThumbnailLink,
                SizeBytes = driveFile?.Size ?? dto.SizeBytes,
                DriveLastModified = driveFile?.ModifiedTimeDateTimeOffset?.UtcDateTime ?? dto.DriveLastModified,
                DriveOwnerEmail = owner?.EmailAddress ?? dto.DriveOwnerEmail,
                DriveOwnerName = owner?.DisplayName ?? dto.DriveOwnerName,
                Description = dto.Description,
                Category = dto.Category,
                IsFeatured = dto.IsFeatured,
                IsActive = true,
                SyncedAt = now
            };
            dbContext.IntranetDocuments.Add(doc);
            await dbContext.SaveChangesAsync(ct);
            return doc;
        }

        private static string ResolveMimeType(string? explicitMime, string? kind)
        {
            if (!string.IsNullOrWhiteSpace(explicitMime))
                return explicitMime.Trim();

            var k = (kind ?? "document").Trim().ToLowerInvariant();
            return k switch
            {
                "document" or "doc" or "docs" => "application/vnd.google-apps.document",
                "spreadsheet" or "sheet" or "sheets" => "application/vnd.google-apps.spreadsheet",
                "presentation" or "slide" or "slides" => "application/vnd.google-apps.presentation",
                "form" => "application/vnd.google-apps.form",
                _ => "application/vnd.google-apps.document"
            };
        }

        /// <summary>
        /// Idempotent register (upsert metadata) + attach (or update caption/sort on re-attach).
        /// Returns the flattened page attachment DTO the client can splice into its editor list.
        /// </summary>
        private async Task<IntranetPageDocumentDto> RegisterDriveFileAndAttachAsync(
            string pageId,
            Google.Apis.Drive.v3.Data.File file,
            string? caption,
            CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            var doc = await dbContext.IntranetDocuments
                .FirstOrDefaultAsync(d => d.DriveFileId == file.Id, ct);

            if (doc == null)
            {
                var owner = file.Owners?.FirstOrDefault();
                doc = new IntranetDocument
                {
                    DocumentId = Guid.NewGuid().ToString("N"),
                    DriveFileId = file.Id ?? throw new InvalidOperationException("Drive returned no file id"),
                    Name = file.Name ?? "Untitled",
                    MimeType = file.MimeType,
                    WebViewLink = file.WebViewLink,
                    ThumbnailLink = file.ThumbnailLink,
                    SizeBytes = file.Size,
                    DriveLastModified = file.ModifiedTimeDateTimeOffset?.UtcDateTime,
                    DriveOwnerEmail = owner?.EmailAddress,
                    DriveOwnerName = owner?.DisplayName,
                    SyncedAt = now,
                    IsActive = true
                };
                dbContext.IntranetDocuments.Add(doc);
            }
            else
            {
                // Refresh cached metadata from Drive
                doc.Name = file.Name ?? doc.Name;
                doc.MimeType = file.MimeType ?? doc.MimeType;
                doc.WebViewLink = file.WebViewLink ?? doc.WebViewLink;
                doc.ThumbnailLink = file.ThumbnailLink ?? doc.ThumbnailLink;
                doc.SizeBytes = file.Size ?? doc.SizeBytes;
                doc.DriveLastModified = file.ModifiedTimeDateTimeOffset?.UtcDateTime ?? doc.DriveLastModified;
                var owner = file.Owners?.FirstOrDefault();
                if (owner != null)
                {
                    doc.DriveOwnerEmail = owner.EmailAddress ?? doc.DriveOwnerEmail;
                    doc.DriveOwnerName = owner.DisplayName ?? doc.DriveOwnerName;
                }
                doc.SyncedAt = now;
                dbContext.IntranetDocuments.Update(doc);
            }

            // Junction (composite key PageId + DocumentId)
            var link = await dbContext.IntranetPageDocuments
                .FirstOrDefaultAsync(x => x.PageId == pageId && x.DocumentId == doc.DocumentId, ct);

            int nextSort = link?.SortOrder ?? await dbContext.IntranetPageDocuments
                .Where(x => x.PageId == pageId)
                .Select(x => (int?)x.SortOrder)
                .MaxAsync(ct) + 1 ?? 0;

            if (link == null)
            {
                link = new IntranetPageDocument
                {
                    PageId = pageId,
                    DocumentId = doc.DocumentId,
                    SortOrder = nextSort,
                    Caption = caption
                };
                dbContext.IntranetPageDocuments.Add(link);
            }
            else
            {
                if (caption != null) link.Caption = caption;
                // Do not auto-bump sort on re-attach unless client asks
                dbContext.IntranetPageDocuments.Update(link);
            }

            await dbContext.SaveChangesAsync(ct);

            return new IntranetPageDocumentDto
            {
                DocumentId = doc.DocumentId,
                DriveFileId = doc.DriveFileId,
                Name = doc.Name,
                MimeType = doc.MimeType,
                WebViewLink = doc.WebViewLink,
                ThumbnailLink = doc.ThumbnailLink,
                SortOrder = link.SortOrder,
                Caption = link.Caption
            };
        }

        private async Task<string?> ResolveParentPageTitleAsync(string? parentPageId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(parentPageId))
                return null;

            return await dbContext.IntranetPages.AsNoTracking()
                .Where(p => p.PageId == parentPageId)
                .Select(p => p.Title)
                .FirstOrDefaultAsync(ct);
        }

        private static Google.Apis.Drive.v3.Data.File BuildDriveFileFromAttachRequest(AttachExistingRequest body)
            => new()
            {
                Id = body.DriveFileId.Trim(),
                Name = body.Name ?? "Untitled",
                MimeType = body.MimeType,
                WebViewLink = body.WebViewLink,
                ThumbnailLink = body.ThumbnailLink,
                Size = body.SizeBytes,
                ModifiedTimeDateTimeOffset = body.DriveLastModified.HasValue
                    ? new DateTimeOffset(body.DriveLastModified.Value, TimeSpan.Zero)
                    : null
            };

        private static bool IsDriveApiNotEnabled(Google.GoogleApiException ex) =>
            ex.Message.Contains("Drive API has not been used", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("accessNotConfigured", StringComparison.OrdinalIgnoreCase);
    }
}