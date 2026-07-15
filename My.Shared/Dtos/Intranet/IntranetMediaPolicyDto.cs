namespace My.Shared.Dtos.Intranet;

public class IntranetMediaPolicyDto
{
    public List<string> AllowedExtensions { get; set; } = new();
    public Dictionary<string, long> MaxUploadBytesByExtension { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsConfigured { get; set; }
    public string AllowedExtensionsDisplay { get; set; } = string.Empty;
    public string MaxUploadSizeDisplay { get; set; } = string.Empty;
}