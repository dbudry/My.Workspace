using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace My.Client.Components.Intranet;

public partial class FileDropZone : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Parameter] public string Title { get; set; } = "Upload a file";
    [Parameter] public string? Description { get; set; }
    [Parameter] public string? Accept { get; set; }
    [Parameter] public EventCallback<InputFileChangeEventArgs> OnFileSelected { get; set; }
    [Parameter] public EventCallback<FileDropPayload> OnFileReady { get; set; }

    private readonly string _dropZoneId = $"fdz-{Guid.NewGuid():N}";
    private readonly string _inputId = $"fdi-{Guid.NewGuid():N}";
    private DotNetObjectReference<FileDropZone>? _dotNetRef;
    private bool _initialized;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("fileDropZone.init", _dropZoneId, _inputId, _dotNetRef);
        }
        catch { /* non-fatal */ }
    }

    private async Task HandleInputChangeAsync(InputFileChangeEventArgs e)
    {
        await OnFileSelected.InvokeAsync(e);

        var file = e.File;
        if (file == null || !OnFileReady.HasDelegate) return;

        try
        {
            using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
            var buffer = new byte[stream.Length];
            var bytesRead = await stream.ReadAsync(buffer);
            await OnFileReady.InvokeAsync(new FileDropPayload
            {
                FileName = file.Name,
                ContentType = file.ContentType,
                Size = file.Size,
                Base64 = Convert.ToBase64String(buffer, 0, bytesRead)
            });
        }
        catch { /* parent OnFileSelected may handle errors */ }
    }

    [JSInvokable]
    public async Task HandlePastedFileAsync(string fileName, string contentType, string base64, long size)
    {
        if (!OnFileReady.HasDelegate) return;
        await OnFileReady.InvokeAsync(new FileDropPayload
        {
            FileName = fileName,
            ContentType = contentType,
            Size = size,
            Base64 = base64
        });
    }

    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();
        try { await JS.InvokeVoidAsync("fileDropZone.dispose", _dropZoneId); } catch { }
    }
}