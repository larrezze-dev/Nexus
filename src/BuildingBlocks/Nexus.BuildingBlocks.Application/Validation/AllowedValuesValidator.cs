using FluentValidation;
using FluentValidation.Validators;

namespace Nexus.BuildingBlocks.Application.Validation;

/// <summary>
/// A property validator that restricts a string to a fixed, closed set of values and exposes
/// that set for inspection (ADR-0014). Anything that reads FluentValidation rules by
/// reflection - the OpenAPI projection in <c>Nexus.BuildingBlocks.Web.OpenApi</c> - recognises
/// this interface and can turn the rule into an <c>enum</c>; a plain
/// <c>.Must(value =&gt; value is "A" or "B")</c> is an opaque delegate and cannot be read back.
/// </summary>
public interface IAllowedValuesValidator : IPropertyValidator
{
    /// <summary>The allowed values, in declaration order, compared ordinally.</summary>
    IReadOnlyList<string> AllowedValues { get; }
}

/// <summary>
/// <see cref="IAllowedValuesValidator"/> implementation used by
/// <see cref="NexusRuleBuilderExtensions.MustBeOneOf{T}"/>. Follows the FluentValidation
/// convention for value validators (<c>Matches</c>, <c>Length</c>, <c>IsEnumName</c>...): a
/// <see langword="null"/> value is valid, so the rule only constrains a value that is present.
/// Combine with <c>NotNull()</c> when the value is mandatory.
/// </summary>
public sealed class AllowedValuesValidator<T> : PropertyValidator<T, string?>, IAllowedValuesValidator
{
    private readonly HashSet<string> _allowed;

    public AllowedValuesValidator(IEnumerable<string> allowedValues)
    {
        ArgumentNullException.ThrowIfNull(allowedValues);

        var values = new List<string>();
        foreach (var value in allowedValues)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(allowedValues));
            if (!values.Contains(value, StringComparer.Ordinal))
            {
                values.Add(value);
            }
        }

        if (values.Count == 0)
        {
            throw new ArgumentException("At least one allowed value is required.", nameof(allowedValues));
        }

        AllowedValues = values.AsReadOnly();
        _allowed = new HashSet<string>(values, StringComparer.Ordinal);
    }

    public override string Name => "AllowedValuesValidator";

    public IReadOnlyList<string> AllowedValues { get; }

    public override bool IsValid(ValidationContext<T> context, string? value)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is null || _allowed.Contains(value))
        {
            return true;
        }

        context.MessageFormatter.AppendArgument("AllowedValues", string.Join(", ", AllowedValues));
        return false;
    }

    protected override string GetDefaultMessageTemplate(string errorCode) =>
        "'{PropertyName}' must be one of: {AllowedValues}.";
}
