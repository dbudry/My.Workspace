using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Net.Http.Json;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Components.Organizations;
using My.Client.Services;
using My.Shared.Dtos.Organization;
using My.Shared.Dtos.Department;
using My.Shared.Dtos.Paging;
using My.Shared.Constants;
using My.Shared.Dtos;
using My.Shared.Helpers;

namespace My.Client.Pages.Tyme
{
    public partial class OrganizationManager
    {
        List<Organization> organizationsList { get; set; } = new();

        MudTable<OrgDisplayRow> table = null!;

        bool allowOrgDelete = false;
        bool sortAscending = true;
        bool canManage = false;

        HttpClient client = null!;

        string searchString = "";

        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

        [CascadingParameter(Name = "SetPageTitle")]
        private Action<string>? SetPageTitle { get; set; }

        #region Dependency Injection

        [Inject]
        protected NavigationManager Navigation { get; set; } = null!;

        [Inject]
        private IHttpClientFactory ClientFactory { get; set; } = null!;

        [Inject]
        private ISnackbar Snackbar { get; set; } = null!;

        [Inject]
        private IDialogService DialogService { get; set; } = null!;

        [Inject]
        private OrganizationsCache OrganizationsCache { get; set; } = null!;

        [Inject]
        private ProjectsCache ProjectsCache { get; set; } = null!;

        [Inject]
        private AppSettingsCache AppSettingsCache { get; set; } = null!;

        #endregion

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;

            if (user.Identity != null && !user.Identity.IsAuthenticated)
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);

            canManage = user.IsInRole(Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Tyme))
                     || user.IsInRole(Constants.Roles.Scoped(Constants.Roles.Admin, Constants.Scopes.Tyme));

            client = ClientFactory.CreateClient(Constants.API.ClientName);

            SetPageTitle?.Invoke(canManage ? "Manage Organizations" : "Organizations");

            await LoadAppSettings();
        }

        private async Task LoadAppSettings()
        {
            try
            {
                var settings = await AppSettingsCache.GetAsync();
                var val = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.AllowOrganizationDelete);
                if (val != null && bool.TryParse(val.Value, out var parsed))
                    allowOrgDelete = parsed;
            }
            catch { }
        }

        private async Task<TableData<OrgDisplayRow>> LoadServerData(TableState state, CancellationToken cancellationToken)
        {
            try
            {
                var query = new ListQueryParameters
                {
                    PageNumber = state.Page + 1,
                    PageSize = state.PageSize,
                    Search = searchString,
                    SortBy = "Name",
                    SortDescending = !sortAscending,
                    IncludeArchived = showArchived,
                    IncludeInactive = showInactive
                };

                var url = ListQueryUrlBuilder.Build(Constants.API.Organization.Get, query);
                var response = await client.GetFromJsonAsync<PagedResponse<OrganizationDto>>(url, cancellationToken);

                organizationsList = response?.Items.Select(d => new Organization(d)).ToList() ?? new List<Organization>();
                var rows = BuildDisplayRows(organizationsList);

                return new TableData<OrgDisplayRow>
                {
                    Items = rows,
                    TotalItems = response?.TotalCount ?? 0
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // MudTable cancels the previous in-flight request when paging/search/sort changes.
                return new TableData<OrgDisplayRow> { Items = Array.Empty<OrgDisplayRow>(), TotalItems = 0 };
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load organizations.");
                return new TableData<OrgDisplayRow> { Items = Array.Empty<OrgDisplayRow>(), TotalItems = 0 };
            }
        }

        private async Task<Organization?> FetchOrganizationDetailsAsync(string organizationId)
        {
            var dto = await client.GetFromJsonAsync<OrganizationDto>(
                $"{Constants.API.Organization.GetById}{organizationId}?includeArchived=true");
            return dto == null ? null : new Organization(dto);
        }

        private void InvalidateAfterOrgMutation()
        {
            OrganizationsCache.Invalidate();
            ProjectsCache.Invalidate();
        }

        private async Task ReloadTableAsync()
        {
            if (table != null)
                await table.ReloadServerData();
        }

        private static List<OrgDisplayRow> BuildDisplayRows(IEnumerable<Organization> orgs)
        {
            var rows = new List<OrgDisplayRow>();
            foreach (var org in orgs)
            {
                rows.Add(new OrgDisplayRow(org));
                if (org.Departments != null)
                {
                    foreach (var dept in org.Departments)
                        rows.Add(new OrgDisplayRow(org, dept));
                }
            }
            return rows;
        }

        bool showArchived = false;
        bool showInactive = false;

        private async Task OnShowArchivedChanged(bool value)
        {
            showArchived = value;
            await ReloadTableAsync();
        }

        private async Task OnShowInactiveChanged(bool value)
        {
            showInactive = value;
            await ReloadTableAsync();
        }

        private async Task ToggleSortDirection()
        {
            sortAscending = !sortAscending;
            await ReloadTableAsync();
        }

        private async Task OnSearchChanged(string value)
        {
            searchString = value;
            await ReloadTableAsync();
        }

        private string GetEmptyMessage()
        {
            if (!string.IsNullOrWhiteSpace(searchString))
                return "No organizations match your search.";
            if (showArchived)
                return "No archived organizations match.";
            return "Add an organization to get started.";
        }

        #region Organization CRUD

        private async Task ViewOrganization(Organization org)
        {
            try
            {
                var detailed = await FetchOrganizationDetailsAsync(org.OrganizationId) ?? org;
                var parameters = new DialogParameters<ViewOrganizationDialog>
                {
                    { x => x.Model, detailed }
                };

                await DialogService.ShowAsync<ViewOrganizationDialog>(detailed.Name, parameters,
                    new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true, CloseButton = true });
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load organization details.");
            }
        }

        private async Task AddOrganization()
        {
            var model = new Organization();
            var parameters = new DialogParameters<OrganizationDialog>
            {
                { x => x.Model, model },
                { x => x.SubmitLabel, "Create" }
            };

            var dialog = await DialogService.ShowAsync<OrganizationDialog>("New Organization", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;

            if (result == null || result.Canceled)
                return;

            var org = (Organization)result.Data!;

            try
            {
                var dto = new CreateOrganizationDto
                {
                    Name = org.Name,
                    Address = org.Address,
                    City = org.City,
                    State = org.State,
                    PostalCode = org.PostalCode,
                    Country = org.Country,
                    Note = org.Note,
                    Color = org.Color
                };

                var response = await client.PostAsJsonAsync(Constants.API.Organization.Create, dto);
                response.EnsureSuccessStatusCode();

                Snackbar.Add("Organization created.", Severity.Success);
                InvalidateAfterOrgMutation();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't create organization.");
            }
        }

        private async Task EditOrganization(Organization org)
        {
            Organization source;
            try
            {
                source = await FetchOrganizationDetailsAsync(org.OrganizationId) ?? org;
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load organization details.");
                return;
            }

            var model = new Organization
            {
                OrganizationId = source.OrganizationId,
                Name = source.Name,
                Address = source.Address,
                City = source.City,
                State = source.State,
                PostalCode = source.PostalCode,
                Country = source.Country,
                Note = source.Note,
                Color = source.Color,
                Contacts = source.Contacts?.Select(c => new ContactModel
                {
                    ContactId = c.ContactId,
                    Name = c.Name,
                    ContactType = c.ContactType,
                    Title = c.Title,
                    PhoneNumber = c.PhoneNumber,
                    Email = c.Email,
                    OrganizationId = c.OrganizationId,
                    DepartmentId = c.DepartmentId
                }).ToList()
            };

            var parameters = new DialogParameters<OrganizationDialog>
            {
                { x => x.Model, model },
                { x => x.SubmitLabel, "Save" }
            };

            var dialog = await DialogService.ShowAsync<OrganizationDialog>("Edit Organization", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
            var result = await dialog.Result;

            if (result == null || result.Canceled)
            {
                OrganizationsCache.Invalidate();
                await ReloadTableAsync();
                return;
            }

            var edited = (Organization)result.Data!;

            try
            {
                var dto = new UpdateOrganizationDto
                {
                    OrganizationId = org.OrganizationId,
                    Name = edited.Name,
                    Address = edited.Address,
                    City = edited.City,
                    State = edited.State,
                    PostalCode = edited.PostalCode,
                    Country = edited.Country,
                    Note = edited.Note,
                    Color = edited.Color
                };

                var response = await client.PutAsJsonAsync(Constants.API.Organization.Update, dto);
                response.EnsureSuccessStatusCode();

                Snackbar.Add("Organization updated.", Severity.Success);
                InvalidateAfterOrgMutation();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't update organization.");
            }
        }

        private async Task ToggleActive(Organization org)
        {
            var action = org.IsActive ? "deactivate" : "activate";
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm", $"Are you sure you want to {action} \"{org.Name}\"?",
                yesText: "Yes", cancelText: "Cancel");

            if (confirmed != true) return;

            try
            {
                var response = await client.PostAsync($"{Constants.API.Organization.SetActive}/{org.OrganizationId}/setactive", null);
                response.EnsureSuccessStatusCode();
                Snackbar.Add($"Organization {action}d.", Severity.Success);
                InvalidateAfterOrgMutation();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, $"Couldn't {action} organization.");
            }
        }

        private async Task ArchiveOrganization(Organization org)
        {
            var action = org.IsArchived ? "unarchive" : "archive";
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm", $"Are you sure you want to {action} \"{org.Name}\"?",
                yesText: "Yes", cancelText: "Cancel");

            if (confirmed != true) return;

            try
            {
                var response = await client.PostAsync($"{Constants.API.Organization.Archive}/{org.OrganizationId}/archive", null);
                response.EnsureSuccessStatusCode();
                Snackbar.Add($"Organization {action}d.", Severity.Success);
                InvalidateAfterOrgMutation();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, $"Couldn't {action} organization.");
            }
        }

        private async Task DeleteOrganization(Organization org)
        {
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm Delete", $"Are you sure you want to permanently delete \"{org.Name}\"? This cannot be undone.",
                yesText: "Delete", cancelText: "Cancel");

            if (confirmed != true) return;

            try
            {
                var response = await client.DeleteAsync($"{Constants.API.Organization.Delete}/{org.OrganizationId}");
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Organization deleted.", Severity.Success);
                    InvalidateAfterOrgMutation();
                    await ReloadTableAsync();
                }
                else
                {
                    var msg = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(msg, Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete organization.");
            }
        }

        #endregion

        #region Department CRUD

        private async Task ViewDepartment(Organization org, DepartmentModel dept)
        {
            var parameters = new DialogParameters<ViewDepartmentDialog>
            {
                { x => x.Model, dept }
            };

            await DialogService.ShowAsync<ViewDepartmentDialog>(dept.Name, parameters,
                new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true, CloseButton = true });
        }

        private async Task AddDepartment(Organization org)
        {
            var model = new DepartmentModel { OrganizationId = org.OrganizationId };
            var parameters = new DialogParameters<DepartmentDialog>
            {
                { x => x.Model, model },
                { x => x.SubmitLabel, "Create" }
            };

            var dialog = await DialogService.ShowAsync<DepartmentDialog>($"New Department for {org.Name}", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;

            if (result == null || result.Canceled)
                return;

            var dept = (DepartmentModel)result.Data!;

            try
            {
                var dto = new CreateDepartmentDto
                {
                    Name = dept.Name,
                    OrganizationId = org.OrganizationId
                };

                var response = await client.PostAsJsonAsync(Constants.API.Department.Create, dto);
                response.EnsureSuccessStatusCode();

                Snackbar.Add("Department created.", Severity.Success);
                InvalidateAfterOrgMutation();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't create department.");
            }
        }

        private async Task EditDepartment(Organization org, DepartmentModel dept)
        {
            var model = new DepartmentModel
            {
                DepartmentId = dept.DepartmentId,
                Name = dept.Name,
                OrganizationId = dept.OrganizationId,
                Contacts = dept.Contacts?.Select(c => new ContactModel
                {
                    ContactId = c.ContactId,
                    Name = c.Name,
                    ContactType = c.ContactType,
                    Title = c.Title,
                    PhoneNumber = c.PhoneNumber,
                    Email = c.Email,
                    OrganizationId = c.OrganizationId,
                    DepartmentId = c.DepartmentId
                }).ToList()
            };

            var parameters = new DialogParameters<DepartmentDialog>
            {
                { x => x.Model, model },
                { x => x.SubmitLabel, "Save" }
            };

            var dialog = await DialogService.ShowAsync<DepartmentDialog>($"Edit Department - {org.Name}", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
            var result = await dialog.Result;

            if (result == null || result.Canceled)
            {
                OrganizationsCache.Invalidate();
                await ReloadTableAsync();
                return;
            }

            var edited = (DepartmentModel)result.Data!;

            try
            {
                var dto = new UpdateDepartmentDto
                {
                    DepartmentId = dept.DepartmentId,
                    Name = edited.Name,
                    OrganizationId = dept.OrganizationId
                };

                var response = await client.PutAsJsonAsync(Constants.API.Department.Update, dto);
                response.EnsureSuccessStatusCode();

                Snackbar.Add("Department updated.", Severity.Success);
                InvalidateAfterOrgMutation();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't update department.");
            }
        }

        private async Task ToggleDepartmentActive(DepartmentModel dept)
        {
            var action = dept.IsActive ? "deactivate" : "activate";
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm", $"Are you sure you want to {action} \"{dept.Name}\"?",
                yesText: "Yes", cancelText: "Cancel");

            if (confirmed != true) return;

            try
            {
                var response = await client.PostAsync($"{Constants.API.Department.SetActive}/{dept.DepartmentId}/setactive", null);
                response.EnsureSuccessStatusCode();
                Snackbar.Add($"Department {action}d.", Severity.Success);
                InvalidateAfterOrgMutation();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, $"Couldn't {action} department.");
            }
        }

        private async Task ArchiveDepartment(DepartmentModel dept)
        {
            var action = dept.IsArchived ? "unarchive" : "archive";
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm", $"Are you sure you want to {action} \"{dept.Name}\"?",
                yesText: "Yes", cancelText: "Cancel");

            if (confirmed != true) return;

            try
            {
                var response = await client.PostAsync($"{Constants.API.Department.Archive}/{dept.DepartmentId}/archive", null);
                response.EnsureSuccessStatusCode();
                Snackbar.Add($"Department {action}d.", Severity.Success);
                InvalidateAfterOrgMutation();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, $"Couldn't {action} department.");
            }
        }

        private async Task DeleteDepartment(DepartmentModel dept)
        {
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm Delete", $"Are you sure you want to delete department \"{dept.Name}\"?",
                yesText: "Delete", cancelText: "Cancel");

            if (confirmed != true) return;

            try
            {
                var response = await client.DeleteAsync($"{Constants.API.Department.Delete}/{dept.DepartmentId}");
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Department deleted.", Severity.Success);
                    InvalidateAfterOrgMutation();
                    await ReloadTableAsync();
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete department.");
            }
        }

        #endregion
    }

    public class OrgDisplayRow
    {
        public Organization? Organization { get; }
        public DepartmentModel? Department { get; }
        public bool IsOrgRow => Department == null;
        public string SortKey => Organization?.Name ?? "";

        public OrgDisplayRow(Organization org)
        {
            Organization = org;
        }

        public OrgDisplayRow(Organization org, DepartmentModel dept)
        {
            Organization = org;
            Department = dept;
        }
    }
}