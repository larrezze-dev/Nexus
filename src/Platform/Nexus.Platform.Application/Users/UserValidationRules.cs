using FluentValidation;
using Nexus.BuildingBlocks.Application.Validation;
using Nexus.Platform.Domain.Users;

namespace Nexus.Platform.Application.Users;

/// <summary>Shape rules shared by the user commands (400 on failure, conventions-backend.md §6).</summary>
internal static class UserValidationRules
{
    public static IRuleBuilderOptions<T, string?> MustBeNonBlank<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage("'{PropertyName}' must not be empty.");

    private static readonly string UnsupportedLanguageMessage =
        $"'{{PropertyName}}' must be one of: {string.Join(", ", Languages.Supported)}.";

    /// <summary>Mandatory and one of <see cref="Languages.Supported"/>; built on
    /// <see cref="NexusRuleBuilderExtensions.MustBeOneOf{T}"/> so the generated OpenAPI document
    /// shows the allowed values as an <c>enum</c> (ADR-0014).</summary>
    public static IRuleBuilderOptions<T, string?> MustBeSupportedLanguage<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotNull().WithMessage(UnsupportedLanguageMessage)
            .MustBeOneOf(Languages.Supported).WithMessage(UnsupportedLanguageMessage);
}
