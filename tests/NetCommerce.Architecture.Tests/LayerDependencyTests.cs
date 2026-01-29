using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Architecture.Tests;

/// <summary>
///     Architecture tests ensuring clean architecture principles are followed.
///     Uses NetArchTest.Rules for structural validation.
/// </summary>
public class LayerDependencyTests
{
    // Assembly references for each module
    private static readonly Assembly CatalogDomainAssembly = typeof(Product).Assembly;
    private static readonly Assembly CatalogApplicationAssembly = typeof(CreateProductCommand).Assembly;
    private static readonly Assembly CatalogInfrastructureAssembly = typeof(CatalogModule).Assembly;

    private static readonly Assembly OrderingDomainAssembly = typeof(Order).Assembly;
    private static readonly Assembly OrderingApplicationAssembly = typeof(CreateOrderCommand).Assembly;
    private static readonly Assembly OrderingInfrastructureAssembly = typeof(OrderingModule).Assembly;

    private static readonly Assembly InventoryDomainAssembly = typeof(Stock).Assembly;
    private static readonly Assembly InventoryApplicationAssembly = typeof(ReserveStockCommand).Assembly;
    private static readonly Assembly InventoryInfrastructureAssembly = typeof(InventoryModule).Assembly;

    private static readonly Assembly SharedKernelAssembly = typeof(Entity<>).Assembly;

    #region SharedKernel Tests

    [Fact]
    public void SharedKernel_ShouldNotDependOn_AnyModule()
    {
        var result = Types.InAssembly(SharedKernelAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "NetCommerce.Catalog",
                "NetCommerce.Ordering",
                "NetCommerce.Inventory",
                "NetCommerce.Payments",
                "NetCommerce.Basket",
                "NetCommerce.Media")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "SharedKernel should not depend on any module. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    #endregion

    #region Domain Layer Tests

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_Application()
    {
        var result = Types.InAssembly(CatalogDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Catalog.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer should not depend on Application layer. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(CatalogDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Catalog.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer should not depend on Infrastructure layer. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void OrderingDomain_ShouldNotDependOn_Application()
    {
        var result = Types.InAssembly(OrderingDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Ordering.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer should not depend on Application layer. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void OrderingDomain_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(OrderingDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Ordering.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer should not depend on Infrastructure layer. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void InventoryDomain_ShouldNotDependOn_Application()
    {
        var result = Types.InAssembly(InventoryDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Inventory.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer should not depend on Application layer. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void InventoryDomain_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(InventoryDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Inventory.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer should not depend on Infrastructure layer. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    #endregion

    #region Application Layer Tests

    [Fact]
    public void CatalogApplication_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(CatalogApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Catalog.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Application layer should not depend on Infrastructure layer. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void OrderingApplication_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(OrderingApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Ordering.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Application layer should not depend on Infrastructure layer. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void InventoryApplication_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(InventoryApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Inventory.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Application layer should not depend on Infrastructure layer. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    #endregion

    #region Module Isolation Tests (Modular Monolith Boundaries)

    [Fact]
    public void CatalogModule_ShouldNotDependOn_OrderingDomain()
    {
        var result = Types.InAssembly(CatalogDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Ordering.Domain")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Catalog module should not directly depend on Ordering domain. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void CatalogModule_ShouldNotDependOn_InventoryDomain()
    {
        var result = Types.InAssembly(CatalogDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Inventory.Domain")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Catalog module should not directly depend on Inventory domain. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void OrderingModule_ShouldNotDependOn_CatalogDomain()
    {
        var result = Types.InAssembly(OrderingDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Catalog.Domain")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Ordering module should not directly depend on Catalog domain. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void InventoryModule_ShouldNotDependOn_CatalogDomain()
    {
        var result = Types.InAssembly(InventoryDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Catalog.Domain")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Inventory module should not directly depend on Catalog domain. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    #endregion

    #region No Entity Framework in Domain

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_EntityFramework()
    {
        var result = Types.InAssembly(CatalogDomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer should not depend on Entity Framework. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void OrderingDomain_ShouldNotDependOn_EntityFramework()
    {
        var result = Types.InAssembly(OrderingDomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer should not depend on Entity Framework. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void InventoryDomain_ShouldNotDependOn_EntityFramework()
    {
        var result = Types.InAssembly(InventoryDomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer should not depend on Entity Framework. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    #endregion
}
