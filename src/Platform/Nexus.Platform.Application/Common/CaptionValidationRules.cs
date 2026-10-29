using FluentValidation;
using Nexus.BuildingBlocks.Application.Validation;

namespace Nexus.Platform.Application.Common;

/// <summary>
/// Shape rule shared by every <c>captionI18n</c> field (400 on failure, conventions-backend.md
/// §6): <c>de</c> is mandatory (<c>_shared/components.yaml#/components/schemas/I18nText</c>),
/// matching the database's own <c>CHECK (... ? 'de')</c>. Built on
/// <see cref="NexusRuleBuilderExtensions.MustContainNonBlankKeys{T}"/> so the generated OpenAPI
/// document shows <c>de</c> as a required key (ADR-0014).
/// </summary>
internal static class CaptionValidationRules
{
    private const string Message = "'{PropertyName}' must include a non-empty 'de' caption.";

    public static IRuleBuilderOptions<T, IReadOnlyDictionary<string, string>?> MustHaveGermanCaption<T>(
        this IRuleBuilder<T, IReadOnlyDictionary<string, string>> rule) =>
        rule.NotNull().WithMessage(Message)
            .MustContainNonBlankKeys("de").WithMessage(Message);
}
