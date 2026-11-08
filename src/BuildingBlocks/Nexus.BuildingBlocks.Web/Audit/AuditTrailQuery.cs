using FluentValidation;
using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.BuildingBlocks.Web.Audit;

/// <summary>
/// Input of <c>listAuditEvents</c> that has rules (ADR-0014: declared with
/// <c>.ValidatedBy&lt;AuditTrailQuery&gt;()</c> so the generated OpenAPI shows the same pattern and
/// bounds as <c>contracts/openapi/_shared/audit.yaml</c>). Members carry the raw path/query values.
/// </summary>
internal sealed record AuditTrailQuery(string AggregateType, int Page, int PageSize);

internal sealed class AuditTrailQueryValidator : AbstractValidator<AuditTrailQuery>
{
    /// <summary>Maximum <c>aggregateType</c> length (the contract's pattern has none).</summary>
    internal const int MaxAggregateTypeLength = 64;

    public AuditTrailQueryValidator()
    {
        // Same pattern as the aggregateType path parameter of the contract.
        RuleFor(q => q.AggregateType)
            // Must, not MaximumLength: the contract's schema has no maxLength, so keep it out of the
            // generated document (ADR-0014 projects MaximumLength) while still bounding the input.
            .Must(type => type.Length <= MaxAggregateTypeLength).WithMessage($"'Aggregate Type' must not be longer than {MaxAggregateTypeLength} characters.")
            .Matches("^[a-z][a-z0-9]*([-_][a-z0-9]+)*$");
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
    }
}
