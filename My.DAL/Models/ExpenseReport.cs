using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    public class ExpenseReport
    {
        public string ExpenseReportId { get; set; } = null!;

        public string UserId { get; set; } = null!;
        public ApplicationUser User { get; set; } = null!;

        public int Year { get; set; }
        public int Month { get; set; }

        public DateTime CoverStart { get; set; }
        public DateTime CoverEnd { get; set; }
        public DateTime ReportDate { get; set; }

        [Required, MaxLength(20)]
        public string Status { get; set; } = null!;

        public DateTime? SubmittedAt { get; set; }
        public string? SubmittedByUserId { get; set; }

        public DateTime? ReimbursedAt { get; set; }
        public string? ReimbursedByUserId { get; set; }

        [MaxLength(2000)]
        public string? Purpose { get; set; }

        [MaxLength(100)]
        public string? PlantOrLocation { get; set; }

        public string? DepartmentId { get; set; }
        public Department? Department { get; set; }

        [MaxLength(200)]
        public string? ChargeToNote { get; set; }

        [MaxLength(120)]
        public string? EmployeeNameSnapshot { get; set; }

        [MaxLength(255)]
        public string? AddressSnapshot { get; set; }

        public decimal MileageRateSnapshot { get; set; }

        [MaxLength(128)]
        public string? DriveUserFolderId { get; set; }

        [MaxLength(128)]
        public string? DrivePeriodFolderId { get; set; }

        /// <summary>
        /// App Drive file id of the statement PDF filed on submit, under
        /// Expenses/Filed — not the per-user receipt folder. Survives user delete.
        /// </summary>
        [MaxLength(128)]
        public string? DriveFiledPdfFileId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public ICollection<ExpenseLine> Lines { get; set; } = new List<ExpenseLine>();
    }
}
