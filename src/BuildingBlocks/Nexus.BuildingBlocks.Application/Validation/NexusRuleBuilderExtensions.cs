using FluentValidation;

namespace Nexus.BuildingBlocks.Application.Validation;

/// <summary>
/// FluentValidation rules whose constraint stays readable after the validator is built
/// (ADR-0014), so the generated OpenAPI document can show it. Prefer these over an equivalent
/// <c>.Must(...)</c> lambda, which is an opaque delegate to any reader of the rules:
/// <list type="bullet">
///   <item><see cref="MustBeOneOf{T}"/> → OpenAPI <c>enum</c>.</item>
///   <item><see cref="MustContainNonBlankKeys{T}"/> → OpenAPI <c>required</c> on the dictionary's
///   object schema.</item>
/// </list>
/// The built-in FluentValidation rules <c>GreaterThan[OrEqualTo]</c>, <c>LessThan[OrEqualTo]</c>,
/// <c>InclusiveBetween</c>/<c>ExclusiveBetween</c>, <c>Length</c>/<c>MinimumLength</c>/
/// <c>MaximumLength</c>, <c>NotEmpty</c> and <c>Matches</c> are already readable and need no
/// wrapper.
/// </summary>
public static class NexusRuleBuilderExtensions
{
    /// <summary>
    /// The value, when present, must be one of <paramref name="allowedValues"/> (ordinal
    /// comparison). <see langword="null"/> passes - chain <c>NotNull()</c> first if the value is
    /// mandatory.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> MustBeOneOf<T>(
        this IRuleBuilder<T, string?> rule,
        IEnumerable<string> allowedValues)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.SetValidator(new AllowedValuesValidator<T>(allowedValues));
    }

    /// <summary>
    /// The dictionary, when present, must contain every one of <paramref name="requiredKeys"/>
    /// with a non-blank value. <see langword="null"/> passes - chain <c>NotNull()</c> first if the
    /// dictionary is mandatory.
    /// </summary>
    public static IRuleBuilderOptions<T, IReadOnlyDictionary<string, string>?> MustContainNonBlankKeys<T>(
        this IRuleBuilder<T, IReadOnlyDictionary<string, string>?> rule,
        params string[] requiredKeys)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.SetValidator(new RequiredNonBlankKeysValidator<T>(requiredKeys));
    }
}
