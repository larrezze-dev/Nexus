using FluentValidation;
using FluentValidation.Validators;

namespace Nexus.BuildingBlocks.Application.Validation;

/// <summary>
/// Rejects a client-supplied <see cref="DateTimeOffset"/> finer than one microsecond (ADR-0016).
/// Used by <see cref="NexusRuleBuilderExtensions.MustBeMicrosecondPrecision{T}(IRuleBuilder{T, DateTimeOffset})"/>
/// and its nullable overload.
/// </summary>
/// <remarks>
/// Event instants are Avro <c>timestamp-micros</c> (conventions-events.md) and
/// <c>AvroPayloadSerializer</c> rejects anything finer instead of rounding it; PostgreSQL
/// <c>timestamptz</c> stores microseconds and rounds (not truncates) the rest. ISO-8601 and
/// System.Text.Json accept 7 fractional digits, so without this rule such a value reaches the
/// outbox and fails there with a 500, or is stored 1 µs away from what the event carries. Same
/// policy as the serializer and as <see cref="RoundedSystemTimeProvider"/>'s alignment: one
/// microsecond is <see cref="TimeSpan.TicksPerMicrosecond"/> (10) ticks; reject, never round.
/// A <see langword="null"/> value is valid (FluentValidation convention for value validators).
/// </remarks>
public sealed class MicrosecondPrecisionValidator<T, TProperty> : PropertyValidator<T, TProperty>
{
    internal MicrosecondPrecisionValidator()
    {
    }

    public override string Name => "MicrosecondPrecisionValidator";

    public override bool IsValid(ValidationContext<T> context, TProperty value) =>
        value is not DateTimeOffset instant || instant.Ticks % TimeSpan.TicksPerMicrosecond == 0;

    protected override string GetDefaultMessageTemplate(string errorCode) =>
        "'{PropertyName}' must not be more precise than one microsecond (at most 6 fractional second digits).";
}
