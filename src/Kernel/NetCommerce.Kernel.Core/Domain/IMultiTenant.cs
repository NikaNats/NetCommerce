#nullable enable
namespace NetCommerce.Kernel.Core.Domain;

/// <summary>
///     Marker interface for Multi-Tenant entities.
///     Enables automatic data isolation via Global Query Filters.
/// </summary>
public interface IMultiTenant
{
    /// <summary>
    ///     The unique identifier of the tenant (Customer/Organization).
    ///     Should be indexed in the database.
    /// </summary>
    string TenantId { get; set; }
}
