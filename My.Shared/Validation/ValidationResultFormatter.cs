using FluentValidation.Results;

namespace My.Shared.Validation
{
    public static class ValidationResultFormatter
    {
        public static string[] ToMessages(ValidationResult result) =>
            result.Errors.Select(e => e.ErrorMessage).Distinct().ToArray();

        public static string ToMessage(ValidationResult result) =>
            string.Join(" ", ToMessages(result));
    }
}