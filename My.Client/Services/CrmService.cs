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

    public async Task<PagedResponse<OpportunityListItemDto>?> ListAsync(
        ListQueryParameters query,
        string? stage = null,
        string? organizationId = null)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        var path = ListQueryUrlBuilder.Build(
            Constants.API.Crm.Opportunities,
            query,
            ("stage", stage),
            ("organizationId", organizationId));

        var response = await client.GetAsync(path);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<PagedResponse<OpportunityListItemDto>>();
    }

    public async Task<OpportunityDetailDto?> GetAsync(string opportunityId)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        var response = await client.GetAsync($"{Constants.API.Crm.Opportunities}/{opportunityId}");
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<OpportunityDetailDto>();
    }

    public Task<HttpResponseMessage> CreateAsync(CreateOpportunityDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync(Constants.API.Crm.Opportunities, dto);
    }

    public Task<HttpResponseMessage> UpdateAsync(UpdateOpportunityDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PutAsJsonAsync(Constants.API.Crm.Opportunities, dto);
    }

    public Task<HttpResponseMessage> ArchiveAsync(string opportunityId, bool isArchived)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync(
            $"{Constants.API.Crm.Opportunities}/{opportunityId}/archive",
            new SetOpportunityArchivedDto { IsArchived = isArchived });
    }

    public Task<HttpResponseMessage> DeleteAsync(string opportunityId)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.DeleteAsync($"{Constants.API.Crm.Opportunities}/{opportunityId}");
    }

    public Task<HttpResponseMessage> CreateContactAsync(CreateContactDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync(Constants.API.Crm.Contacts, dto);
    }

    public Task<HttpResponseMessage> CreateActivityAsync(CreateCrmActivityDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PostAsJsonAsync(Constants.API.Crm.Activities, dto);
    }

    public Task<HttpResponseMessage> UpdateActivityAsync(UpdateCrmActivityDto dto)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.PutAsJsonAsync(Constants.API.Crm.Activities, dto);
    }

    public Task<HttpResponseMessage> DeleteActivityAsync(string activityId)
    {
        var client = clientFactory.CreateClient(Constants.API.ClientName);
        return client.DeleteAsync($"{Constants.API.Crm.Activities}/{activityId}");
    }
}
