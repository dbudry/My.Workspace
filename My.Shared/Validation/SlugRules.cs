namespace My.Shared.Validation
{
    /// <summary>
    /// Project calendar tags: titles use <c>[slug]</c> to route Google events to a project.
    /// Shape is shared by FluentValidation, the client DataAnnotations, and the DB column.
    /// </summary>
    public static class SlugRules
    {
        public const int MinLength = 2;
        public const int MaxLength = 10;

        public static string? Normalize(string? slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return null;
            var trimmed = slug.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        public static bool IsValidShape(string? slug)
        {
            if (slug == null) return true;
            if (slug.Length < MinLength || slug.Length > MaxLength) return false;
            foreach (var ch in slug)
            {
                if (!((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')))
                    return false;
            }
            return true;
        }

        public static string ShapeErrorMessage =>
            $"Slug must be between {MinLength} and {MaxLength} characters and may only contain lowercase letters and digits.";
    }
}
