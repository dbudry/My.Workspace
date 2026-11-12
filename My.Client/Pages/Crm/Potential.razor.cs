using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MudBlazor;
using My.Client.Extensions;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.Contact;
using My.Shared.Dtos.Crm;
using My.Shared.Rules;

namespace My.Client.Pages.Crm;

public partial class Potential
{
    private const string NewContactChoice = "__new__";

    [Parameter] public string? PotentialId { get; set; }

    [Inject] private CrmService Crm { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
    [Inject] private IJSRuntime Js { get; set; } = null!;
    [Inject] private AppSettingsCache AppSettings { get; set; } = null!;
    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    private bool IsNew => string.IsNullOrEmpty(PotentialId) || PotentialId == "new";
    private bool loading = true;
    private bool saving;
    private bool canEdit;
    private bool canPost;
    private bool canManage;
    private bool isArchived;
    private string name = "";
    private string? description;
    private string? stageId;
    private int probability;
    private decimal? amount;
    private DateTime? expectedClose;
    private DateTime? actualClose;
    private string? organizationId;
    private string? organizationName;
    private string? contactChoice;
    private string relationshipType = "";
    private string newRelationshipType = CrmRelationshipTypeRules.DecisionMaker;
    private string newContactName = "";
    private string newContactType = "";
    private string newContactTitle = "";
    private string newContactPhone = "";
    private string newContactEmail = "";
    private string? createdByName;
    private string? modifiedByName;
    private string createdLabel = "";
    private string modifiedLabel = "";
    private string statusBody = "";
    private DateTime? statusDate = DateTime.Today;
    private string? statusStageId;
    private string? savedStageId;
    private string discussionMessage = "";
    private List<CrmStageDto> stages = new();
    private List<CrmRelationshipTypeDto> relationshipTypes = new();
    private IReadOnlyList<string> contactTypes = ContactTypeRules.DefaultTypes;
    private List<ContactDto> contacts = new();
    private List<PotentialStatusDto> statuses = new();
    private List<CrmDiscussionDto> discussions = new();
    private List<CrmAttachmentDto> attachments = new();
    private readonly List<UploadCrmAttachmentDto> pendingFiles = new();

    private string ExpectedRevenueText => CrmStageRules.ExpectedRevenue(amount, probability).ToString("C");

    private string Accept => string.Join(",", CrmAttachmentRules.AllowedExtensions);

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthStateTask).User;
        canEdit = Constants.Roles.HasScopedAccess(user, Constants.Scopes.Crm, Constants.Roles.Editor);
        canPost = Constants.Roles.HasScopedAccess(user, Constants.Scopes.Crm, Constants.Roles.User);
        canManage = Constants.Roles.HasScopedAccess(user, Constants.Scopes.Crm, Constants.Roles.Manager);
        contactTypes = await ContactTypeOptions.GetAsync(AppSettings);
        newContactType = ContactTypeOptions.DefaultForNew(contactTypes);
        stages = await Crm.StagesAsync() ?? new();
        relationshipTypes = await Crm.RelationshipTypesAsync() ?? new();
        if (relationshipTypes.Count > 0
            && relationshipTypes.All(type => !string.Equals(type.Name, newRelationshipType, StringComparison.OrdinalIgnoreCase)))
        {
            newRelationshipType = relationshipTypes[0].Name;
        }

        if (IsNew)
        {
            var first = stages.FirstOrDefault();
            if (first != null)
            {
                stageId = first.CrmStageId;
                probability = first.DefaultProbability;
                statusStageId = first.CrmStageId;
            }

            loading = false;
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            var detail = await Crm.GetAsync(PotentialId!);
            if (detail == null)
            {
                Snackbar.Add("Couldn't load that potential.", Severity.Error);
                return;
            }

            name = detail.Name;
            description = detail.Description;
            stageId = detail.CrmStageId;
            savedStageId = detail.CrmStageId;
            probability = detail.Probability;
            amount = detail.Amount;
            expectedClose = detail.ExpectedCloseDate;
            actualClose = detail.ActualCloseDate;
            organizationId = detail.OrganizationId;
            organizationName = detail.OrganizationName;
            contactChoice = detail.ContactId;
            relationshipType = detail.RelationshipType ?? "";
            isArchived = detail.IsArchived;
            createdByName = detail.CreatedByName;
            modifiedByName = detail.ModifiedByName;
            createdLabel = detail.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            modifiedLabel = detail.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            statuses = detail.Statuses;
            discussions = detail.Discussions;
            attachments = detail.Attachments;
            statusStageId = detail.CrmStageId;
            if (!string.IsNullOrEmpty(organizationId))
                await LoadContactsAsync();
        }
        finally
        {
            loading = false;
        }
    }

    private Task OnStageChanged(string? value)
    {
        if (value == stageId)
            return Task.CompletedTask;
        stageId = value;
        statusStageId = value;
        var stage = stages.FirstOrDefault(item => item.CrmStageId == value);
        if (stage != null)
            probability = stage.DefaultProbability;
        return Task.CompletedTask;
    }

    private async Task OnOrganizationChanged(string? value)
    {
        if (organizationId == value)
            return;
        organizationId = value;
        contactChoice = null;
        relationshipType = "";
        contacts.Clear();
        if (string.IsNullOrEmpty(value))
            return;
        await LoadContactsAsync();
        var maker = contacts.FirstOrDefault(contact =>
            string.Equals(contact.RelationshipType, CrmRelationshipTypeRules.DecisionMaker, StringComparison.OrdinalIgnoreCase));
        if (maker != null)
        {
            contactChoice = maker.ContactId;
            relationshipType = maker.RelationshipType ?? "";
        }
    }

    private Task OnContactChanged(string? value)
    {
        contactChoice = value;
        if (value == NewContactChoice || string.IsNullOrEmpty(value))
        {
            relationshipType = "";
            return Task.CompletedTask;
        }

        var contact = contacts.FirstOrDefault(item => item.ContactId == value);
        relationshipType = contact?.RelationshipType ?? "";
        return Task.CompletedTask;
    }

    private void OnRelationshipChanged(string? value)
    {
        relationshipType = value ?? "";
        var contact = contacts.FirstOrDefault(item => item.ContactId == contactChoice);
        if (contact != null)
            contact.RelationshipType = string.IsNullOrEmpty(relationshipType) ? null : relationshipType;
    }

    private async Task LoadContactsAsync()
    {
        var client = ClientFactory.CreateClient(Constants.API.ClientName);
        var response = await client.GetAsync($"{Constants.API.Contact.Get}?organizationId={organizationId}");
        if (!response.IsSuccessStatusCode)
            return;
        contacts = await response.Content.ReadFromJsonAsync<List<ContactDto>>() ?? new();
    }

    private void OnOrganizationNameChanged(string? value) => organizationName = value;

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Snackbar.Add("Potential name is required.", Severity.Warning);
            return;
        }

        if (string.IsNullOrEmpty(stageId))
        {
            Snackbar.Add("Stage is required.", Severity.Warning);
            return;
        }

        if (!string.IsNullOrEmpty(organizationId) && string.IsNullOrEmpty(contactChoice))
        {
            Snackbar.Add("Contact is required.", Severity.Warning);
            return;
        }

        if (contactChoice == NewContactChoice && string.IsNullOrWhiteSpace(newContactName))
        {
            Snackbar.Add("New contact name is required.", Severity.Warning);
            return;
        }

        saving = true;
        try
        {
            var savedContactId = await ResolveContactIdAsync();
            if (!string.IsNullOrEmpty(organizationId) && string.IsNullOrEmpty(savedContactId))
                return;

            string? sentRelationship = null;
            if (!string.IsNullOrEmpty(savedContactId))
                sentRelationship = contactChoice == NewContactChoice ? newRelationshipType : relationshipType;

            HttpResponseMessage response;
            if (IsNew)
            {
                response = await Crm.CreateAsync(new CreatePotentialDto
                {
                    Name = name.Trim(),
                    Description = description,
                    CrmStageId = stageId,
                    Probability = probability,
                    Amount = amount,
                    ExpectedCloseDate = expectedClose,
                    ActualCloseDate = actualClose,
                    OrganizationId = organizationId,
                    ContactId = savedContactId,
                    RelationshipType = sentRelationship
                });
            }
            else
            {
                response = await Crm.UpdateAsync(new UpdatePotentialDto
                {
                    PotentialId = PotentialId!,
                    Name = name.Trim(),
                    Description = description,
                    CrmStageId = stageId,
                    Probability = probability,
                    Amount = amount,
                    ExpectedCloseDate = expectedClose,
                    ActualCloseDate = actualClose,
                    OrganizationId = organizationId,
                    ContactId = savedContactId,
                    RelationshipType = sentRelationship
                });
            }

            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadMessage(response, "Couldn't save the potential."), Severity.Error);
                return;
            }

            var saved = await response.Content.ReadFromJsonAsync<PotentialDetailDto>();
            Snackbar.Add("Potential saved.", Severity.Success);
            if (IsNew && saved != null)
            {
                await UploadPendingFilesAsync(saved.PotentialId);
                Navigation.NavigateTo($"crm/potential/{saved.PotentialId}");
                return;
            }

            if (saved != null)
                ApplyDetail(saved);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't save the potential.");
        }
        finally
        {
            saving = false;
        }
    }

    private async Task UploadPendingFilesAsync(string potentialId)
    {
        var failed = new List<string>();
        foreach (var upload in pendingFiles.ToList())
        {
            try
            {
                upload.CrmStageId = stageId;
                var response = await Crm.UploadAttachmentAsync(potentialId, upload);
                if (!response.IsSuccessStatusCode)
                    failed.Add(upload.FileName);
            }
            catch (Exception)
            {
                failed.Add(upload.FileName);
            }
        }

        pendingFiles.Clear();
        if (failed.Count > 0)
            Snackbar.Add($"Potential saved. These files did not upload, add them again: {string.Join(", ", failed)}", Severity.Warning);
    }

    private void ApplyDetail(PotentialDetailDto detail)
    {
        savedStageId = detail.CrmStageId;
        actualClose = detail.ActualCloseDate;
        probability = detail.Probability;
        relationshipType = detail.RelationshipType ?? relationshipType;
        createdLabel = detail.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        modifiedLabel = detail.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        createdByName = detail.CreatedByName;
        modifiedByName = detail.ModifiedByName;
        isArchived = detail.IsArchived;
    }

    private async Task<string?> ResolveContactIdAsync()
    {
        if (string.IsNullOrEmpty(organizationId) || contactChoice != NewContactChoice)
            return string.IsNullOrEmpty(contactChoice) ? null : contactChoice;

        var response = await Crm.CreateContactAsync(new CreateContactDto
        {
            Name = newContactName.Trim(),
            ContactType = newContactType,
            RelationshipType = string.IsNullOrWhiteSpace(newRelationshipType)
                ? CrmRelationshipTypeRules.DecisionMaker
                : newRelationshipType,
            Title = string.IsNullOrWhiteSpace(newContactTitle) ? null : newContactTitle.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(newContactPhone) ? null : newContactPhone.Trim(),
            Email = string.IsNullOrWhiteSpace(newContactEmail) ? null : newContactEmail.Trim(),
            OrganizationId = organizationId
        });
        if (!response.IsSuccessStatusCode)
        {
            Snackbar.Add(await ReadMessage(response, "Couldn't save the new contact."), Severity.Error);
            return null;
        }

        var created = await response.Content.ReadFromJsonAsync<ContactDto>();
        if (created == null)
            return null;
        contacts.Add(created);
        contactChoice = created.ContactId;
        relationshipType = created.RelationshipType ?? newRelationshipType;
        newContactName = "";
        return created.ContactId;
    }

    private async Task ArchiveAsync()
    {
        if (IsNew) return;
        saving = true;
        try
        {
            var response = await Crm.ArchiveAsync(PotentialId!, !isArchived);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadMessage(response, "Couldn't update the archive flag."), Severity.Error);
                return;
            }

            isArchived = !isArchived;
            Snackbar.Add(isArchived ? "Potential archived." : "Potential restored.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't update the archive flag.");
        }
        finally
        {
            saving = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (IsNew) return;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Confirm Delete",
            $"Delete potential \"{name}\"?",
            yesText: "Delete",
            cancelText: "Cancel");
        if (confirmed != true)
            return;

        saving = true;
        try
        {
            var response = await Crm.DeleteAsync(PotentialId!);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadMessage(response, "Couldn't delete the potential."), Severity.Error);
                return;
            }

            Navigation.NavigateTo("crm");
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't delete the potential.");
        }
        finally
        {
            saving = false;
        }
    }

    private async Task AddStatusAsync()
    {
        if (IsNew || string.IsNullOrWhiteSpace(statusBody) || statusDate == null || string.IsNullOrEmpty(statusStageId))
        {
            Snackbar.Add("Status, date, and stage are required.", Severity.Warning);
            return;
        }

        saving = true;
        try
        {
            var response = await Crm.CreateStatusAsync(PotentialId!, new CreatePotentialStatusDto
            {
                Body = statusBody.Trim(),
                StatusDate = statusDate.Value,
                CrmStageId = statusStageId
            });
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadMessage(response, "Couldn't add the status."), Severity.Error);
                return;
            }

            var created = await response.Content.ReadFromJsonAsync<PotentialStatusDto>();
            if (created != null)
                statuses.Insert(0, created);
            statusBody = "";
            statusDate = DateTime.Today;
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't add the status.");
        }
        finally
        {
            saving = false;
        }
    }

    private async Task AddDiscussionAsync()
    {
        if (IsNew || string.IsNullOrWhiteSpace(discussionMessage))
        {
            Snackbar.Add("Discussion is required.", Severity.Warning);
            return;
        }

        if (!string.Equals(savedStageId, stageId, StringComparison.Ordinal))
        {
            Snackbar.Add("Save this potential before posting on the new stage.", Severity.Warning);
            return;
        }

        saving = true;
        try
        {
            var response = await Crm.CreateDiscussionAsync(PotentialId!, new CreateCrmDiscussionDto
            {
                Message = discussionMessage.Trim()
            });
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadMessage(response, "Couldn't post the discussion."), Severity.Error);
                return;
            }

            var created = await response.Content.ReadFromJsonAsync<CrmDiscussionDto>();
            if (created != null)
            {
                discussions.Insert(0, created);
                if (!string.IsNullOrWhiteSpace(created.EmailWarning))
                    Snackbar.Add(created.EmailWarning, Severity.Warning);
            }

            discussionMessage = "";
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't post the discussion.");
        }
        finally
        {
            saving = false;
        }
    }

    private async Task OnFileAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (!CrmAttachmentRules.TryValidate(file.Name, (int)Math.Min(file.Size, int.MaxValue), out var error))
        {
            Snackbar.Add(error ?? "That file can't be attached.", Severity.Warning);
            return;
        }

        saving = true;
        try
        {
            await using var stream = file.OpenReadStream(CrmAttachmentRules.MaxBytes);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var upload = new UploadCrmAttachmentDto
            {
                FileName = file.Name,
                MimeType = file.ContentType,
                ContentBase64 = Convert.ToBase64String(buffer.ToArray()),
                CrmStageId = stageId
            };
            if (IsNew)
            {
                pendingFiles.Add(upload);
                return;
            }

            var response = await Crm.UploadAttachmentAsync(PotentialId!, upload);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadMessage(response, "Couldn't upload the file."), Severity.Error);
                return;
            }

            var created = await response.Content.ReadFromJsonAsync<CrmAttachmentDto>();
            if (created != null)
                attachments.Insert(0, created);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't upload the file.");
        }
        finally
        {
            saving = false;
        }
    }

    private async Task MoveFileAsync(CrmAttachmentDto file, string? stage)
    {
        if (string.IsNullOrEmpty(stage) || stage == file.CrmStageId || IsNew)
            return;
        var response = await Crm.MoveAttachmentAsync(PotentialId!, file.CrmAttachmentId, stage);
        if (!response.IsSuccessStatusCode)
        {
            Snackbar.Add(await ReadMessage(response, "Couldn't move the file."), Severity.Error);
            return;
        }

        var updated = await response.Content.ReadFromJsonAsync<CrmAttachmentDto>();
        if (updated == null)
            return;
        var index = attachments.FindIndex(item => item.CrmAttachmentId == file.CrmAttachmentId);
        if (index >= 0)
            attachments[index] = updated;
    }

    private async Task DeleteFileAsync(CrmAttachmentDto file)
    {
        if (IsNew) return;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Confirm Delete",
            $"Delete \"{file.OriginalFileName}\"?",
            yesText: "Delete",
            cancelText: "Cancel");
        if (confirmed != true)
            return;
        var response = await Crm.DeleteAttachmentAsync(PotentialId!, file.CrmAttachmentId);
        if (!response.IsSuccessStatusCode)
        {
            Snackbar.Add(await ReadMessage(response, "Couldn't delete the file."), Severity.Error);
            return;
        }

        attachments.RemoveAll(item => item.CrmAttachmentId == file.CrmAttachmentId);
    }

    private async Task DownloadAsync(CrmAttachmentDto file)
    {
        if (IsNew) return;
        try
        {
            var content = await Crm.DownloadAttachmentAsync(PotentialId!, file.CrmAttachmentId);
            if (content == null)
            {
                Snackbar.Add("Couldn't download the file.", Severity.Error);
                return;
            }

            await Js.InvokeVoidAsync("fileSave.download", content.ContentBase64, content.MimeType, content.FileName);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't download the file.");
        }
    }

    private static async Task<string> ReadMessage(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        return ApiErrorBodyRules.Format(body, fallback);
    }

    private IEnumerable<PotentialStatusDto> StageStatuses =>
        statuses.Where(item => item.CrmStageId == stageId);

    private IEnumerable<CrmDiscussionDto> StageDiscussions =>
        discussions.Where(item => item.CrmStageId == stageId);

    private IEnumerable<CrmAttachmentDto> StageAttachments =>
        attachments.Where(item => item.CrmStageId == stageId);

    private static Color StageColor(string outcome) => outcome switch
    {
        CrmStageRules.ClosedWon => Color.Success,
        CrmStageRules.ClosedLost => Color.Error,
        _ => Color.Info
    };
}
