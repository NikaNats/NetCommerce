#nullable enable
// =============================================================================
// SharedKernel Type Aliases - Backward Compatibility Layer
// =============================================================================
// This file provides type-forwarding to consolidate the split-brain architecture.
// All domain primitives now come from NetCommerce.Kernel.Core.Domain.
// All compliance types (PII, Audit) come from NetCommerce.Kernel.Compliance.
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
// =============================================================================
// Migration Notes
// =============================================================================
// Phase 1 (IN PROGRESS): PII/Audit types consolidated to Kernel.Compliance
//   - PiiVaultEntry → NetCommerce.Kernel.Compliance.Pii.PiiVaultEntry
//   - AuditEntry → NetCommerce.Kernel.Compliance.Audit.AuditEntry
//
// If you see compilation errors, update your using statements:
//   OLD: using NetCommerce.SharedKernel.Domain;
//   NEW: using NetCommerce.Kernel.Core.Domain;
//   NEW (PII/Audit): using NetCommerce.Kernel.Compliance.Pii;
//   NEW (PII/Audit): using NetCommerce.Kernel.Compliance.Audit;
//
// For IRepository<T,TId> and IUnitOfWork:
//   OLD: using NetCommerce.SharedKernel.Domain;
//   NEW: using NetCommerce.Kernel.Application;

