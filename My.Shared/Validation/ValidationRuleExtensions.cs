using FluentValidation;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public static class ValidationRuleExtensions
    {
        public static IRuleBuilderOptions<T, string?> MustBeValidBase64<T>(
            this IRuleBuilder<T, string?> ruleBuilder) =>
            ruleBuilder
                .Must(value =>
                {
                    if (string.IsNullOrWhiteSpace(value)) return false;
                    try
                    {
                        Convert.FromBase64String(value);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .WithMessage("contentBase64 is not valid base64.");

    }
}