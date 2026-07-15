namespace My.Client.Services;

/// <summary>
/// Shared open/close state for the app-bar intranet search dialog.
/// The trigger (HeaderSearch) and panel (HeaderSearchDialog) live in different
/// parts of the layout tree; this scoped service bridges them.
/// </summary>
public class HeaderSearchUiState
{
    public bool IsOpen { get; private set; }

    public event Action? StateChanged;

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        StateChanged?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        StateChanged?.Invoke();
    }
}