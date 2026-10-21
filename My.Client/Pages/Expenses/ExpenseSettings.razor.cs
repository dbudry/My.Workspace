using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Client.Extensions;
using My.Shared.Constants;
using My.Shared.Dtos.Expenses;
using My.Shared.Rules;

namespace My.Client.Pages.Expenses;

public partial class ExpenseSettings
{
    private bool isLoading = true;
    private bool preferencesReady;
    private bool codesBusy;
    private decimal mileageRatePerMile = ExpenseMileageRateRules.DefaultPerMile;
    private decimal lastSavedRate = ExpenseMileageRateRules.DefaultPerMile;
    private bool creditBucketEnabled;
    private bool lastSavedCredit;
    private bool userEntersRates;
    private bool lastSavedUserEntersRates;
    private string companyCurrencyCode = ExpenseCurrencyRules.UsdCode;
    private string lastSavedCompanyCurrency = ExpenseCurrencyRules.UsdCode;
    private string creditEntryMode = ExpenseCreditEntryRules.FromLine;
    private string lastSavedCreditEntryMode = ExpenseCreditEntryRules.FromLine;
    private bool hasApprovalSignature;
    private string newCurrencyCode = "";
    private string newCurrencyName = "";
    private string newCurrencyColor = ExpenseCurrencyRules.DefaultEurColor;
    private decimal newCurrencyRate = 1m;
    private bool addingCurrency;
    private HttpClient client = null!;
    private readonly SemaphoreSlim persistLock = new(1, 1);
    private ExpenseSettingsDto settings = new();
    private List<ExpenseNotifyUserDto> directory = [];
    private string newCode = "";
    private string newLabel = "";
    private string? subcategoryFor;
    private string subcategoryDigits = "";
    private string subcategoryLabel = "";
    private ExpenseNotifyUserDto? notifyPick;
    private int notifyFieldKey;

    [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        client = ClientFactory.CreateClient(Constants.API.ClientName);
        try
        {
            var loaded = await client.GetFromJsonAsync<ExpenseSettingsDto>(Constants.API.Expenses.Settings);
            if (loaded != null)
                ApplySettings(loaded);
            lastSavedRate = mileageRatePerMile;
            lastSavedCredit = creditBucketEnabled;
            lastSavedUserEntersRates = userEntersRates;
            lastSavedCompanyCurrency = companyCurrencyCode;
            lastSavedCreditEntryMode = creditEntryMode;
            preferencesReady = true;
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't load expense settings.");
        }

        try
        {
            directory = await client.GetFromJsonAsync<List<ExpenseNotifyUserDto>>(Constants.API.Expenses.Directory) ?? [];
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't load people for the submission email.");
        }

        isLoading = false;
    }

    private void ApplySettings(ExpenseSettingsDto loaded)
    {
        settings = loaded;
        mileageRatePerMile = loaded.MileageRatePerMile;
        creditBucketEnabled = loaded.CreditBucketEnabled;
        userEntersRates = loaded.UserEntersRates;
        companyCurrencyCode = ExpenseCurrencyRules.NormalizeCode(
            string.IsNullOrWhiteSpace(loaded.CompanyCurrencyCode)
                ? ExpenseCurrencyRules.UsdCode
                : loaded.CompanyCurrencyCode);
        creditEntryMode = ExpenseCreditEntryRules.NormalizeMode(loaded.CreditEntryMode);
        hasApprovalSignature = loaded.HasApprovalSignature;
    }

    private string CompanyCurrencyLabel
    {
        get
        {
            if (ExpenseCurrencyRules.IsUsd(companyCurrencyCode))
                return "US dollars";
            var name = settings.Currencies.FirstOrDefault(currency =>
                string.Equals(currency.Code, companyCurrencyCode, StringComparison.Ordinal))?.Name;
            return string.IsNullOrWhiteSpace(name) ? companyCurrencyCode : name;
        }
    }

    private bool StructureLocked(ExpenseCurrencyDto row) =>
        row.IsLocked || ExpenseCurrencyRules.IsUsd(row.Code) || ExpenseCurrencyRules.IsHome(row.Code, companyCurrencyCode);

    private bool RateLocked(ExpenseCurrencyDto row) =>
        userEntersRates || ExpenseCurrencyRules.IsHome(row.Code, companyCurrencyCode);

    private Task OnCreditChangedAsync(bool value)
    {
        creditBucketEnabled = value;
        return PersistAsync();
    }

    private Task OnMileageChangedAsync(decimal value)
    {
        mileageRatePerMile = value;
        return PersistAsync();
    }

    private async Task PersistAsync()
    {
        if (!preferencesReady)
            return;
        if (mileageRatePerMile < ExpenseMileageRateRules.MinPerMile
            || mileageRatePerMile > ExpenseMileageRateRules.MaxPerMile)
        {
            Snackbar.Add(
                $"Mileage rate must be between {ExpenseMileageRateRules.MinPerMile} and {ExpenseMileageRateRules.MaxPerMile}.",
                Severity.Warning);
            return;
        }

        await persistLock.WaitAsync();
        try
        {
            if (mileageRatePerMile == lastSavedRate
                && creditBucketEnabled == lastSavedCredit)
                return;

            var payload = new UpdateExpenseSettingsDto
            {
                MileageRatePerMile = mileageRatePerMile,
                CreditBucketEnabled = creditBucketEnabled,
                UserEntersRates = userEntersRates,
                CompanyCurrencyCode = companyCurrencyCode,
                CreditEntryMode = creditEntryMode
            };
            var response = await client.PutAsJsonAsync(Constants.API.Expenses.Settings, payload);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Snackbar.Add(string.IsNullOrWhiteSpace(body) ? "Couldn't update expense settings." : body, Severity.Error);
                RevertPolicy();
                return;
            }

            var updated = await response.Content.ReadFromJsonAsync<ExpenseSettingsDto>();
            if (updated != null)
                ApplySettings(updated);
            lastSavedRate = mileageRatePerMile;
            lastSavedCredit = creditBucketEnabled;
            lastSavedUserEntersRates = userEntersRates;
            lastSavedCompanyCurrency = companyCurrencyCode;
            lastSavedCreditEntryMode = creditEntryMode;
            Snackbar.Add("Expense settings updated.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't update expense settings.");
            RevertPolicy();
        }
        finally
        {
            persistLock.Release();
        }
    }

    private void RevertPolicy()
    {
        mileageRatePerMile = lastSavedRate;
        creditBucketEnabled = lastSavedCredit;
        userEntersRates = lastSavedUserEntersRates;
        companyCurrencyCode = lastSavedCompanyCurrency;
        creditEntryMode = lastSavedCreditEntryMode;
    }

    private static bool IsSoftwareCode(ExpenseCodeDto row) =>
        string.Equals(row.Code, ExpenseForm8743Rules.MiscSoftwareCode, StringComparison.OrdinalIgnoreCase);

    private async Task AddCodeAsync()
    {
        var code = ExpenseCategoryRules.NormalizeCode(newCode);
        var label = newLabel.Trim();
        if (code is not { Length: 1 } || !ExpenseCodeFormatRules.IsValid(code))
        {
            Snackbar.Add("Add a letter A–Z here. Use Add subcategory on that letter for A.00–A.99.", Severity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            Snackbar.Add("Enter a label for the code.", Severity.Warning);
            return;
        }

        codesBusy = true;
        try
        {
            var response = await client.PostAsJsonAsync(Constants.API.Expenses.Codes, new UpsertExpenseCodeDto
            {
                Code = code,
                Label = label,
                IsActive = true
            });
            if (!await ApplyCodeResponseAsync(response, "Couldn't add that code."))
                return;
            newCode = "";
            newLabel = "";
            Snackbar.Add("Miscellaneous code added.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't add that code.");
        }
        finally
        {
            codesBusy = false;
        }
    }

    private async Task SaveCodeAsync(ExpenseCodeDto row)
    {
        var code = ExpenseCategoryRules.NormalizeCode(row.Code);
        var label = row.Label.Trim();
        if (!ExpenseCodeFormatRules.IsValid(code))
        {
            Snackbar.Add("A miscellaneous code is A–Z or A.00–A.99.", Severity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            Snackbar.Add("Enter a label for the code.", Severity.Warning);
            return;
        }

        codesBusy = true;
        try
        {
            var response = await client.PutAsJsonAsync(Constants.API.Expenses.CodeById(row.ExpenseCodeId), new UpsertExpenseCodeDto
            {
                Code = code!,
                Label = label,
                IsActive = IsSoftwareCode(row) || row.IsActive
            });
            if (!await ApplyCodeResponseAsync(response, "Couldn't update that code."))
                return;
            Snackbar.Add("Miscellaneous code updated.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't update that code.");
        }
        finally
        {
            codesBusy = false;
        }
    }

    private async Task RemoveCodeAsync(ExpenseCodeDto row)
    {
        if (IsSoftwareCode(row) || codesBusy)
            return;

        var code = ExpenseCategoryRules.NormalizeCode(row.Code) ?? row.Code;
        var isLetter = code.Length == 1;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            $"Remove {code}?",
            isLetter
                ? $"{code} and its subcategories will be removed. A code that is already on an expense line stays on that line and is hidden from new ones."
                : $"{code} will be removed. If an expense line already uses it, that line keeps {code} and the code is hidden from new lines.",
            yesText: "Remove",
            cancelText: "Cancel");
        if (confirmed != true)
            return;

        codesBusy = true;
        try
        {
            var response = await client.DeleteAsync(Constants.API.Expenses.CodeById(row.ExpenseCodeId));
            if (!await ApplyCodeResponseAsync(response, "Couldn't remove that code."))
                return;
            if (string.Equals(subcategoryFor, code, StringComparison.OrdinalIgnoreCase)
                || (code.Length == 1 && subcategoryFor != null && subcategoryFor.StartsWith(code, StringComparison.OrdinalIgnoreCase)))
                subcategoryFor = null;
            Snackbar.Add("Code removed.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't remove that code.");
        }
        finally
        {
            codesBusy = false;
        }
    }

    private async Task<bool> ApplyCodeResponseAsync(HttpResponseMessage response, string fallback)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Snackbar.Add(string.IsNullOrWhiteSpace(body) ? fallback : body, Severity.Error);
            await ReloadSettingsAsync();
            return false;
        }

        var loaded = await response.Content.ReadFromJsonAsync<ExpenseSettingsDto>();
        if (loaded != null)
            ApplySettings(loaded);
        return true;
    }

    private async Task ReloadSettingsAsync()
    {
        try
        {
            var loaded = await client.GetFromJsonAsync<ExpenseSettingsDto>(Constants.API.Expenses.Settings);
            if (loaded != null)
                ApplySettings(loaded);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't reload expense settings.");
        }
    }

    private IReadOnlyList<string> MiscLetterKeys =>
        settings.MiscellaneousCodes
            .Select(LetterOf)
            .Where(letter => letter != null)
            .Select(letter => letter!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(letter => letter, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? LetterOf(ExpenseCodeDto row)
    {
        var code = ExpenseCategoryRules.NormalizeCode(row.Code);
        if (string.IsNullOrEmpty(code))
            return null;
        if (code.Length == 1 || (code.Length == 4 && code[1] == '.'))
            return code[..1];
        return null;
    }

    private ExpenseCodeDto? LetterRow(string letter) =>
        settings.MiscellaneousCodes.FirstOrDefault(row =>
            string.Equals(ExpenseCategoryRules.NormalizeCode(row.Code), letter, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<ExpenseCodeDto> ChildRows(string letter) =>
        settings.MiscellaneousCodes
            .Where(row =>
            {
                var code = ExpenseCategoryRules.NormalizeCode(row.Code);
                return code != null
                    && code.Length == 4
                    && code.StartsWith(letter + ".", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(row => row.Code, StringComparer.OrdinalIgnoreCase);

    private void BeginSubcategory(string letter)
    {
        subcategoryFor = letter;
        subcategoryDigits = "";
        subcategoryLabel = "";
    }

    private void CancelSubcategory() => subcategoryFor = null;

    private async Task AddSubcategoryAsync()
    {
        var letter = ExpenseCategoryRules.NormalizeCode(subcategoryFor);
        var digits = subcategoryDigits.Trim();
        if (digits.Length == 1 && char.IsDigit(digits[0]))
            digits = "0" + digits;
        var code = letter == null ? "" : letter + "." + digits;
        var label = subcategoryLabel.Trim();
        if (!ExpenseCodeFormatRules.IsValid(code) || code.Length != 4)
        {
            Snackbar.Add("A subcategory is two digits, 00–99.", Severity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            Snackbar.Add("Enter a label for the subcategory.", Severity.Warning);
            return;
        }

        codesBusy = true;
        try
        {
            var response = await client.PostAsJsonAsync(Constants.API.Expenses.Codes, new UpsertExpenseCodeDto
            {
                Code = code,
                Label = label,
                IsActive = true
            });
            if (!await ApplyCodeResponseAsync(response, "Couldn't add that subcategory."))
                return;
            subcategoryDigits = "";
            subcategoryLabel = "";
            subcategoryFor = null;
            Snackbar.Add("Subcategory added.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't add that subcategory.");
        }
        finally
        {
            codesBusy = false;
        }
    }

    private Task<IEnumerable<ExpenseNotifyUserDto>> SearchDirectory(string? text, CancellationToken _)
    {
        var selected = settings.NotifyUsers.Select(user => user.UserId).ToHashSet(StringComparer.Ordinal);
        IEnumerable<ExpenseNotifyUserDto> options = directory.Where(user => !selected.Contains(user.UserId));
        if (!string.IsNullOrWhiteSpace(text))
        {
            var term = text.Trim();
            options = options.Where(user =>
                user.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || user.Email.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(options.Take(30));
    }

    private static string NotifyText(ExpenseNotifyUserDto? user) =>
        user == null ? "" : $"{user.Name} ({user.Email})";

    private async Task AddNotifyAsync(ExpenseNotifyUserDto? user)
    {
        if (user == null || settings.NotifyUsers.Any(existing => existing.UserId == user.UserId))
            return;
        notifyPick = null;
        notifyFieldKey++;
        var ids = settings.NotifyUsers.Select(existing => existing.UserId).Append(user.UserId).ToList();
        await SaveNotifyAsync(ids, "Added to the submission email.");
    }

    private Task RemoveNotifyAsync(ExpenseNotifyUserDto user)
    {
        var ids = settings.NotifyUsers
            .Where(existing => existing.UserId != user.UserId)
            .Select(existing => existing.UserId)
            .ToList();
        return SaveNotifyAsync(ids, "Removed from the submission email.");
    }

    private async Task SaveNotifyAsync(List<string> userIds, string success)
    {
        codesBusy = true;
        try
        {
            var response = await client.PutAsJsonAsync(
                Constants.API.Expenses.NotifyUsers,
                new UpdateExpenseNotifyUsersDto { UserIds = userIds });
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Snackbar.Add(string.IsNullOrWhiteSpace(body) ? "Couldn't update the submission email." : body, Severity.Error);
                return;
            }

            var loaded = await response.Content.ReadFromJsonAsync<ExpenseSettingsDto>();
            if (loaded != null)
                ApplySettings(loaded);
            Snackbar.Add(success, Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't update the submission email.");
        }
        finally
        {
            codesBusy = false;
        }
    }

    private void BeginAddCurrency() => addingCurrency = true;

    private void CancelAddCurrency()
    {
        addingCurrency = false;
        ResetNewCurrency();
    }

    private void ResetNewCurrency()
    {
        newCurrencyCode = "";
        newCurrencyName = "";
        newCurrencyColor = ExpenseCurrencyRules.DefaultEurColor;
        newCurrencyRate = 1m;
    }

    private async Task AddCurrencyAsync()
    {
        var code = ExpenseCurrencyRules.NormalizeCode(newCurrencyCode);
        if (!ExpenseCurrencyRules.IsCodeShape(code) || ExpenseCurrencyRules.IsUsd(code))
        {
            Snackbar.Add("Add a currency code of 3 to 8 letters. US dollars are already on the list.", Severity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(newCurrencyName))
        {
            Snackbar.Add("Enter a name for the currency.", Severity.Warning);
            return;
        }

        codesBusy = true;
        try
        {
            var response = await client.PostAsJsonAsync(Constants.API.Expenses.Currencies, new UpsertExpenseCurrencyDto
            {
                Code = code,
                Name = newCurrencyName.Trim(),
                Color = newCurrencyColor,
                RateToUsd = newCurrencyRate,
                IsActive = true
            });
            if (!await ApplyCodeResponseAsync(response, "Couldn't add that currency."))
                return;
            ResetNewCurrency();
            addingCurrency = false;
            Snackbar.Add("Currency added.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't add that currency.");
        }
        finally
        {
            codesBusy = false;
        }
    }

    private async Task SaveCurrencyAsync(ExpenseCurrencyDto row)
    {
        codesBusy = true;
        try
        {
            var response = await client.PutAsJsonAsync(
                Constants.API.Expenses.CurrencyById(row.ExpenseCurrencyId),
                new UpsertExpenseCurrencyDto
                {
                    Code = row.Code,
                    Name = row.Name,
                    Color = row.Color,
                    RateToUsd = ExpenseCurrencyRules.IsHome(row.Code, companyCurrencyCode) ? 1m : row.RateToUsd,
                    IsActive = StructureLocked(row) || row.IsActive
                });
            if (!await ApplyCodeResponseAsync(response, "Couldn't update that currency."))
                return;
            Snackbar.Add(
                userEntersRates
                    ? "Currency updated."
                    : "Currency updated. Saved lines keep the rate they were saved with.",
                Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't update that currency.");
        }
        finally
        {
            codesBusy = false;
        }
    }

    private async Task RemoveCurrencyAsync(ExpenseCurrencyDto row)
    {
        if (StructureLocked(row))
            return;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Remove currency?",
            $"Remove {row.Code}? A currency already used on a line is retired instead of deleted.",
            yesText: "Remove",
            cancelText: "Cancel");
        if (confirmed != true)
            return;

        codesBusy = true;
        try
        {
            var response = await client.DeleteAsync(Constants.API.Expenses.CurrencyById(row.ExpenseCurrencyId));
            if (!await ApplyCodeResponseAsync(response, "Couldn't remove that currency."))
                return;
            Snackbar.Add("Currency removed.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't remove that currency.");
        }
        finally
        {
            codesBusy = false;
        }
    }
}
