using FluentValidation;

namespace Nexus.Platform.Application.Common;

/// <summary>
/// Shape rule shared by every <c>captionI18n</c> field (400 on failure, conventions-backend.md
/// §6): <c>de</c> is mandatory (<c>_shared/components.yaml#/components/schemas/I18nText</c>),
/// matching the database's own <c>CHECK (... ? 'de')</c>.
/// </summary>
internal static class CaptionValidationRules
{
    public static IRuleBuilderOptions<T, IReadOnlyDictionary<string, string>> MustHaveGermanCaption<T>(
        this IRuleBuilder<T, IReadOnlyDictionary<string, string>> rule) =>
        rule.Must(captions => captions is not null && captions.TryGetValue("de", out var de) && !string.IsNullOrWhiteSpace(de))
            .WithMessage("'{PropertyName}' must include a non-empty 'de' caption.");
}
