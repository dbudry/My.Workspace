using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using My.DAL.Models;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.Crm;
using My.Shared.Rules;

namespace My.Functions;

public partial class CrmFunctions
{
    [Function("UploadCrmAttachment")]
    public async Task<IActionResult> UploadAttachmentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crmpotentials/{id}/attachments")] HttpRequestData req,
        string id)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out var userId, Constants.Roles.Editor) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, uploadAttachmentValidator);
        if (error != null) return error;

        var potential = await db.Potentials.AsNoTracking()
            .Where(row => row.PotentialId == id)
            .Select(row => new { row.PotentialId, row.CrmStageId })
            .FirstOrDefaultAsync();
        if (potential == null)
            return new NotFoundObjectResult("Potential not found.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(dto!.ContentBase64);
        }
        catch (FormatException)
        {
            return new BadRequestObjectResult("File content is not valid.");
        }

        var fileName = CrmAttachmentRules.SafeFileName(dto.FileName);
        if (!CrmAttachmentRules.TryValidate(fileName, bytes.Length, out var fileError))
            return new BadRequestObjectResult(fileError);

        var stageId = BlankToNull(dto.CrmStageId) ?? potential.CrmStageId;
        var stage = await db.CrmStages.AsNoTracking()
            .Where(row => row.CrmStageId == stageId)
            .Select(row => new { row.CrmStageId, row.Name })
            .FirstOrDefaultAsync();
        if (stage == null)
            return new BadRequestObjectResult("Stage not found.");

        var access = await TryGetCrmDriveAsync();
        if (access.Error != null) return access.Error;

        string driveFileId;
        try
        {
            await using var stream = new MemoryStream(bytes);
            var mime = string.IsNullOrWhiteSpace(dto.MimeType) ? "application/octet-stream" : dto.MimeType.Trim();
            if (mime.Length > 100)
                mime = mime[..100];
            var uploaded = await drive.UploadFileAsync(access.Token!, stream, fileName, mime, access.FolderId);
            if (string.IsNullOrEmpty(uploaded.Id))
                return new ObjectResult("The file could not be saved to Drive.") { StatusCode = 409 };
            driveFileId = uploaded.Id;
        }
        catch (Exception)
        {
            return new ObjectResult("The file could not be saved to Drive.") { StatusCode = 409 };
        }

        var attachment = new CrmAttachment
        {
            CrmAttachmentId = Guid.NewGuid().ToString(),
            PotentialId = potential.PotentialId,
            CrmStageId = stage.CrmStageId,
            OriginalFileName = fileName,
            MimeType = string.IsNullOrWhiteSpace(dto.MimeType) ? "application/octet-stream" : TrimMime(dto.MimeType),
            SizeBytes = bytes.Length,
            DriveFileId = driveFileId,
            UploadedAt = DateTimeOffset.UtcNow,
            UploadedByUserId = userId
        };
        db.CrmAttachments.Add(attachment);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            await TryDeleteDriveFileAsync(access.Token!, driveFileId);
            throw;
        }

        return new OkObjectResult(new CrmAttachmentDto
        {
            CrmAttachmentId = attachment.CrmAttachmentId,
            PotentialId = attachment.PotentialId,
            CrmStageId = attachment.CrmStageId,
            StageName = stage.Name,
            OriginalFileName = attachment.OriginalFileName,
            MimeType = attachment.MimeType,
            SizeBytes = attachment.SizeBytes,
            UploadedAt = attachment.UploadedAt,
            UploadedByName = await AuthorNameAsync(userId)
        });
    }

    [Function("MoveCrmAttachmentStage")]
    public async Task<IActionResult> MoveAttachmentStageAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "crmpotentials/{id}/attachments/{attachmentId}/stage")] HttpRequestData req,
        string id,
        string attachmentId)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Editor) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, moveAttachmentValidator);
        if (error != null) return error;

        var attachment = await db.CrmAttachments.FirstOrDefaultAsync(row =>
            row.CrmAttachmentId == attachmentId && row.PotentialId == id);
        if (attachment == null)
            return new NotFoundObjectResult("Attachment not found.");

        var stage = await db.CrmStages.AsNoTracking()
            .Where(row => row.CrmStageId == dto!.CrmStageId)
            .Select(row => new { row.CrmStageId, row.Name })
            .FirstOrDefaultAsync();
        if (stage == null)
            return new BadRequestObjectResult("Stage not found.");

        attachment.CrmStageId = stage.CrmStageId;
        await db.SaveChangesAsync();
        return new OkObjectResult(new CrmAttachmentDto
        {
            CrmAttachmentId = attachment.CrmAttachmentId,
            PotentialId = attachment.PotentialId,
            CrmStageId = attachment.CrmStageId,
            StageName = stage.Name,
            OriginalFileName = attachment.OriginalFileName,
            MimeType = attachment.MimeType,
            SizeBytes = attachment.SizeBytes,
            UploadedAt = attachment.UploadedAt,
            UploadedByName = await AuthorNameAsync(attachment.UploadedByUserId)
        });
    }

    [Function("DeleteCrmAttachment")]
    public async Task<IActionResult> DeleteAttachmentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "crmpotentials/{id}/attachments/{attachmentId}")] HttpRequestData req,
        string id,
        string attachmentId)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        var attachment = await db.CrmAttachments.FirstOrDefaultAsync(row =>
            row.CrmAttachmentId == attachmentId && row.PotentialId == id);
        if (attachment == null)
            return new NotFoundObjectResult("Attachment not found.");

        if (!string.IsNullOrEmpty(attachment.DriveFileId))
        {
            var access = await TryGetCrmDriveAsync();
            if (access.Error != null) return access.Error;
            var deleted = await TryDeleteDriveFileAsync(access.Token!, attachment.DriveFileId);
            if (deleted != null) return deleted;
        }

        db.CrmAttachments.Remove(attachment);
        await db.SaveChangesAsync();
        return new NoContentResult();
    }

    [Function("DownloadCrmAttachment")]
    public async Task<IActionResult> DownloadAttachmentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "crmpotentials/{id}/attachments/{attachmentId}")] HttpRequestData req,
        string id,
        string attachmentId)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireCrmRead(principal, out _) is { } denied) return denied;

        var attachment = await db.CrmAttachments.AsNoTracking().FirstOrDefaultAsync(row =>
            row.CrmAttachmentId == attachmentId && row.PotentialId == id);
        if (attachment == null)
            return new NotFoundObjectResult("Attachment not found.");

        var access = await TryGetCrmDriveAsync();
        if (access.Error != null) return access.Error;

        try
        {
            var downloaded = await drive.DownloadFileContentAsync(access.Token!, attachment.DriveFileId);
            return new OkObjectResult(new CrmAttachmentContentDto
            {
                FileName = attachment.OriginalFileName,
                MimeType = string.IsNullOrWhiteSpace(downloaded.MimeType) ? attachment.MimeType : downloaded.MimeType,
                ContentBase64 = Convert.ToBase64String(downloaded.Content)
            });
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new NotFoundObjectResult("That file is no longer in Drive.");
        }
        catch (Exception)
        {
            return new ObjectResult("The file could not be downloaded.") { StatusCode = 409 };
        }
    }

    private static string TrimMime(string mime)
    {
        var trimmed = mime.Trim();
        return trimmed.Length <= 100 ? trimmed : trimmed[..100];
    }
}
