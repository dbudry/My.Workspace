using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    /// <summary>A credit typed onto one expense. It reduces that expense only.</summary>
    public class ExpenseCredit
    {
        public string ExpenseCreditId { get; set; } = null!;

        public string ExpenseReportId { get; set; } = null!;
        public ExpenseReport ExpenseReport { get; set; } = null!;

        [MaxLength(450)]
        public string? ChargeToOrganizationId { get; set; }

        public DateTime CreditDate { get; set; }

        [Required, MaxLength(200)]
        public string Description { get; set; } = "";

        public decimal Amount { get; set; }

        [Required, MaxLength(8)]
        public string CurrencyCode { get; set; } = "USD";

        public decimal ExchangeRateToUsd { get; set; } = 1m;

        public int SortOrder { get; set; }
    }
}
