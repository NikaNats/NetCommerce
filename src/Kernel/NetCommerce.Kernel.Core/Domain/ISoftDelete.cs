#nullable enable
namespace NetCommerce.Kernel.Core.Domain;

/// <summary>
///     Marker interface for soft-deletable entities.
///     Enables global query filters for soft-deleted records.
/// </summary>
public interface ISoftDelete
{
    /// <summary>
    ///     Timestamp when the entity was soft-deleted.
    ///     Null = not deleted.
    /// </summary>
    DateTime? DeletedAt { get; set; }

    /// <summary>
    ///     User ID who soft-deleted this entity.
    /// </summary>
    string? DeletedBy { get; set; }

    /// <summary>
    ///     Marks this entity as deleted.
    /// </summary>
    void SoftDelete(string deletedBy);

    /// <summary>
    ///     Restores a soft-deleted entity.
    /// </summary>
    void Restore();
}
