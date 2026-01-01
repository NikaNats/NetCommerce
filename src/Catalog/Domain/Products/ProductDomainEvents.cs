using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Catalog.Domain.Products;

// Domain Events for Product aggregate

public sealed record ProductCreatedDomainEvent(
    Guid ProductId,
    string Name,
    string Sku) : DomainEvent;

public sealed record ProductUpdatedDomainEvent(
    Guid ProductId,
    string Name) : DomainEvent;

public sealed record ProductPriceChangedDomainEvent(
    Guid ProductId,
    Money OldPrice,
    Money NewPrice) : DomainEvent;

public sealed record ProductPublishedDomainEvent(
    Guid ProductId) : DomainEvent;

public sealed record ProductArchivedDomainEvent(
    Guid ProductId) : DomainEvent;