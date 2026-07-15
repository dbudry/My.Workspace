using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker.Http;
using My.Shared.Dtos.Query;
using My.Shared.Validation;

namespace My.Functions.Helpers
{
    /// <summary>
    /// API-layer FluentValidation gate — runs before business rules and DB writes.
    /// </summary>
    public static class RequestValidator
    {
        public const string InvalidBodyMessage = "Invalid body.";
        public const string RequestBodyRequiredMessage = "Request body is required.";

        public static async Task<IActionResult?> BadRequestIfInvalidAsync<T>(
            IValidator<T> validator,
            T? dto,
            Action<ValidationContext<T>>? configure = null,
            CancellationToken cancellationToken = default)
        {
            if (dto is null)
                return new BadRequestObjectResult(RequestBodyRequiredMessage);

            var context = ValidationContext<T>.CreateWithOptions(dto, _ => { });
            configure?.Invoke(context);

            var result = await validator.ValidateAsync(context, cancellationToken);
            if (result.IsValid)
                return null;

            return new BadRequestObjectResult(ValidationResultFormatter.ToMessages(result));
        }

        public static IActionResult? BadRequestIfInvalid<T>(
            IValidator<T> validator,
            T? dto,
            Action<ValidationContext<T>>? configure = null)
        {
            if (dto is null)
                return new BadRequestObjectResult(RequestBodyRequiredMessage);

            var context = ValidationContext<T>.CreateWithOptions(dto, _ => { });
            configure?.Invoke(context);

            var result = validator.Validate(context);
            if (result.IsValid)
                return null;

            return new BadRequestObjectResult(ValidationResultFormatter.ToMessages(result));
        }

        public static async Task<(T? Dto, IActionResult? Error)> ReadJsonAndValidateAsync<T>(
            HttpRequestData req,
            IValidator<T> validator,
            Action<ValidationContext<T>>? configure = null,
            string? invalidBodyMessage = null,
            CancellationToken cancellationToken = default)
        {
            T? dto;
            try
            {
                dto = await req.ReadFromJsonAsync<T>(cancellationToken);
            }
            catch
            {
                return (default, new BadRequestObjectResult(invalidBodyMessage ?? InvalidBodyMessage));
            }

            var error = await BadRequestIfInvalidAsync(validator, dto, configure, cancellationToken);
            return (dto, error);
        }

        public static (DateRangeQueryDto? Query, IActionResult? Error) ParseDateRangeQuery(
            HttpRequestData req,
            IValidator<DateRangeQueryDto> validator)
        {
            if (!DateTime.TryParse(req.Query["from"], out var fromLocal))
                return (null, new BadRequestObjectResult("from is required (YYYY-MM-DD)."));
            if (!DateTime.TryParse(req.Query["to"], out var toLocal))
                return (null, new BadRequestObjectResult("to is required (YYYY-MM-DD)."));

            var query = new DateRangeQueryDto
            {
                From = fromLocal,
                To = toLocal
            };

            var error = BadRequestIfInvalid(validator, query);
            return error is null ? (query, null) : (null, error);
        }

    }
}