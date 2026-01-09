#nullable enable
namespace NetCommerce.Kernel.Core.Domain;

/// <summary>
/// Defines the minimum contract for an entity with a typed identifier.
/// </summary>
public interface IEntity<out TId> where TId : notnull
{
    TId Id { get; }
}

/// <summary>
/// Marker interface to identify Aggregate Roots within the domain.
/// This no longer requires a base class.
/// </summary>
public interface IAggregateRoot : IHasDomainEvents
{
    // Optional: Add versioning for concurrency if needed globally
    // uint Version { get; }
}

/// <summary>
/// Combined interface for repositories to use as a constraint.
/// </summary>
public interface IAggregateRoot<out TId> : IEntity<TId>, IAggregateRoot
    where TId : notnull
{
}
