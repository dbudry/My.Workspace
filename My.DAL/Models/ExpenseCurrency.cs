using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    public class ExpenseCurrency
    {
        public string ExpenseCurrencyId { get; set; } = null!;

        [Required, MaxLength(8)]
        public string Code { get; set; } = null!;

        [Required, MaxLength(40)]
        public string Name { get; set; } = null!;

        [Required, MaxLength(16)]
        public string Color { get; set; } = "#1565C0";

        /// <summary>US dollars for one unit of this currency. USD is always 1.</summary>
        public decimal RateToUsd { get; set; } = 1m;

        public bool IsActive { get; set; } = true;

        /// <summary>Hidden after a line has used it. The row stays so that line still resolves.</summary>
        public bool IsRemoved { get; set; }

        /// <summary>USD cannot be renamed, retired, or given another rate.</summary>
        public bool IsLocked { get; set; }

        public int SortOrder { get; set; }
    }
}
