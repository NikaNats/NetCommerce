using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Architecture.Tests;

/// <summary>
/// Architecture tests for naming conventions and coding standards.
/// </summary>
public class NamingConventionTests
{
    private static readonly Assembly CatalogDomainAssembly = typeof(Catalog.Domain.Products.Product).Assembly;
    private static readonly Assembly OrderingDomainAssembly = typeof(Ordering.Domain.Orders.Order).Assembly;
    private static readonly Assembly InventoryDomainAssembly = typeof(Inventory.Domain.Stock.Stock).Assembly;
    
    private static readonly Assembly CatalogApplicationAssembly = typeof(Catalog.Application.Products.Commands.CreateProductCommand).Assembly;
    private static readonly Assembly OrderingApplicationAssembly = typeof(Ordering.Application.Orders.Commands.CreateOrderCommand).Assembly;
    private static readonly Assembly InventoryApplicationAssembly = typeof(Inventory.Application.Stock.Commands.ReserveStockCommand).Assembly;

    private static readonly Assembly[] AllDomainAssemblies =
    [
        CatalogDomainAssembly,
        OrderingDomainAssembly,
        InventoryDomainAssembly
    ];

    private static readonly Assembly[] AllApplicationAssemblies =
    [
        CatalogApplicationAssembly,
        OrderingApplicationAssembly,
        InventoryApplicationAssembly
    ];

    #region Domain Event Naming

    [Fact]
    public void DomainEvents_ShouldEndWith_DomainEvent()
    {
        foreach (var assembly in AllDomainAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .ImplementInterface(typeof(IDomainEvent))
                .Should()
                .HaveNameEndingWith("DomainEvent")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"All domain events in {assembly.GetName().Name} should end with 'DomainEvent'. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region Repository Interface Naming

    [Fact]
    public void RepositoryInterfaces_ShouldStartWith_I_AndEndWith_Repository()
    {
        foreach (var assembly in AllDomainAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .HaveNameEndingWith("Repository")
                .And()
                .AreInterfaces()
                .Should()
                .HaveNameStartingWith("I")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Repository interfaces in {assembly.GetName().Name} should start with 'I'. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region Handler Naming (MediatR)

    [Fact]
    public void CommandHandlers_ShouldEndWith_Handler()
    {
        foreach (var assembly in AllApplicationAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .HaveNameEndingWith("CommandHandler")
                .Or()
                .HaveNameEndingWith("QueryHandler")
                .Should()
                .HaveNameEndingWith("Handler")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Handlers in {assembly.GetName().Name} should end with 'Handler'. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region Aggregate Roots

    [Fact]
    public void AggregateRoots_ShouldBeSealed()
    {
        foreach (var assembly in AllDomainAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .Inherit(typeof(AggregateRoot<>))
                .Should()
                .BeSealed()
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Aggregate roots in {assembly.GetName().Name} should be sealed. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region Value Objects

    [Fact]
    public void ValueObjects_ShouldBeSealed()
    {
        foreach (var assembly in AllDomainAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .Inherit(typeof(ValueObject))
                .Should()
                .BeSealed()
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Value objects in {assembly.GetName().Name} should be sealed. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region Interfaces Should Be Public

    [Fact]
    public void Interfaces_ShouldBePublic()
    {
        foreach (var assembly in AllDomainAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .AreInterfaces()
                .Should()
                .BePublic()
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"All interfaces in {assembly.GetName().Name} should be public. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion

    #region No Public Setters in Domain Entities

    [Fact]
    public void DomainEntities_ShouldNotHavePublicSetters()
    {
        foreach (var assembly in AllDomainAssemblies)
        {
            var entityTypes = Types.InAssembly(assembly)
                .That()
                .Inherit(typeof(Entity<>))
                .GetTypes();

            foreach (var entityType in entityTypes)
            {
                var publicSetters = entityType.GetProperties()
                    .Where(p => p.SetMethod?.IsPublic == true && 
                                p.Name != "Version" && // Allow Version for EF Core
                                !p.Name.StartsWith("Search")) // Allow computed properties
                    .Select(p => p.Name)
                    .ToList();

                publicSetters.Should().BeEmpty(
                    $"Entity '{entityType.Name}' should not have public setters. " +
                    $"Properties with public setters: {string.Join(", ", publicSetters)}");
            }
        }
    }

    #endregion

    #region Validators Naming

    [Fact]
    public void Validators_ShouldEndWith_Validator()
    {
        foreach (var assembly in AllApplicationAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .HaveNameEndingWith("Validator")
                .Should()
                .ResideInNamespaceContaining("Application")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Validators in {assembly.GetName().Name} should reside in Application namespace. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    #endregion
}
