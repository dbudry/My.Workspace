using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    /// <summary>
    /// One filed statement PDF per Charge to. Receipts for that Charge to are inside the file.
    /// </summary>
    public class ExpenseFiledPdf
    {
        public string ExpenseFiledPdfId { get; set; } = null!;

        public string ExpenseReportId { get; set; } = null!;
        public ExpenseReport ExpenseReport { get; set; } = null!;

        [MaxLength(450)]
        public string? ChargeToOrganizationId { get; set; }

        [MaxLength(100)]
        public string ChargeToName { get; set; } = "";

        [Required, MaxLength(128)]
        public string DriveFileId { get; set; } = null!;

        public int SortOrder { get; set; }

        /// <summary>
        /// <see cref="My.Shared.Rules.ExpenseFormVersionRules.Current"/> at the time this file was generated.
        /// Null on packets filed before form versions existed.
        /// </summary>
        public int? FormVersion { get; set; }
    }
}
