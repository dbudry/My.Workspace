using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    public class AppSetting
    {
        [Key, MaxLength(100)]
        public string Key { get; set; } = null!;

        [Required, MaxLength(500)]
        public string Value { get; set; } = null!;

        [MaxLength(200)]
        public string? Description { get; set; }
    }
}
