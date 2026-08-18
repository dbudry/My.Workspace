namespace My.Shared.Dtos
{
    public class AppSettingDto
    {
        public string Key { get; set; } = null!;
        public string Value { get; set; } = null!;
        public string? Description { get; set; }
    }
}
