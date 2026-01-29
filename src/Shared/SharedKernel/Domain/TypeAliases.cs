#nullable enable
// =============================================================================
// SharedKernel Type Aliases - Backward Compatibility Layer
// =============================================================================
// This file provides type-forwarding to consolidate the split-brain architecture.
// All domain primitives now come from NetCommerce.Kernel.Core.Domain.
// This file maintains backward compatibility for existing code using SharedKernel.Domain namespace.
// =============================================================================

// Re-export core domain types with SharedKernel namespace for backward compatibility
global using KernelEntity = NetCommerce.Kernel.Core.Domain.Entity<System.Guid>;
global using KernelAggregateRoot = NetCommerce.Kernel.Core.Domain.AggregateRoot<System.Guid>;
global using KernelValueObject = NetCommerce.Kernel.Core.Domain.ValueObject;
global using KernelDomainEvent = NetCommerce.Kernel.Core.Domain.DomainEvent;

namespace NetCommerce.SharedKernel.Domain;

// =============================================================================
// Type Aliases for backward compatibility
// These inherit from Kernel.Core types to maintain the same behavior
// while allowing existing code to use SharedKernel.Domain namespace.
// =============================================================================

// Note: The original duplicate types (Entity<T>, AggregateRoot<T>, ValueObject,
// IDomainEvent, IEntity<T>, IAggregateRoot, IAggregateRoot<T>, IHasDomainEvents)
// have been removed. Code should use NetCommerce.Kernel.Core.Domain directly.
//
// If you see compilation errors, update your using statements:
//   OLD: using NetCommerce.SharedKernel.Domain;
//   NEW: using NetCommerce.Kernel.Core.Domain;
//
// For IRepository<T,TId> and IUnitOfWork:
//   OLD: using NetCommerce.SharedKernel.Domain;
//   NEW: using NetCommerce.Kernel.Application;
