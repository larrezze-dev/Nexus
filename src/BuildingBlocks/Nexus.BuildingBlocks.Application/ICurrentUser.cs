namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Identifies the caller of the current request. The concrete implementation backed by the
/// authenticated JWT arrives in step 0.5 (Nexus.BuildingBlocks.Web). Until then, this
/// abstraction exists so that <c>UnitOfWork</c> (step 0.3) can depend on it to set
/// <c>app.current_user_id</c> in the RLS session for every unit of work
/// (conventions-database.md §7).
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    bool IsAuthenticated { get; }
}
