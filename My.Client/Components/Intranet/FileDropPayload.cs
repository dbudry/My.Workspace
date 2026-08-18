namespace My.Client.Components.Intranet;

public sealed class FileDropPayload
{
    public string FileName { get; init; } = "";
    public string ContentType { get; init; } = "";
    public long Size { get; init; }
    public string Base64 { get; init; } = "";
}