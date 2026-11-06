using FluentValidation;
using Nexus.BuildingBlocks.Application.Validation;
using Shouldly;
using Xunit;

namespace Nexus.BuildingBlocks.Application.Tests.Validation;

/// <summary>
/// Runtime behaviour of the readable rules from <see cref="NexusRuleBuilderExtensions"/>
/// (ADR-0014): they must validate exactly like the <c>.Must(...)</c> lambdas they replace, and
/// expose their constraint for the OpenAPI projection.
/// </summary>
public sealed class NexusRuleBuilderExtensionsTests
{
    private static readonly string[] Statuses = ["DRAFT", "ACTIVE"];

    [Theory]
    [InlineData("DRAFT", true)]
    [InlineData("ACTIVE", true)]
    [InlineData("draft", false)]
    [InlineData("", false)]
    [InlineData("OTHER", false)]
    [InlineData(null, true)]
    public void MustBeOneOf_accepts_only_the_listed_values_and_lets_null_through(string? status, bool valid)
    {
        var validator = new InlineValidator<Sample>();
        validator.RuleFor(s => s.Status).MustBeOneOf(Statuses);

        var result = validator.Validate(new Sample(status, null));

        result.IsValid.ShouldBe(valid);
        if (!valid)
        {
            result.Errors.Single().ErrorMessage.ShouldBe("'Status' must be one of: DRAFT, ACTIVE.");
        }
    }

    [Fact]
    public void MustBeOneOf_combined_with_NotNull_rejects_null_once()
    {
        var validator = new InlineValidator<Sample>();
        validator.RuleFor(s => s.Status).NotNull().MustBeOneOf(Statuses);

        var result = validator.Validate(new Sample(null, null));

        result.Errors.Count.ShouldBe(1);
    }

    [Fact]
    public void MustBeOneOf_exposes_the_allowed_values_in_declaration_order_without_duplicates()
    {
        var allowed = new AllowedValuesValidator<Sample>(["B", "A", "B"]);

        allowed.AllowedValues.ShouldBe(["B", "A"]);
    }

    [Fact]
    public void MustBeOneOf_requires_at_least_one_value()
    {
        Should.Throw<ArgumentException>(() => new AllowedValuesValidator<Sample>([]));
    }

    [Theory]
    [MemberData(nameof(CaptionCases))]
    public void MustContainNonBlankKeys_requires_every_key_with_a_non_blank_value(
        Dictionary<string, string>? captions,
        bool valid)
    {
        var validator = new InlineValidator<Sample>();
        validator.RuleFor(s => s.Captions).MustContainNonBlankKeys("de");

        var result = validator.Validate(new Sample(null, captions));

        result.IsValid.ShouldBe(valid);
        if (!valid)
        {
            result.Errors.Single().ErrorMessage.ShouldBe("'Captions' must include a non-empty value for: de.");
        }
    }

    [Fact]
    public void MustContainNonBlankKeys_exposes_the_required_keys()
    {
        var requiredKeys = new RequiredNonBlankKeysValidator<Sample>(["de", "en", "de"]);

        requiredKeys.RequiredKeys.ShouldBe(["de", "en"]);
    }

    [Theory]
    [InlineData(0L, true)] // whole second
    [InlineData(1_234_560L, true)] // .123456 s: 6 fractional digits
    [InlineData(1_234_567L, false)] // .1234567 s: 7 fractional digits
    [InlineData(1L, false)] // one tick (100 ns)
    [InlineData(9L, false)]
    [InlineData(10L, true)] // exactly one microsecond
    public void MustBeMicrosecondPrecision_rejects_sub_microsecond_instants(long extraTicks, bool valid)
    {
        var instant = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero).AddTicks(extraTicks);
        var validator = new InlineValidator<Instants>();
        validator.RuleFor(s => s.Required).MustBeMicrosecondPrecision();
        validator.RuleFor(s => s.Optional).MustBeMicrosecondPrecision();

        var result = validator.Validate(new Instants(instant, instant));

        result.IsValid.ShouldBe(valid);
        if (!valid)
        {
            result.Errors.Select(e => e.PropertyName).ShouldBe(["Required", "Optional"]);
            result.Errors[0].ErrorMessage.ShouldBe(
                "'Required' must not be more precise than one microsecond (at most 6 fractional second digits).");
        }
    }

    [Fact]
    public void MustBeMicrosecondPrecision_lets_null_through()
    {
        var validator = new InlineValidator<Instants>();
        validator.RuleFor(s => s.Optional).MustBeMicrosecondPrecision();

        validator.Validate(new Instants(default, null)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void MustBeMicrosecondPrecision_accepts_what_RoundedSystemTimeProvider_hands_out()
    {
        var validator = new InlineValidator<Instants>();
        validator.RuleFor(s => s.Required).MustBeMicrosecondPrecision();

        validator.Validate(new Instants(RoundedSystemTimeProvider.Instance.GetUtcNow(), null)).IsValid.ShouldBeTrue();
    }

    public sealed record Instants(DateTimeOffset Required, DateTimeOffset? Optional);

    public static TheoryData<Dictionary<string, string>?, bool> CaptionCases() => new()
    {
        { new Dictionary<string, string> { ["de"] = "Bühne" }, true },
        { new Dictionary<string, string> { ["de"] = "Bühne", ["en"] = "Stage" }, true },
        { new Dictionary<string, string> { ["en"] = "Stage" }, false },
        { new Dictionary<string, string> { ["de"] = "   " }, false },
        { new Dictionary<string, string>(), false },
        { null, true },
    };

    public sealed record Sample(string? Status, IReadOnlyDictionary<string, string>? Captions);
}
