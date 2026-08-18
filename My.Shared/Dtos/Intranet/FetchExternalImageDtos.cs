namespace My.Shared.Dtos.Intranet;

public class FetchExternalImageResultDto
{
    public string Base64 { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long ByteLength { get; set; }
    public string? SuggestedFileName { get; set; }
}