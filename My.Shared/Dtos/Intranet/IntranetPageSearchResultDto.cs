namespace My.Shared.Dtos.Intranet;

public class IntranetPageSearchResultDto
{
    public string PageId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Slug { get; set; }
    public string Excerpt { get; set; } = "";
    public bool IsPublished { get; set; }
    public int Score { get; set; }
}