using FluentAssertions;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Persistence.Repositories;
using NetCommerce.Kernel.Application;

namespace NetCommerce.Architecture.Tests;

/// <summary>
///     Structural contract for the repository hierarchy (distilled DDD:
///     narrowing must be honest).
/// </summary>
/// <remarks>
/// <para>
///     The basket pricing adapter depends on the narrow
///     <see cref="IReadOnlyRepository{T,TId}"/> view resolved over the catalog's
///     cached <see cref="IProductRepository"/>. That resolution is a runtime cast:
///     if the write interface ever stops being assignable to the read interface,
///     every basket pricing call fails. These tests pin the hierarchy so the
///     failure happens here, not in production.
/// </para>
/// </remarks>
public class RepositoryContractTests
{
    [Fact]
    public void WriteRepository_ShouldBeAssignableTo_ReadOnlyRepository()
    {
        typeof(IProductRepository)
            .IsAssignableTo(typeof(IReadOnlyRepository<Product, Guid>))
            .Should().BeTrue(
                "IRepository<T> must extend IReadOnlyRepository<T>; " +
                "resolving a read-only view over a read-write repository must be a safe upcast.");
    }

    [Fact]
    public void CachedProductRepository_ShouldExpose_ReadOnlyView()
    {
        typeof(CachedProductRepository)
            .IsAssignableTo(typeof(IReadOnlyRepository<Product, Guid>))
            .Should().BeTrue(
                "The cached decorator must satisfy the read-only contract " +
                "the basket ACL depends on.");
    }
}
