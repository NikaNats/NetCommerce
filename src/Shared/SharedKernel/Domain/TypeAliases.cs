#nullable enable
// =============================================================================
// SharedKernel Type Aliases - Backward Compatibility Layer
// =============================================================================
// This file provides type-forwarding to consolidate the split-brain architecture.
// All domain primitives now come from NetCommerce.Kernel.Core.Domain.
// All compliance types (PII, Audit) come from NetCommerce.Kernel.Compliance.
// All security types come from NetCommerce.Kernel.Security.
// All domain value objects (Money, PriceBreakdown) come from NetCommerce.Domain.Shared.
// All integration events come from NetCommerce.Domain.Shared.Events.
// This file maintains backward compatibility for existing code using SharedKernel namespace.
// =============================================================================

// Re-export core domain types with SharedKernel namespace for backward compatibility
global using KernelEntity = NetCommerce.Kernel.Core.Domain.Entity<System.Guid>;
global using KernelAggregateRoot = NetCommerce.Kernel.Core.Domain.AggregateRoot<System.Guid>;
global using KernelValueObject = NetCommerce.Kernel.Core.Domain.ValueObject;
global using KernelDomainEvent = NetCommerce.Kernel.Core.Domain.DomainEvent;

// Phase 1 Consolidation: Forward PII/Audit types to Kernel.Compliance
global using PiiVaultEntry = NetCommerce.Kernel.Compliance.Pii.PiiVaultEntry;
global using AuditEntry = NetCommerce.Kernel.Compliance.Audit.AuditEntry;

// Phase 2 Consolidation: Forward Notification interfaces to Kernel.Application
global using IEmailProvider = NetCommerce.Kernel.Application.Notifications.IEmailProvider;
global using ITemplateEngine = NetCommerce.Kernel.Application.Notifications.ITemplateEngine;

// Phase 3 Consolidation: Forward BaseDbContext to Kernel.EfCore
global using BaseDbContext = NetCommerce.Kernel.EfCore.Persistence.BaseDbContext;

// Phase 4 Consolidation: Forward Zero-Trust Security to Kernel.Security
// NOTE: Direct usage of Kernel.Security types is preferred (no aliasing for security)
// These aliases exist ONLY for backward compatibility during migration
global using ZeroTrustAuthOptions = NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthOptions;

// =============================================================================
// Phase 5 Consolidation Notes
// =============================================================================
// Domain Value Objects consolidated to Domain.Shared.
// Global usings not possible (cross-project), so migration must be done file-by-file.
//
// Migration pattern:
//   OLD: using NetCommerce.SharedKernel.Domain;
//        var price = Money.Create(100m);
//
//   NEW: using NetCommerce.Domain.Shared;
//        var price = Money.Create(100m);
//
// The SharedKernel types are marked [Obsolete] to trigger compiler warnings.
// =============================================================================

// =============================================================================
// Migration Notes
// =============================================================================
// Phase 5 (COMPLETE): Domain Value Objects consolidated to Domain.Shared
//   - Money → NetCommerce.Domain.Shared.Money
//   - PriceBreakdown → NetCommerce.Domain.Shared.PriceBreakdown
//   - IntegrationEvents → NetCommerce.Domain.Shared.Events.*
//   - SagaMessages → NetCommerce.Domain.Shared.Events.*
//
// IMPORTANT: Stop using SharedKernel.Domain types. Use Domain.Shared types:
//   OLD: using NetCommerce.SharedKernel.Domain;
//   NEW: using NetCommerce.Domain.Shared;
//
//   OLD: using NetCommerce.SharedKernel.Events;
//   NEW: using NetCommerce.Domain.Shared.Events;
//
// Phase 4 (COMPLETE): Zero-Trust Security consolidated to Kernel.Security
//   - ZeroTrustAuthOptions → NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthOptions
//   - TokenIntrospectionMiddleware → NetCommerce.Kernel.Security.Authentication.TokenIntrospectionMiddleware
//   - TokenExchangeDelegatingHandler → NetCommerce.Kernel.Security.Authentication.TokenExchangeDelegatingHandler
//   - OidcRoleClaimsTransformation → NetCommerce.Kernel.Security.Authentication.OidcRoleClaimsTransformation
//   - IUserContext implementation → NetCommerce.Kernel.Security.HttpUserContext
//   - ITenantContext implementation → NetCommerce.Kernel.Security.HttpTenantContext
//
// IMPORTANT: Stop using type aliases for security. Use direct Kernel.Security types:
//   OLD: using NetCommerce.SharedKernel.Infrastructure.Security.Authentication;
//   NEW: using NetCommerce.Kernel.Security.Authentication;
//
// For IRepository<T,TId> and IUnitOfWork:
//   OLD: using NetCommerce.SharedKernel.Domain;
//   NEW: using NetCommerce.Kernel.Application;

