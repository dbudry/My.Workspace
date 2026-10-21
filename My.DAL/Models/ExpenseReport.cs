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

        /// <summary>The organization this expense is charged to. Set when the report is created.</summary>
        [MaxLength(450)]
        public string? ChargeToOrganizationId { get; set; }
        public Organization? ChargeToOrganization { get; set; }

        [MaxLength(100)]
        public string? ChargeToName { get; set; }

        [MaxLength(120)]
        public string? EmployeeNameSnapshot { get; set; }

        [MaxLength(255)]
        public string? AddressSnapshot { get; set; }

        public decimal MileageRateSnapshot { get; set; }

        [MaxLength(128)]
        public string? DriveUserFolderId { get; set; }

        [MaxLength(128)]
        public string? DrivePeriodFolderId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public ICollection<ExpenseLine> Lines { get; set; } = new List<ExpenseLine>();

        /// <summary>Credits typed onto this expense. They reduce this expense only.</summary>
        public ICollection<ExpenseCredit> FormCredits { get; set; } = new List<ExpenseCredit>();

        /// <summary>The filed statement for this expense, under Expenses/Filed.</summary>
        public ICollection<ExpenseFiledPdf> FiledPdfs { get; set; } = new List<ExpenseFiledPdf>();
    }
}
