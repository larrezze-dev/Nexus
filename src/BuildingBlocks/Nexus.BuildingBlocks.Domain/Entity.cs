namespace Nexus.BuildingBlocks.Domain;

/// <summary>
/// Base class for entities with identity. Equality is based on <see cref="Id"/> (and the
/// concrete runtime type), never on reference identity: two instances with the same
/// <typeparamref name="TId"/> are equal even if they are different objects in memory.
/// </summary>
/// <typeparam name="TId">
/// The type of the entity's identifier. Left generic on purpose: most NEXUS aggregates use
/// <see cref="Guid"/> (UUIDv7, per ADR-0009), but this base type does not assume it.
/// </typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    protected Entity(TId id)
    {
        Id = id;
    }

    public TId Id { get; }

    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return GetType() == other.GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
