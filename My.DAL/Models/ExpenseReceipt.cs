using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models;

public class ExpenseReceipt
{
    public string ExpenseReceiptId { get; set; } = null!;

    public string ExpenseLineId { get; set; } = null!;
    public ExpenseLine ExpenseLine { get; set; } = null!;

    [Required, MaxLength(200)]
    public string OriginalFileName { get; set; } = null!;

    [Required, MaxLength(100)]
    public string MimeType { get; set; } = null!;

    public int SizeBytes { get; set; }

    /// <summary>Legacy local-debug blobs. New uploads store bytes in Drive.</summary>
    public byte[]? Content { get; set; }

    [MaxLength(128)]
    public string? DriveFileId { get; set; }

    public DateTime UploadedAt { get; set; }

    [MaxLength(450)]
    public string UploadedByUserId { get; set; } = null!;
}
