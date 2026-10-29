using FluentValidation;
using FluentValidation.Validators;

namespace Nexus.BuildingBlocks.Application.Validation;

/// <summary>
/// A property validator that requires a string dictionary (e.g. an <c>I18nText</c>
/// <c>captionI18n</c>) to contain a fixed set of keys, each with a non-blank value, and exposes
/// those keys for inspection (ADR-0014): the OpenAPI projection in
/// <c>Nexus.BuildingBlocks.Web.OpenApi</c> turns them into the object schema's
/// <c>required</c> list. A plain <c>.Must(d =&gt; d.ContainsKey("de"))</c> cannot be read back.
/// </summary>
public interface IRequiredKeysValidator : IPropertyValidator
{
    /// <summary>The keys that must be present with a non-blank value, compared ordinally.</summary>
    IReadOnlyList<string> RequiredKeys { get; }
}

/// <summary>
/// <see cref="IRequiredKeysValidator"/> implementation used by
/// <see cref="NexusRuleBuilderExtensions.MustContainNonBlankKeys{T}"/>. A <see langword="null"/>
/// dictionary is valid (FluentValidation convention for value validators); combine with
/// <c>NotNull()</c> when the dictionary itself is mandatory.
/// </summary>
public sealed class RequiredNonBlankKeysValidator<T> : PropertyValidator<T, IReadOnlyDictionary<string, string>?>, IRequiredKeysValidator
{
    public RequiredNonBlankKeysValidator(IEnumerable<string> requiredKeys)
    {
        ArgumentNullException.ThrowIfNull(requiredKeys);

        var keys = new List<string>();
        foreach (var key in requiredKeys)
        {
            ArgumentNullException.ThrowIfNull(key, nameof(requiredKeys));
            if (!keys.Contains(key, StringComparer.Ordinal))
            {
                keys.Add(key);
            }
        }

        if (keys.Count == 0)
        {
            throw new ArgumentException("At least one required key is needed.", nameof(requiredKeys));
        }

        RequiredKeys = keys.AsReadOnly();
    }

    public override string Name => "RequiredNonBlankKeysValidator";

    public IReadOnlyList<string> RequiredKeys { get; }

    public override bool IsValid(ValidationContext<T> context, IReadOnlyDictionary<string, string>? value)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is null)
        {
            return true;
        }

        foreach (var key in RequiredKeys)
        {
            if (!value.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
            {
                context.MessageFormatter.AppendArgument("RequiredKeys", string.Join(", ", RequiredKeys));
                return false;
            }
        }

        return true;
    }

    protected override string GetDefaultMessageTemplate(string errorCode) =>
        "'{PropertyName}' must include a non-empty value for: {RequiredKeys}.";
}
