namespace My.Shared.Dtos.Intranet;

public class IntranetUploadLimitDto
{
    public string Extension { get; set; } = string.Empty;
    public int MaxMegabytes { get; set; } = 1;
}