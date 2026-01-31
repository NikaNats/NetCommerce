using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Domain.Orders;

namespace NetCommerce.Architecture.Tests;

/// <summary>
///     Tests for forbidden dependencies and patterns.
/// </summary>
public class ForbiddenDependencyTests
{
    private static readonly Assembly CatalogDomainAssembly = typeof(Product).Assembly;
    private static readonly Assembly OrderingDomainAssembly = typeof(Order).Assembly;
    private static readonly Assembly InventoryDomainAssembly = typeof(Stock).Assembly;

    private static readonly Assembly CatalogApplicationAssembly = typeof(CreateProductCommand).Assembly;
    private static readonly Assembly OrderingApplicationAssembly = typeof(CreateOrderCommand).Assembly;
    private static readonly Assembly InventoryApplicationAssembly = typeof(ReserveStockCommand).Assembly;

    private static readonly Assembly[] AllAssemblies =
    [
        CatalogDomainAssembly,
        CatalogApplicationAssembly,
        OrderingDomainAssembly,
        OrderingApplicationAssembly,
        InventoryDomainAssembly,
        InventoryApplicationAssembly
    ];

    #region No System.Data Dependencies in Domain

    [Fact]
    public void DomainLayers_ShouldNotDependOn_SystemData()
    {
        var domainAssemblies = new[]
        {
            CatalogDomainAssembly,
            OrderingDomainAssembly,
            InventoryDomainAssembly
        };

        foreach (var assembly in domainAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(
                    "System.Data.SqlClient",
                    "Npgsql",
                    "Microsoft.Data.SqlClient")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Domain layer in {assembly.GetName().Name} should not depend on data access libraries. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region No HTTP Client in Domain

    [Fact]
    public void DomainLayers_ShouldNotDependOn_HttpClient()
    {
        var domainAssemblies = new[]
        {
            CatalogDomainAssembly,
            OrderingDomainAssembly,
            InventoryDomainAssembly
        };

        foreach (var assembly in domainAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("System.Net.Http")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Domain layer in {assembly.GetName().Name} should not depend on HttpClient. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region No ASP.NET in Domain or Application

    [Fact]
    public void DomainAndApplicationLayers_ShouldNotDependOn_AspNetCore()
    {
        foreach (var assembly in AllAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("Microsoft.AspNetCore")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"{assembly.GetName().Name} should not depend on ASP.NET Core. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region No Logging Implementation in Domain

    [Fact]
    public void DomainLayers_ShouldNotDependOn_LoggingImplementations()
    {
        var domainAssemblies = new[]
        {
            CatalogDomainAssembly,
            OrderingDomainAssembly,
            InventoryDomainAssembly
        };

        foreach (var assembly in domainAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(
                    "Serilog",
                    "NLog",
                    "log4net")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Domain layer in {assembly.GetName().Name} should not depend on logging implementations. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region No Caching Libraries in Domain

    [Fact]
    public void DomainLayers_ShouldNotDependOn_CachingLibraries()
    {
        var domainAssemblies = new[]
        {
            CatalogDomainAssembly,
            OrderingDomainAssembly,
            InventoryDomainAssembly
        };

        foreach (var assembly in domainAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(
                    "StackExchange.Redis",
                    "Microsoft.Extensions.Caching")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Domain layer in {assembly.GetName().Name} should not depend on caching libraries. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region No JSON Serialization in Domain (except for DTOs)

    [Fact]
    public void DomainLayers_ShouldNotDependOn_JsonSerializers()
    {
        var domainAssemblies = new[]
        {
            CatalogDomainAssembly,
            OrderingDomainAssembly,
            InventoryDomainAssembly
        };

        foreach (var assembly in domainAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(
                    "Newtonsoft.Json",
                    "System.Text.Json")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Domain layer in {assembly.GetName().Name} should not depend on JSON serializers. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region No Messaging Libraries in Domain

    [Fact]
    public void DomainLayers_ShouldNotDependOn_MessagingLibraries()
    {
        var domainAssemblies = new[]
        {
            CatalogDomainAssembly,
            OrderingDomainAssembly,
            InventoryDomainAssembly
        };

        foreach (var assembly in domainAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(
                    "RabbitMQ.Client",
                    "Azure.Messaging",
                    "MassTransit")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Domain layer in {assembly.GetName().Name} should not depend on messaging libraries. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region Application Layer Should Only Use Abstractions

    [Fact]
    public void ApplicationLayers_ShouldNotDependOn_ConcreteDbContexts()
    {
        var applicationAssemblies = new[]
        {
            CatalogApplicationAssembly,
            OrderingApplicationAssembly,
            InventoryApplicationAssembly
        };

        foreach (var assembly in applicationAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(
                    "Microsoft.EntityFrameworkCore.DbContext",
                    "Npgsql.EntityFrameworkCore")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Application layer in {assembly.GetName().Name} should use repository abstractions, not DbContext. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region SharedKernel Namespace Ban

    [Fact]
    public void SharedKernel_Namespace_Should_Be_Forbidden_Globally()
    {
        var result = Types.InCurrentDomain()
            .That()
            .ResideInNamespace("NetCommerce")
            .Should()
            .NotHaveDependencyOnAny("NetCommerce.SharedKernel")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Architecture Violation: The legacy SharedKernel is deprecated. " +
            $"Please use NetCommerce.Kernel.* or NetCommerce.Domain.Shared. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    #endregion

    #region No MediatR Dependencies (Migration to Wolverine)

    /// <summary>
    ///     Ensures complete migration from MediatR to Wolverine.
    ///     MediatR should not be referenced in any assembly after migration.
    /// </summary>
    [Fact]
    public void AllAssemblies_ShouldNotDependOn_MediatR()
    {
        foreach (var assembly in AllAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(
                    "MediatR",
                    "MediatR.Contracts")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Assembly {assembly.GetName().Name} should not depend on MediatR. " +
                $"Complete migration to Wolverine is required. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    /// <summary>
    ///     Ensures no MediatR handler interfaces are used.
    ///     All handlers should use Wolverine conventions instead.
    /// </summary>
    [Fact]
    public void ApplicationHandlers_ShouldNotImplement_MediatRInterfaces()
    {
        var applicationAssemblies = new[]
        {
            CatalogApplicationAssembly,
            OrderingApplicationAssembly,
            InventoryApplicationAssembly
        };

        foreach (var assembly in applicationAssemblies)
        {
            // Check for IRequest, IRequestHandler, INotification, INotificationHandler
            var result = Types.InAssembly(assembly)
                .That()
                .HaveNameEndingWith("Handler")
                .Should()
                .NotHaveDependencyOn("MediatR")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Handlers in {assembly.GetName().Name} should not implement MediatR interfaces. " +
                $"Use Wolverine's conventional handlers instead. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion
}
