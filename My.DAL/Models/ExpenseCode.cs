using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    public class ExpenseCode
    {
        public string ExpenseCodeId { get; set; } = null!;

        /// <summary>Transportation or Miscellaneous. See <c>ExpenseCodeKindRules</c>.</summary>
        [Required, MaxLength(20)]
        public string Kind { get; set; } = null!;

        [Required, MaxLength(5)]
        public string Code { get; set; } = null!;

        [Required, MaxLength(80)]
        public string Label { get; set; } = null!;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Hidden from settings and from new lines. The row stays because an expense line
        /// already stores this code.
        /// </summary>
        public bool IsRemoved { get; set; }

        /// <summary>Transportation rows stay fixed. Miscellaneous rows are edited in Expense settings.</summary>
        public bool IsLocked { get; set; }

        public int SortOrder { get; set; }
    }
}
