using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace NetCommerce.Architecture.Tests;

/// <summary>
/// Tests for forbidden dependencies and patterns.
/// </summary>
public class ForbiddenDependencyTests
{
    private static readonly Assembly CatalogDomainAssembly = typeof(Catalog.Domain.Products.Product).Assembly;
    private static readonly Assembly OrderingDomainAssembly = typeof(Ordering.Domain.Orders.Order).Assembly;
    private static readonly Assembly InventoryDomainAssembly = typeof(Inventory.Domain.Stock.Stock).Assembly;
    
    private static readonly Assembly CatalogApplicationAssembly = typeof(Catalog.Application.Products.Commands.CreateProductCommand).Assembly;
    private static readonly Assembly OrderingApplicationAssembly = typeof(Ordering.Application.Orders.Commands.CreateOrderCommand).Assembly;
    private static readonly Assembly InventoryApplicationAssembly = typeof(Inventory.Application.Stock.Commands.ReserveStockCommand).Assembly;

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
}
