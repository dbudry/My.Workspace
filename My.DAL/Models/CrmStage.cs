using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    /// <summary>
    /// A pipeline stage maintained by a CRM manager. Not seeded with HasData:
    /// a later migration would put the manager's edits back.
    /// </summary>
    public class CrmStage
    {
        public string CrmStageId { get; set; } = null!;

        [Required, MaxLength(50)]
        public string Name { get; set; } = null!;

        public int DefaultProbability { get; set; }

        /// <summary>Open, Closed Won, or Closed Lost.</summary>
        [Required, MaxLength(20)]
        public string Outcome { get; set; } = null!;

        public int SortOrder { get; set; }
    }
}
