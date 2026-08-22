namespace ShopHub.Shared.Kernel.Entities;

/// <summary>
/// Base for business entities. Identity is the <see cref="Id"/>, not reference equality,
/// so a tracked entity and a freshly-loaded one compare equal.
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity(Guid id) => Id = id;

    /// <summary>Parameterless constructor for EF Core materialisation.</summary>
    protected Entity()
    {
    }

    public Guid Id { get; protected set; }

    public bool Equals(Entity? other)
    {
        if (other is null || Id == Guid.Empty || other.Id == Guid.Empty)
        {
            return false;
        }

        return GetType() == other.GetType() && Id == other.Id;
    }

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
