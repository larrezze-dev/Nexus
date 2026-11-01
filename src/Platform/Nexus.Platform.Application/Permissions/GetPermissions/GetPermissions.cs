using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Permissions.GetPermissions;

/// <summary>
/// <c>GET /v1/permissions</c> (<c>listPermissions</c>): offset pagination (conventions-api.md
/// §4) over the <c>access.permission</c> catalog.
/// </summary>
public sealed record GetPermissionsQuery(int Page, int PageSize, string? Sort) : IQuery<PagedResult<PermissionItem>>;

internal sealed class GetPermissionsValidator : AbstractValidator<GetPermissionsQuery>
{
    public GetPermissionsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, PermissionSortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
    }
}

internal sealed class GetPermissionsHandler : IQueryHandler<GetPermissionsQuery, PagedResult<PermissionItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly IPermissionReadDb _readDb;

    public GetPermissionsHandler(IRequestTransaction transaction, IPermissionReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<PagedResult<PermissionItem>>> Handle(GetPermissionsQuery query, CancellationToken cancellationToken)
    {
        SortSpec.TryParse(query.Sort, PermissionSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);
        var page = await _readDb.ListAsync(query.Page, query.PageSize, sort, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}
