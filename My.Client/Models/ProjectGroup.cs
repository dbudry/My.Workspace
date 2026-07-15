using System.ComponentModel.DataAnnotations;
using My.Shared.Dtos.ProjectGroup;

namespace My.Client.Models
{
    public class ProjectGroup
    {
        public string ProjectGroupId { get; set; } = null!;

        [Required]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters.")]
        public string Name { get; set; } = null!;

        public string Color { get; set; } = "#616161";

        public ProjectGroup() { }

        public ProjectGroup(ProjectGroupDto dto)
        {
            ProjectGroupId = dto.ProjectGroupId;
            Name = dto.Name;
            Color = dto.Color;
        }
    }
}
