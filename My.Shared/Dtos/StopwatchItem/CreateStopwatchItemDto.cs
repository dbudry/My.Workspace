using System.ComponentModel.DataAnnotations;

namespace My.Shared.Dtos.StopwatchItem
{
    public class CreateStopwatchItemDto
    {
        [Required]
        [StringLength(50, MinimumLength = 2)]
        public string Name { get; set; } = null!;

        [Required(ErrorMessage = "Project is required.")]
        public string ProjectId { get; set; } = null!;
    }
}