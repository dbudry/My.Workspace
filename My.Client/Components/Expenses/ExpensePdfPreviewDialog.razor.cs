using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace My.Client.Components.Expenses;

public partial class ExpensePdfPreviewDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter] public string FileName { get; set; } = "Expenses.pdf";
    [Parameter] public string Base64 { get; set; } = "";

    private string? objectUrl;
    private bool ready;
    private bool started;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || started)
            return;
        started = true;
        if (!string.IsNullOrEmpty(Base64))
        {
            try
            {
                objectUrl = await Js.InvokeAsync<string>("fileSave.objectUrl", Base64, "application/pdf");
            }
            catch (Exception)
            {
                objectUrl = null;
            }
        }

        ready = true;
        StateHasChanged();
    }

    private async Task DownloadAsync()
    {
        try
        {
            await Js.InvokeVoidAsync("fileSave.download", Base64, "application/pdf", FileName);
        }
        catch (Exception)
        {
            Snackbar.Add("Couldn't download the PDF.", Severity.Error);
        }
    }

    private void Close() => MudDialog.Close();

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrEmpty(objectUrl))
            return;
        try
        {
            await Js.InvokeVoidAsync("fileSave.revokeObjectUrl", objectUrl);
        }
        catch (Exception)
        {
            // The circuit is already gone when the dialog closes during navigation.
        }

        objectUrl = null;
    }
}
