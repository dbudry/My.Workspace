using System.ComponentModel.DataAnnotations;

namespace My.Shared.Dtos.StopwatchItem
{
    public class UpdateStopwatchItemDto
    {
        [Required]
        public string StopwatchItemId { get; set; } = null!;

        [Required]
        [StringLength(50, MinimumLength = 2)]
        public string Name { get; set; } = null!;

        [Required]
        public string ProjectId { get; set; } = null!;
    }
}