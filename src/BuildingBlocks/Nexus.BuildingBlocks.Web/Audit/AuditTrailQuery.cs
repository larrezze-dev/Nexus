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
    public AuditTrailQueryValidator()
    {
        // Same pattern as the aggregateType path parameter of the contract.
        RuleFor(q => q.AggregateType)
            .Matches("^[a-z][a-z0-9]*([-_][a-z0-9]+)*$")
            // .NET's `$` also matches before a trailing "\n"; JSON-Schema/ECMAScript `$` does not.
            // The pattern stays identical to the contract's (projected into OpenAPI), this closes the gap.
            .Must(type => !type.EndsWith('\n')).WithMessage("'Aggregate Type' must match the contract's pattern.");
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
    }
}
