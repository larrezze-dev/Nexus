using FluentValidation;
using Nexus.Platform.Domain.Users;

namespace Nexus.Platform.Application.Users;

/// <summary>Shape rules shared by the user commands (400 on failure, conventions-backend.md §6).</summary>
internal static class UserValidationRules
{
    public static IRuleBuilderOptions<T, string?> MustBeNonBlank<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage("'{PropertyName}' must not be empty.");

    public static IRuleBuilderOptions<T, string?> MustBeSupportedLanguage<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => value is not null && Languages.Supported.Contains(value))
            .WithMessage($"'{{PropertyName}}' must be one of: {string.Join(", ", Languages.Supported)}.");
}
