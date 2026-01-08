namespace NetCommerce.Kernel.Core.Domain;

/// <summary>
///     Interface for auditable entities.
/// </summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime? ModifiedAt { get; set; }
}