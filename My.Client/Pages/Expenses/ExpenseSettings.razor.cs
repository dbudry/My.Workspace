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
    private decimal mileageRatePerMile = ExpenseMileageRateRules.DefaultPerMile;
    private decimal lastSavedRate = ExpenseMileageRateRules.DefaultPerMile;
    private bool hasApprovalSignature;
    private HttpClient client = null!;
    private readonly SemaphoreSlim persistLock = new(1, 1);

    [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        client = ClientFactory.CreateClient(Constants.API.ClientName);
        try
        {
            var settings = await client.GetFromJsonAsync<ExpenseSettingsDto>(Constants.API.Expenses.Settings);
            if (settings != null)
            {
                mileageRatePerMile = settings.MileageRatePerMile;
                hasApprovalSignature = settings.HasApprovalSignature;
            }
            lastSavedRate = mileageRatePerMile;
            preferencesReady = true;
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't load expense settings.");
        }

        isLoading = false;
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
            if (mileageRatePerMile == lastSavedRate)
                return;

            var payload = new UpdateExpenseSettingsDto
            {
                MileageRatePerMile = mileageRatePerMile
            };
            var response = await client.PutAsJsonAsync(Constants.API.Expenses.Settings, payload);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Snackbar.Add(string.IsNullOrWhiteSpace(body) ? "Couldn't update the mileage rate." : body, Severity.Error);
                mileageRatePerMile = lastSavedRate;
                return;
            }

            lastSavedRate = mileageRatePerMile;
            Snackbar.Add("Mileage rate updated.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't update the mileage rate.");
            mileageRatePerMile = lastSavedRate;
        }
        finally
        {
            persistLock.Release();
        }
    }
}
