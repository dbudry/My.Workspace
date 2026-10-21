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

        /// <summary>US dollar amount. Personal-car lines are miles times the report mileage rate.</summary>
        public decimal Amount { get; set; }

        /// <summary>Amount the person spent, in <see cref="CurrencyCode"/>.</summary>
        public decimal SpentAmount { get; set; }

        /// <summary>ISO-style code. USD when this line is mileage or was filed before currencies.</summary>
        [Required, MaxLength(8)]
        public string CurrencyCode { get; set; } = "USD";

        /// <summary>US dollars per one unit of <see cref="CurrencyCode"/>, copied when the line was saved.</summary>
        public decimal ExchangeRateToUsd { get; set; } = 1m;

        /// <summary>
        /// Start of the credit day in the owner's time zone, stored as UTC.
        /// Set only after the report was paid and the line was credited.
        /// </summary>
        public DateTime? ReturnedAt { get; set; }

        /// <summary>Amount credited. Null means the whole line, for credits saved before partial amounts.</summary>
        public decimal? CreditAmount { get; set; }

        [MaxLength(8)]
        public string? CreditCurrencyCode { get; set; }

        [MaxLength(200)]
        public string? CreditDescription { get; set; }

        /// <summary>Default-currency units for one unit of <see cref="CreditCurrencyCode"/>.</summary>
        public decimal? CreditExchangeRate { get; set; }

        public decimal? Miles { get; set; }

        [MaxLength(5)]
        public string? TransportationCode { get; set; }

        [MaxLength(5)]
        public string? MiscellaneousCode { get; set; }

        /// <summary>Breakfast, Lunch, or Dinner. Null when this line is not a meal.</summary>
        [MaxLength(20)]
        public string? MealType { get; set; }

        /// <summary>How many people that meal covers.</summary>
        public int? HeadCount { get; set; }

        public int SortOrder { get; set; }

        public ICollection<ExpenseReceipt> Receipts { get; set; } = new List<ExpenseReceipt>();
    }
}
