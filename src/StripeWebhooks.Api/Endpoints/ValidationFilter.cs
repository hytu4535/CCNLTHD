using System.ComponentModel.DataAnnotations;

namespace StripeWebhooks.Api.Endpoints;

public sealed class ValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is null)
                continue;

            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(
                argument,
                new ValidationContext(argument),
                validationResults,
                validateAllProperties: true);

            if (isValid)
                continue;

            var errors = validationResults
                .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                    .Select(member => new { Member = member, Message = result.ErrorMessage ?? "Invalid value." }))
                .GroupBy(error => error.Member)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());

            return Results.ValidationProblem(errors);
        }

        return await next(context);
    }
}