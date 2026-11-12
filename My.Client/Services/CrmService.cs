using System.Net.Http.Json;
using My.Shared.Constants;
using My.Shared.Dtos.Contact;
using My.Shared.Dtos.Crm;
using My.Shared.Dtos.Paging;
using My.Shared.Helpers;

namespace My.Client.Services;

public class CrmService
{
    private readonly IHttpClientFactory clientFactory;

    public CrmService(IHttpClientFactory clientFactory)
    {
        this.clientFactory = clientFactory;
    }

    public async Task<PagedResponse<PotentialListItemDto>?> ListAsync(
        ListQueryParameters query,
        string? stage = null,
        string? organizationId = null)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        var path = ListQueryUrlBuilder.Build(
            Constants.API.Crm.Potentials,
            query,
            ("stage", stage),
            ("organizationId", organizationId));
        var response = await client.GetAsync(path);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PagedResponse<PotentialListItemDto>>();
    }

    public async Task<PotentialDetailDto?> GetAsync(string potentialId)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        var response = await client.GetAsync($"{Constants.API.Crm.Potentials}/{potentialId}");
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PotentialDetailDto>();
    }

    public Task<HttpResponseMessage> CreateAsync(CreatePotentialDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync(Constants.API.Crm.Potentials, dto);
    }

    public Task<HttpResponseMessage> UpdateAsync(UpdatePotentialDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PutAsJsonAsync(Constants.API.Crm.Potentials, dto);
    }

    public Task<HttpResponseMessage> ArchiveAsync(string potentialId, bool isArchived)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync(
            $"{Constants.API.Crm.Potentials}/{potentialId}/archive",
            new SetPotentialArchivedDto { IsArchived = isArchived });
    }

    public Task<HttpResponseMessage> DeleteAsync(string potentialId)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.DeleteAsync($"{Constants.API.Crm.Potentials}/{potentialId}");
    }

    public Task<HttpResponseMessage> CreateContactAsync(CreateContactDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync(Constants.API.Crm.Contacts, dto);
    }

    public async Task<List<CrmStageDto>?> StagesAsync()
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        var response = await client.GetAsync(Constants.API.Crm.Stages);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<List<CrmStageDto>>();
    }

    public Task<HttpResponseMessage> CreateStageAsync(CreateCrmStageDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync(Constants.API.Crm.Stages, dto);
    }

    public Task<HttpResponseMessage> UpdateStageAsync(UpdateCrmStageDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PutAsJsonAsync(Constants.API.Crm.Stages, dto);
    }

    public Task<HttpResponseMessage> DeleteStageAsync(string stageId)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.DeleteAsync($"{Constants.API.Crm.Stages}/{stageId}");
    }

    public Task<HttpResponseMessage> ReorderStagesAsync(IReadOnlyList<string> stageIds)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync($"{Constants.API.Crm.Stages}/reorder", new ReorderCrmStagesDto
        {
            StageIds = stageIds.ToList()
        });
    }

    public async Task<List<CrmRelationshipTypeDto>?> RelationshipTypesAsync()
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        var response = await client.GetAsync(Constants.API.Crm.RelationshipTypes);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<List<CrmRelationshipTypeDto>>();
    }

    public Task<HttpResponseMessage> SaveRelationshipTypesAsync(IReadOnlyList<CrmRelationshipTypeDto> types)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PutAsJsonAsync(Constants.API.Crm.RelationshipTypes, new SaveCrmRelationshipTypesDto
        {
            Types = types.ToList()
        });
    }

    public async Task<List<CrmNotifyUserDto>?> NotifyUsersAsync()
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        var response = await client.GetAsync(Constants.API.Crm.NotifyUsers);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<List<CrmNotifyUserDto>>();
    }

    public Task<HttpResponseMessage> SaveNotifyUsersAsync(IEnumerable<string> userIds)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PutAsJsonAsync(Constants.API.Crm.NotifyUsers, new SaveCrmNotifyUsersDto
        {
            UserIds = userIds.ToList()
        });
    }

    public async Task<CrmDashboardDto?> DashboardAsync()
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        var response = await client.GetAsync(Constants.API.Crm.Dashboard);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<CrmDashboardDto>();
    }

    public async Task<PagedResponse<CrmReportRowDto>?> ReportAsync(
        ListQueryParameters query,
        string? stage,
        DateTime? from,
        DateTime? to)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        var path = ListQueryUrlBuilder.Build(
            Constants.API.Crm.Report,
            query,
            ("stage", stage),
            ("from", from?.ToString("yyyy-MM-dd")),
            ("to", to?.ToString("yyyy-MM-dd")));
        var response = await client.GetAsync(path);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PagedResponse<CrmReportRowDto>>();
    }

    public Task<HttpResponseMessage> CreateStatusAsync(string potentialId, CreatePotentialStatusDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync($"{Constants.API.Crm.Potentials}/{potentialId}/statuses", dto);
    }

    public Task<HttpResponseMessage> CreateDiscussionAsync(string potentialId, CreateCrmDiscussionDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync($"{Constants.API.Crm.Potentials}/{potentialId}/discussions", dto);
    }

    public Task<HttpResponseMessage> UploadAttachmentAsync(string potentialId, UploadCrmAttachmentDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync($"{Constants.API.Crm.Potentials}/{potentialId}/attachments", dto);
    }

    public Task<HttpResponseMessage> MoveAttachmentAsync(string potentialId, string attachmentId, string stageId)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PutAsJsonAsync(
            $"{Constants.API.Crm.Potentials}/{potentialId}/attachments/{attachmentId}/stage",
            new MoveCrmAttachmentStageDto { CrmStageId = stageId });
    }

    public Task<HttpResponseMessage> DeleteAttachmentAsync(string potentialId, string attachmentId)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.DeleteAsync($"{Constants.API.Crm.Potentials}/{potentialId}/attachments/{attachmentId}");
    }

    public async Task<CrmAttachmentContentDto?> DownloadAttachmentAsync(string potentialId, string attachmentId)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        var response = await client.GetAsync($"{Constants.API.Crm.Potentials}/{potentialId}/attachments/{attachmentId}");
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<CrmAttachmentContentDto>();
    }
}
