using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Validators;
using Microsoft.OpenApi;
using Nexus.BuildingBlocks.Application.Validation;

namespace Nexus.BuildingBlocks.Web.OpenApi;

/// <summary>
/// The schema-expressible part of the FluentValidation rules on one member of a validated
/// command/query (ADR-0014), and how to write it onto an OpenAPI schema.
/// <para>
/// Read conservatively - the generated document must never claim a constraint the service does
/// not enforce: a rule is only read when it is unconditional (no <c>When</c>/<c>Unless</c> on the
/// rule or the component), runs in the default rule set and targets a direct member (no nested
/// path, no <c>RuleForEach</c>). Opaque rules (<c>Must</c>, <c>Custom</c>, async rules) are
/// skipped; use the readable equivalents in <see cref="NexusRuleBuilderExtensions"/>.
/// </para>
/// <list type="table">
///   <listheader><term>Rule</term><description>Schema</description></listheader>
///   <item><term><c>GreaterThanOrEqualTo</c>/<c>GreaterThan</c> (constant)</term><description><c>minimum</c>/<c>exclusiveMinimum</c></description></item>
///   <item><term><c>LessThanOrEqualTo</c>/<c>LessThan</c> (constant)</term><description><c>maximum</c>/<c>exclusiveMaximum</c></description></item>
///   <item><term><c>InclusiveBetween</c>/<c>ExclusiveBetween</c></term><description>both bounds</description></item>
///   <item><term><c>Length</c>/<c>MinimumLength</c>/<c>MaximumLength</c> (constant)</term><description><c>minLength</c>/<c>maxLength</c></description></item>
///   <item><term><c>NotEmpty</c> on a string / collection</term><description><c>minLength: 1</c> / <c>minItems: 1</c></description></item>
///   <item><term><c>Matches</c> (constant pattern, no regex options)</term><description><c>pattern</c></description></item>
///   <item><term><see cref="IAllowedValuesValidator"/> (<c>MustBeOneOf</c>)</term><description><c>enum</c></description></item>
///   <item><term><see cref="IRequiredKeysValidator"/> (<c>MustContainNonBlankKeys</c>)</term><description><c>required</c> keys of the object</description></item>
/// </list>
/// Top-level <c>required</c> is never inferred: the validator sees the command after the
/// endpoint mapped (and possibly defaulted) the input, so "rejects null" does not imply "the
/// JSON property must be present". The request DTO's own shape already drives that.
/// </summary>
internal sealed class ValidationRuleConstraints
{
    private const string DefaultRuleSet = "default";

    private static readonly RegexOptions NeutralRegexOptions =
        RegexOptions.None | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private readonly List<string> _patterns = [];
    private readonly List<IReadOnlyList<string>> _allowedValueSets = [];
    private readonly List<string> _requiredKeys = [];

    private ValidationRuleConstraints(Type memberType)
    {
        MemberType = Nullable.GetUnderlyingType(memberType) ?? memberType;
    }

    public Type MemberType { get; }

    public decimal? Minimum { get; private set; }

    public bool MinimumIsExclusive { get; private set; }

    public decimal? Maximum { get; private set; }

    public bool MaximumIsExclusive { get; private set; }

    public int? MinLength { get; private set; }

    public int? MaxLength { get; private set; }

    public bool NotEmpty { get; private set; }

    /// <summary>The single pattern, or <see langword="null"/> when there is none or more than one
    /// distinct pattern (JSON Schema allows only one <c>pattern</c> per schema).</summary>
    public string? Pattern => _patterns.Distinct(StringComparer.Ordinal).Count() == 1 ? _patterns[0] : null;

    /// <summary>The intersection of every allowed-values rule, in first-declared order, or
    /// <see langword="null"/> when there is none.</summary>
    public IReadOnlyList<string>? AllowedValues
    {
        get
        {
            if (_allowedValueSets.Count == 0)
            {
                return null;
            }

            IEnumerable<string> values = _allowedValueSets[0];
            foreach (var set in _allowedValueSets.Skip(1))
            {
                values = values.Intersect(set, StringComparer.Ordinal);
            }

            return values.ToList();
        }
    }

    public IReadOnlyList<string> RequiredKeys => _requiredKeys;

    /// <summary>
    /// Reads every member of <paramref name="validator"/> that carries at least one
    /// schema-expressible rule, keyed by member name (case-insensitive).
    /// </summary>
    public static IReadOnlyDictionary<string, ValidationRuleConstraints> Read(IValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = new Dictionary<string, ValidationRuleConstraints>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in validator.CreateDescriptor().Rules)
        {
            if (!IsUnconditionalDirectMemberRule(rule))
            {
                continue;
            }

            if (!result.TryGetValue(rule.PropertyName, out var constraints))
            {
                constraints = new ValidationRuleConstraints(rule.TypeToValidate);
            }

            var any = false;
            foreach (var component in rule.Components)
            {
                if (component.HasCondition || component.HasAsyncCondition)
                {
                    continue;
                }

                any |= constraints.Add(component.Validator);
            }

            if (any)
            {
                result[rule.PropertyName] = constraints;
            }
        }

        return result;
    }

    /// <summary>
    /// Writes these constraints onto <paramref name="schema"/>, only ever tightening what the
    /// schema already says (an existing stricter bound, or a different pattern/enum, is left
    /// alone rather than overwritten).
    /// </summary>
    public void ApplyTo(OpenApiSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (IsNumeric(MemberType))
        {
            ApplyNumericBounds(schema);
        }

        if (MemberType == typeof(string))
        {
            ApplyStringConstraints(schema);
        }
        else if (NotEmpty && IsCollection(MemberType))
        {
            schema.MinItems = Math.Max(schema.MinItems ?? 0, 1);
        }

        if (_requiredKeys.Count > 0 && IsStringDictionary(MemberType))
        {
            ApplyRequiredKeys(schema);
        }
    }

    private static bool IsUnconditionalDirectMemberRule(IValidationRule rule)
    {
        if (rule.HasCondition || rule.HasAsyncCondition)
        {
            return false;
        }

        if (string.IsNullOrEmpty(rule.PropertyName) || rule.PropertyName.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        if (rule.RuleSets is { Length: > 0 } ruleSets &&
            !ruleSets.Contains(DefaultRuleSet, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // RuleForEach: its rules constrain the elements, not the collection itself.
        return !rule.GetType().GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollectionRule<,>));
    }

    private bool Add(IPropertyValidator validator)
    {
        switch (validator)
        {
            case IAllowedValuesValidator allowed:
                _allowedValueSets.Add(allowed.AllowedValues);
                return true;

            case IRequiredKeysValidator requiredKeys:
                foreach (var key in requiredKeys.RequiredKeys)
                {
                    if (!_requiredKeys.Contains(key, StringComparer.Ordinal))
                    {
                        _requiredKeys.Add(key);
                    }
                }

                return true;

            case IComparisonValidator comparison when comparison.MemberToCompare is null:
                return AddComparison(comparison.Comparison, ToDecimal(comparison.ValueToCompare));

            case IBetweenValidator between:
                return AddBetween(validator, between);

            case ILengthValidator length:
                return AddLength(validator, length);

            case INotEmptyValidator:
                NotEmpty = true;
                return true;

            case IRegularExpressionValidator regex when validator is not IEmailValidator:
                return AddPattern(validator, regex.Expression);

            default:
                return false;
        }
    }

    private bool AddComparison(Comparison comparison, decimal? value)
    {
        if (value is null)
        {
            return false;
        }

        switch (comparison)
        {
            case Comparison.GreaterThanOrEqual:
                TightenMinimum(value.Value, exclusive: false);
                return true;
            case Comparison.GreaterThan:
                TightenMinimum(value.Value, exclusive: true);
                return true;
            case Comparison.LessThanOrEqual:
                TightenMaximum(value.Value, exclusive: false);
                return true;
            case Comparison.LessThan:
                TightenMaximum(value.Value, exclusive: true);
                return true;
            default:
                return false;
        }
    }

    private bool AddBetween(IPropertyValidator validator, IBetweenValidator between)
    {
        bool exclusive;
        if (validator is IInclusiveBetweenValidator)
        {
            exclusive = false;
        }
        else if (IsClosedGenericOf(validator.GetType(), typeof(ExclusiveBetweenValidator<,>)))
        {
            exclusive = true;
        }
        else
        {
            return false;
        }

        var from = ToDecimal(between.From);
        var to = ToDecimal(between.To);
        if (from is null || to is null)
        {
            return false;
        }

        TightenMinimum(from.Value, exclusive);
        TightenMaximum(to.Value, exclusive);
        return true;
    }

    private bool AddLength(IPropertyValidator validator, ILengthValidator length)
    {
        // Length(x => min, x => max): the bounds depend on the instance, Min/Max stay at their
        // defaults and must not be read.
        if (ReadsInstanceBound(validator, "MinFunc") || ReadsInstanceBound(validator, "MaxFunc"))
        {
            return false;
        }

        var any = false;
        if (length.Min > 0)
        {
            MinLength = Math.Max(MinLength ?? 0, length.Min);
            any = true;
        }

        if (length.Max >= 0)
        {
            MaxLength = MaxLength is null ? length.Max : Math.Min(MaxLength.Value, length.Max);
            any = true;
        }

        return any;
    }

    private bool AddPattern(IPropertyValidator validator, string? expression)
    {
        // Expression is null for Matches(x => ...), whose pattern depends on the instance.
        if (string.IsNullOrEmpty(expression))
        {
            return false;
        }

        // Matches(expression, RegexOptions.IgnoreCase) etc. accepts more than the bare pattern
        // says; only a pattern with neutral options is projected. The constant regex is held by
        // FluentValidation's RegularExpressionValidator in a Func<T, Regex> that ignores its
        // argument (Expression is only non-null for the constant constructors).
        var regex = ReadConstantRegex(validator);
        if (regex is null || (regex.Options & ~NeutralRegexOptions) != 0)
        {
            return false;
        }

        _patterns.Add(expression);
        return true;
    }

    private void TightenMinimum(decimal value, bool exclusive)
    {
        if (Minimum is null || value > Minimum || (value == Minimum && exclusive))
        {
            Minimum = value;
            MinimumIsExclusive = exclusive;
        }
    }

    private void TightenMaximum(decimal value, bool exclusive)
    {
        if (Maximum is null || value < Maximum || (value == Maximum && exclusive))
        {
            Maximum = value;
            MaximumIsExclusive = exclusive;
        }
    }

    private void ApplyNumericBounds(OpenApiSchema schema)
    {
        if (Minimum is { } minimum && IsStricterLowerBound(schema, minimum, MinimumIsExclusive))
        {
            var text = minimum.ToString(CultureInfo.InvariantCulture);
            schema.Minimum = MinimumIsExclusive ? null : text;
            schema.ExclusiveMinimum = MinimumIsExclusive ? text : null;
        }

        if (Maximum is { } maximum && IsStricterUpperBound(schema, maximum, MaximumIsExclusive))
        {
            var text = maximum.ToString(CultureInfo.InvariantCulture);
            schema.Maximum = MaximumIsExclusive ? null : text;
            schema.ExclusiveMaximum = MaximumIsExclusive ? text : null;
        }
    }

    private void ApplyStringConstraints(OpenApiSchema schema)
    {
        var minLength = Math.Max(MinLength ?? 0, NotEmpty ? 1 : 0);
        if (minLength > 0)
        {
            schema.MinLength = Math.Max(schema.MinLength ?? 0, minLength);
        }

        if (MaxLength is { } maxLength)
        {
            schema.MaxLength = schema.MaxLength is null ? maxLength : Math.Min(schema.MaxLength.Value, maxLength);
        }

        if (Pattern is { } pattern && (schema.Pattern is null || schema.Pattern == pattern))
        {
            schema.Pattern = pattern;
        }

        if (AllowedValues is { Count: > 0 } allowed && (schema.Enum is null || schema.Enum.Count == 0))
        {
            var values = new List<JsonNode>(allowed.Count + 1);
            values.AddRange(allowed.Select(value => (JsonNode)JsonValue.Create(value)));

            // The allowed-values rule lets null through (FluentValidation convention), so a
            // nullable schema keeps null as a valid value.
            if (schema.Type is { } type && type.HasFlag(JsonSchemaType.Null))
            {
                values.Add(null!);
            }

            schema.Enum = values;
        }
    }

    private void ApplyRequiredKeys(OpenApiSchema schema)
    {
        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        schema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

        foreach (var key in _requiredKeys)
        {
            schema.Required.Add(key);

            // The value of a required key is a non-blank string.
            if (!schema.Properties.ContainsKey(key))
            {
                schema.Properties[key] = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1 };
            }
        }
    }

    private static bool IsStricterLowerBound(OpenApiSchema schema, decimal value, bool exclusive)
    {
        var existingInclusive = ParseBound(schema.Minimum);
        var existingExclusive = ParseBound(schema.ExclusiveMinimum);

        if (existingInclusive is null && existingExclusive is null)
        {
            return true;
        }

        if (existingExclusive is { } ex && (value < ex || (value == ex && !exclusive)))
        {
            return false;
        }

        return existingInclusive is not { } inc || value > inc || (value == inc && exclusive);
    }

    private static bool IsStricterUpperBound(OpenApiSchema schema, decimal value, bool exclusive)
    {
        var existingInclusive = ParseBound(schema.Maximum);
        var existingExclusive = ParseBound(schema.ExclusiveMaximum);

        if (existingInclusive is null && existingExclusive is null)
        {
            return true;
        }

        if (existingExclusive is { } ex && (value > ex || (value == ex && !exclusive)))
        {
            return false;
        }

        return existingInclusive is not { } inc || value < inc || (value == inc && exclusive);
    }

    private static decimal? ParseBound(string? text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static decimal? ToDecimal(object? value)
    {
        try
        {
            return value switch
            {
                byte or sbyte or short or ushort or int or uint or long or ulong or decimal =>
                    Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                double d when double.IsFinite(d) => (decimal)d,
                float f when float.IsFinite(f) => (decimal)f,
                _ => null,
            };
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool IsCollection(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    private static bool IsStringDictionary(Type type) =>
        typeof(IEnumerable<KeyValuePair<string, string>>).IsAssignableFrom(type);

    private static bool IsClosedGenericOf(Type type, Type openGeneric)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == openGeneric)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReadsInstanceBound(IPropertyValidator validator, string propertyName) =>
        validator.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(validator) is not null;

    private static Regex? ReadConstantRegex(IPropertyValidator validator)
    {
        for (var type = validator.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField("_regexFunc", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(validator) is not Delegate regexFunc)
            {
                continue;
            }

            try
            {
                return regexFunc.DynamicInvoke([null]) as Regex;
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        return null;
    }
}
