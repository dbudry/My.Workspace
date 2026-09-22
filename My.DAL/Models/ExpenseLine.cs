using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    public class ExpenseLine
    {
        public string ExpenseLineId { get; set; } = null!;

        public string ExpenseReportId { get; set; } = null!;
        public ExpenseReport ExpenseReport { get; set; } = null!;

        public DateTime Date { get; set; }

        [Required, MaxLength(200)]
        public string Description { get; set; } = null!;

        [Required, MaxLength(32)]
        public string Category { get; set; } = null!;

        public decimal Amount { get; set; }
        public decimal? Miles { get; set; }

        [MaxLength(5)]
        public string? TransportationCode { get; set; }

        [MaxLength(5)]
        public string? MiscellaneousCode { get; set; }

        public bool MealBreakfast { get; set; }
        public bool MealLunch { get; set; }
        public bool MealDinner { get; set; }

        public int SortOrder { get; set; }

        public ICollection<ExpenseReceipt> Receipts { get; set; } = new List<ExpenseReceipt>();
    }
}
