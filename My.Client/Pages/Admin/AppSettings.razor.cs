using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Net.Http.Json;
using My.Client.Extensions;
using My.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using My.Shared.Constants;
using My.Shared.Dtos;
using My.Shared.Dtos.Intranet;
using My.Shared.Rules;
using My.Shared;

namespace My.Client.Pages.Admin
{
    public partial class AppSettings
    {
        private bool isLoading = true;
        private bool isSaving;
        private bool allowUserDelete;
        private bool rateLimitEnabled;
        private int rateLimitAuthenticatedPerMinute = RateLimitRules.DefaultAuthenticatedPerMinute;
        private int rateLimitAnonymousPerMinute = RateLimitRules.DefaultAnonymousPerMinute;
        private int rateLimitUploadPerMinute = RateLimitRules.UploadPerMinute;
        private int rateLimitHeavyReadPerMinute = RateLimitRules.HeavyReadPerMinute;
        private int rateLimitProvisionPerMinute = RateLimitRules.ProvisionPerMinute;
        private int rateLimitInvalidBearerPerMinute = RateLimitRules.InvalidBearerPerMinute;
        private int rateLimitFetchExternalImagePerMinute = RateLimitRules.FetchExternalImagePerMinute;
        private bool allowOrganizationDelete;
        private bool allowProjectDelete;
        private int dataRetentionDays = 2555;
        private int submissionMonthInterval = 1;
        private bool allowManagerTimeCorrection = true;
        private string managerCorrectionMode = "Alias";
        private int calendarBackfillDefaultDays = 30;
        private bool calendarBackfillPromptUser = true;
        private double workdayHours = 8.0;
        private string teamAvailabilityCalendarId = string.Empty;
        private string intranetDriveParentFolderId = string.Empty;
        private List<IntranetUploadLimitDto> intranetUploadLimits = new();
        private string newUploadExtension = string.Empty;
        private int newUploadMaxMegabytes = 5;
        private int intranetNavigationMaxDepth = My.Shared.Rules.IntranetNavigationRules.DefaultMaxDepth;
        private List<string> contactTypes = ContactTypeRules.DefaultTypes.ToList();
        private List<string> loadedContactTypes = ContactTypeRules.DefaultTypes.ToList();
        private Dictionary<string, int> contactTypeUsage = new(StringComparer.OrdinalIgnoreCase);
        private string newContactType = string.Empty;
        private HttpClient client = null!;

        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

        [CascadingParameter(Name = "SetPageTitle")]
        private Action<string>? SetPageTitle { get; set; }

        [Inject]
        private NavigationManager Navigation { get; set; } = null!;

        [Inject]
        private IHttpClientFactory ClientFactory { get; set; } = null!;

        [Inject]
        private ISnackbar Snackbar { get; set; } = null!;

        [Inject]
        private AppSettingsCache AppSettingsCache { get; set; } = null!;

        [Inject]
        private IntranetMediaPolicyService MediaPolicy { get; set; } = null!;

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            if (authState.User.Identity is not { IsAuthenticated: true })
            {
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);
                return;
            }

            client = ClientFactory.CreateClient(Constants.API.ClientName);
            SetPageTitle?.Invoke("App Settings");

            try
            {
                var settings = await AppSettingsCache.GetAsync();
                {
                    var deleteVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.AllowUserDelete);
                    if (deleteVal != null && bool.TryParse(deleteVal.Value, out var parsed))
                        allowUserDelete = parsed;

                    var rateLimitVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.RateLimitEnabled);
                    if (rateLimitVal != null && bool.TryParse(rateLimitVal.Value, out var rateLimitParsed))
                        rateLimitEnabled = rateLimitParsed;

                    rateLimitAuthenticatedPerMinute = ReadRateLimitInt(
                        settings, Constants.SettingKeys.RateLimitAuthenticatedPerMinute, RateLimitRules.DefaultAuthenticatedPerMinute);
                    rateLimitAnonymousPerMinute = ReadRateLimitInt(
                        settings, Constants.SettingKeys.RateLimitAnonymousPerMinute, RateLimitRules.DefaultAnonymousPerMinute);
                    rateLimitUploadPerMinute = ReadRateLimitInt(
                        settings, Constants.SettingKeys.RateLimitUploadPerMinute, RateLimitRules.UploadPerMinute);
                    rateLimitHeavyReadPerMinute = ReadRateLimitInt(
                        settings, Constants.SettingKeys.RateLimitHeavyReadPerMinute, RateLimitRules.HeavyReadPerMinute);
                    rateLimitProvisionPerMinute = ReadRateLimitInt(
                        settings, Constants.SettingKeys.RateLimitProvisionPerMinute, RateLimitRules.ProvisionPerMinute);
                    rateLimitInvalidBearerPerMinute = ReadRateLimitInt(
                        settings, Constants.SettingKeys.RateLimitInvalidBearerPerMinute, RateLimitRules.InvalidBearerPerMinute);
                    rateLimitFetchExternalImagePerMinute = ReadRateLimitInt(
                        settings, Constants.SettingKeys.RateLimitFetchExternalImagePerMinute, RateLimitRules.FetchExternalImagePerMinute);

                    var retentionVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.DataRetentionDays);
                    if (retentionVal != null && int.TryParse(retentionVal.Value, out var days))
                        dataRetentionDays = days;

                    var orgDeleteVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.AllowOrganizationDelete);
                    if (orgDeleteVal != null && bool.TryParse(orgDeleteVal.Value, out var orgParsed))
                        allowOrganizationDelete = orgParsed;

                    var projDeleteVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.AllowProjectDelete);
                    if (projDeleteVal != null && bool.TryParse(projDeleteVal.Value, out var projParsed))
                        allowProjectDelete = projParsed;

                    var intervalVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.TymeSubmissionMonthInterval);
                    if (intervalVal != null && int.TryParse(intervalVal.Value, out var months) && months >= 1)
                        submissionMonthInterval = months;

                    var managerCorrectionVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.TymeAllowManagerTimeCorrection);
                    if (managerCorrectionVal != null && bool.TryParse(managerCorrectionVal.Value, out var managerCorrectionParsed))
                        allowManagerTimeCorrection = managerCorrectionParsed;

                    var modeVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.TymeManagerCorrectionMode);
                    if (modeVal != null && !string.IsNullOrWhiteSpace(modeVal.Value))
                        managerCorrectionMode = ManagerCorrectionRules.ParseMode(modeVal.Value) == ManagerCorrectionRules.CorrectionMode.Direct
                            ? "Direct"
                            : "Alias";

                    var backfillDaysVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.TymeCalendarBackfillDefaultDays);
                    if (backfillDaysVal != null && int.TryParse(backfillDaysVal.Value, out var days2) && days2 >= 0)
                        calendarBackfillDefaultDays = days2;

                    var promptVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.TymeCalendarBackfillPromptUser);
                    if (promptVal != null && bool.TryParse(promptVal.Value, out var promptParsed))
                        calendarBackfillPromptUser = promptParsed;

                    var workdayVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.WorkdayHours);
                    if (workdayVal != null && double.TryParse(workdayVal.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hours))
                        workdayHours = hours;

                    var teamCalVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.TeamAvailabilityCalendarId);
                    if (teamCalVal != null)
                        teamAvailabilityCalendarId = teamCalVal.Value ?? string.Empty;

                    var driveFolderVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.IntranetDriveParentFolderId);
                    if (driveFolderVal != null)
                        intranetDriveParentFolderId = driveFolderVal.Value ?? string.Empty;

                    var navDepthVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.IntranetNavigationMaxDepth);
                    if (navDepthVal != null)
                        intranetNavigationMaxDepth = IntranetNavigationRules.ParseMaxDepth(navDepthVal.Value);

                    var intranetPolicyVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.IntranetImageMaxMegabytesByExtension);
                    if (intranetPolicyVal != null && !string.IsNullOrWhiteSpace(intranetPolicyVal.Value))
                        intranetUploadLimits = IntranetMediaPolicyRules.ParseUploadLimits(intranetPolicyVal.Value);

                    var contactTypesVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.ContactTypes);
                    loadedContactTypes = ContactTypeRules.Parse(contactTypesVal?.Value).ToList();
                    contactTypes = loadedContactTypes.ToList();
                }

                try
                {
                    var usage = await client.GetFromJsonAsync<Dictionary<string, int>>(
                        Constants.API.AppSettings.ContactTypeUsage);
                    contactTypeUsage = usage ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    contactTypeUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load app settings.");
            }

            isLoading = false;
        }

        private async Task SaveSettings()
        {
            isSaving = true;
            try
            {
                var validationError = ContactTypeRules.ValidateSettingsUpdate(
                    loadedContactTypes,
                    contactTypes,
                    contactTypeUsage);
                if (validationError != null)
                {
                    Snackbar.Add(validationError, Severity.Warning);
                    isSaving = false;
                    return;
                }

                var correctionValidation = ManagerCorrectionRules.ValidateSettings(
                    allowManagerTimeCorrection,
                    ManagerCorrectionRules.ParseMode(managerCorrectionMode));
                if (!correctionValidation.IsAllowed)
                {
                    Snackbar.Add(correctionValidation.Reason!, Severity.Warning);
                    isSaving = false;
                    return;
                }

                var filePolicyError = IntranetMediaPolicyRules.ValidateAdminUploadLimits(intranetUploadLimits);
                if (filePolicyError != null)
                {
                    Snackbar.Add(filePolicyError, Severity.Warning);
                    isSaving = false;
                    return;
                }

                var payload = new List<AppSettingDto>
                {
                    new() { Key = Constants.SettingKeys.AllowUserDelete, Value = allowUserDelete.ToString().ToLower() },
                    new()
                    {
                        Key = Constants.SettingKeys.RateLimitEnabled,
                        Value = rateLimitEnabled.ToString().ToLower(),
                        Description = "When true, the API applies per-user and per-IP rate limits to blunt abuse."
                    },
                    new()
                    {
                        Key = Constants.SettingKeys.RateLimitAuthenticatedPerMinute,
                        Value = RateLimitSettings.ClampPermits(rateLimitAuthenticatedPerMinute).ToString(),
                        Description = "Max general API requests per minute per signed-in user."
                    },
                    new()
                    {
                        Key = Constants.SettingKeys.RateLimitAnonymousPerMinute,
                        Value = RateLimitSettings.ClampPermits(rateLimitAnonymousPerMinute).ToString(),
                        Description = "Max API requests per minute per IP when not authenticated."
                    },
                    new()
                    {
                        Key = Constants.SettingKeys.RateLimitUploadPerMinute,
                        Value = RateLimitSettings.ClampPermits(rateLimitUploadPerMinute).ToString(),
                        Description = "Max document upload POSTs per user per minute."
                    },
                    new()
                    {
                        Key = Constants.SettingKeys.RateLimitHeavyReadPerMinute,
                        Value = RateLimitSettings.ClampPermits(rateLimitHeavyReadPerMinute).ToString(),
                        Description = "Max heavy-read GETs (logs, data extraction) per user per minute."
                    },
                    new()
                    {
                        Key = Constants.SettingKeys.RateLimitProvisionPerMinute,
                        Value = RateLimitSettings.ClampPermits(rateLimitProvisionPerMinute).ToString(),
                        Description = "Max login provision POSTs per IP per minute."
                    },
                    new()
                    {
                        Key = Constants.SettingKeys.RateLimitInvalidBearerPerMinute,
                        Value = RateLimitSettings.ClampPermits(rateLimitInvalidBearerPerMinute).ToString(),
                        Description = "Max invalid Bearer attempts per IP per minute."
                    },
                    new()
                    {
                        Key = Constants.SettingKeys.RateLimitFetchExternalImagePerMinute,
                        Value = RateLimitSettings.ClampPermits(rateLimitFetchExternalImagePerMinute).ToString(),
                        Description = "Max external image fetch POSTs per user per minute."
                    },
                    new() { Key = Constants.SettingKeys.DataRetentionDays, Value = dataRetentionDays.ToString() },
                    new() { Key = Constants.SettingKeys.AllowOrganizationDelete, Value = allowOrganizationDelete.ToString().ToLower() },
                    new() { Key = Constants.SettingKeys.AllowProjectDelete, Value = allowProjectDelete.ToString().ToLower() },
                    new() { Key = Constants.SettingKeys.TymeSubmissionMonthInterval, Value = submissionMonthInterval.ToString() },
                    new() { Key = Constants.SettingKeys.TymeAllowManagerTimeCorrection, Value = allowManagerTimeCorrection.ToString().ToLower() },
                    new() { Key = Constants.SettingKeys.TymeManagerCorrectionMode, Value = managerCorrectionMode },
                    new() { Key = Constants.SettingKeys.TymeCalendarBackfillDefaultDays, Value = calendarBackfillDefaultDays.ToString() },
                    new() { Key = Constants.SettingKeys.TymeCalendarBackfillPromptUser, Value = calendarBackfillPromptUser.ToString().ToLower() },
                    new() { Key = Constants.SettingKeys.WorkdayHours, Value = workdayHours.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    new() { Key = Constants.SettingKeys.TeamAvailabilityCalendarId, Value = (teamAvailabilityCalendarId ?? string.Empty).Trim() },
                    new() { Key = Constants.SettingKeys.IntranetDriveParentFolderId, Value = (intranetDriveParentFolderId ?? string.Empty).Trim() },
                    new()
                    {
                        Key = Constants.SettingKeys.IntranetNavigationMaxDepth,
                        Value = intranetNavigationMaxDepth.ToString(),
                        Description = "Maximum nesting depth for curated intranet sidebar navigation. Top-level menu entries count as depth 1."
                    },
                    new()
                    {
                        Key = Constants.SettingKeys.IntranetImageMaxMegabytesByExtension,
                        Value = IntranetMediaPolicyRules.SerializeUploadLimits(intranetUploadLimits),
                        Description = "Allowed intranet editor file types and max upload sizes (JSON array)."
                    },
                    new() { Key = Constants.SettingKeys.ContactTypes, Value = ContactTypeRules.Serialize(contactTypes) }
                };

                var response = await client.PutAsJsonAsync(Constants.API.AppSettings.Update, payload);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(string.IsNullOrWhiteSpace(body) ? "Couldn't save app settings." : body, Severity.Error);
                    isSaving = false;
                    return;
                }

                loadedContactTypes = contactTypes.ToList();
                AppSettingsCache.Invalidate();
                MediaPolicy.Invalidate();
                Snackbar.Add("Settings saved.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save app settings.");
            }
            isSaving = false;
        }

        private static int ReadRateLimitInt(IEnumerable<AppSettingDto> settings, string key, int fallback)
        {
            var row = settings.FirstOrDefault(s => s.Key == key);
            if (row?.Value == null || !int.TryParse(row.Value, out var parsed))
                return fallback;
            return RateLimitSettings.ClampPermits(parsed);
        }

        private void AddContactType()
        {
            var trimmed = newContactType.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return;

            if (contactTypes.Any(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                Snackbar.Add("That contact type already exists.", Severity.Warning);
                return;
            }

            contactTypes.Add(trimmed);
            newContactType = string.Empty;
        }

        private void RemoveContactType(string type)
        {
            if (string.Equals(type, ContactTypeRules.Primary, StringComparison.OrdinalIgnoreCase))
            {
                Snackbar.Add("Primary is the default contact type and cannot be removed.", Severity.Warning);
                return;
            }

            if (contactTypes.Count <= 1)
            {
                Snackbar.Add("At least one contact type is required.", Severity.Warning);
                return;
            }

            var usageCount = ContactTypeRules.GetUsageCount(type, contactTypeUsage);
            if (usageCount > 0)
            {
                Snackbar.Add(
                    usageCount == 1
                        ? $"Cannot remove \"{type}\": 1 contact still uses this type. Change that contact's type first."
                        : $"Cannot remove \"{type}\": {usageCount} contacts still use this type. Change their types first.",
                    Severity.Warning);
                return;
            }

            contactTypes.RemoveAll(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
        }

        private bool CanRemoveContactType(string type) =>
            !string.Equals(type, ContactTypeRules.Primary, StringComparison.OrdinalIgnoreCase)
            && ContactTypeRules.GetUsageCount(type, contactTypeUsage) == 0
            && contactTypes.Count > 1;

        private static string GetContactTypeChipTooltip(string type, int inUse, bool removable)
        {
            if (string.Equals(type, ContactTypeRules.Primary, StringComparison.OrdinalIgnoreCase))
                return "Default type for new contacts — cannot be removed.";

            if (inUse > 0)
            {
                return inUse == 1
                    ? "1 contact uses this type — reassign it before removing."
                    : $"{inUse} contacts use this type — reassign them before removing.";
            }

            return removable ? "Remove this contact type." : "At least one contact type is required.";
        }

        private void OnContactTypeKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
                AddContactType();
        }

        private void AddIntranetUploadLimit()
        {
            var ext = newUploadExtension.Trim();
            while (ext.StartsWith('.'))
                ext = ext[1..];
            ext = ext.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext))
                return;

            if (!ext.All(static c => char.IsLetterOrDigit(c)))
            {
                Snackbar.Add("Extension must be letters and numbers only (e.g. png, pdf, docx).", Severity.Warning);
                return;
            }

            if (IntranetMediaPolicyRules.IsDeniedExtension(ext))
            {
                Snackbar.Add($"File type .{ext} cannot be allowed — executables and scripts are blocked.", Severity.Warning);
                return;
            }

            if (intranetUploadLimits.Any(e => string.Equals(e.Extension, ext, StringComparison.OrdinalIgnoreCase)))
            {
                Snackbar.Add($"Extension .{ext} is already in the list.", Severity.Warning);
                return;
            }

            var megabytes = Math.Clamp(newUploadMaxMegabytes, IntranetMediaPolicyRules.AbsoluteMinUploadMegabytes,
                IntranetMediaPolicyRules.AbsoluteMaxUploadMegabytes);

            intranetUploadLimits.Add(new IntranetUploadLimitDto
            {
                Extension = ext,
                MaxMegabytes = megabytes
            });
            intranetUploadLimits = intranetUploadLimits
                .OrderBy(e => e.Extension, StringComparer.OrdinalIgnoreCase)
                .ToList();

            newUploadExtension = string.Empty;
            newUploadMaxMegabytes = 5;
        }

        private void RemoveIntranetUploadLimit(string extension) =>
            intranetUploadLimits.RemoveAll(e => string.Equals(e.Extension, extension, StringComparison.OrdinalIgnoreCase));

        private void OnUploadLimitKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
                AddIntranetUploadLimit();
        }
    }
}
