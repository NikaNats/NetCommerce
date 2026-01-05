#nullable enable

using NetCommerce.SharedKernel.Application;

namespace NetCommerce.SharedKernel.Events;

/// <summary>
///     2025 Elite Pattern: GDPR "Right to be Forgotten" command.
///
///     GDPR Article 17: The Right to Erasure ("Right to be Forgotten"):
///     Data subjects have the right to request deletion of their personal data when:
///     - The data is no longer necessary for the original purpose
///     - The data subject withdraws consent
///     - The data subject objects to processing
///     - The data was unlawfully processed
///     - Legal obligation requires erasure
///
///     Implementation Strategy:
///     1. Soft delete PII vault entry (mark IsDeleted = true)
///     2. Publish CustomerForgottenIntegrationEvent to all modules
///     3. Each module scrubs cached data (names in logs, cached addresses)
///     4. Financial records remain (Order totals, payment IDs) - anonymous
///     5. Background job purges soft-deleted data after audit period (90 days)
///
///     Why This Approach?
///     - Instant Anonymization: User data disappears immediately from UI
///     - Audit Compliance: Retain financial records for tax/fraud investigation
///     - Reversibility: Can restore if deletion was accidental (within 90 days)
///     - Simplicity: One DELETE instead of complex multi-table cascade
/// </summary>
public sealed record ForgetCustomerCommand(
    /// <summary>
    ///     The ProfileId (PII vault token) to forget.
    ///     This is NOT the UserId (authentication) - it's the PII profile reference.
    /// </summary>
    Guid ProfileId,

    /// <summary>
    ///     The UserId who requested the deletion (for audit trail).
    /// </summary>
    string RequestedByUserId,

    /// <summary>
    ///     Reason for deletion (GDPR requires documentation).
    ///     Examples: "User request", "Account closure", "Legal obligation"
    /// </summary>
    string Reason) : ICommand;

/// <summary>
///     Integration event published when a customer's PII is forgotten.
///
///     This event allows other bounded contexts to:
///     1. Scrub cached PII (names in Redis, addresses in ElasticSearch)
///     2. Anonymize display names ("Customer #12345" instead of "Alice")
///     3. Update audit logs to redact PII
///     4. Clear session data
///
///     Event-Driven Anonymization:
///     - Ordering Module: Replaces customer name with "Anonymous Customer"
///     - Payments Module: Clears billing address cache
///     - Shipping Module: Anonymizes delivery notes
///     - Media Module: No action (no PII stored)
///
///     Idempotency:
///     This event can be processed multiple times (message bus retry) safely.
///     Each module checks if ProfileId still exists before processing.
/// </summary>
public sealed record CustomerForgottenIntegrationEvent(
    /// <summary>
    ///     The ProfileId that was forgotten.
    /// </summary>
    Guid ProfileId,

    /// <summary>
    ///     When the forget request was processed.
    /// </summary>
    DateTime ForgottenAt,

    /// <summary>
    ///     Correlation ID for tracking the forget flow across modules.
    /// </summary>
    string CorrelationId);

/// <summary>
///     Command to purge soft-deleted PII entries after audit retention period.
///
///     This is executed by a background job (Hangfire/Quartz), not by user requests.
///
///     Process:
///     1. Find PII vault entries where IsDeleted = true AND DeletedAt < (now - 90 days)
///     2. For each entry: entry.PurgeData() (overwrites PII with random data)
///     3. Physically DELETE the row from pii_vault table
///
///     Audit Trail:
///     - Log each purge operation with ProfileId + timestamp
///     - Publish PiiPurgedIntegrationEvent for compliance reporting
///
///     CRITICAL SAFETY:
///     This command requires elevated privileges (database admin role).
///     It cannot be triggered by regular API requests.
/// </summary>
public sealed record PurgeForgottenCustomersCommand(
    /// <summary>
    ///     The retention period after soft delete before physical purge.
    ///     Typical values: 90 days (GDPR), 180 days (financial audit).
    /// </summary>
    TimeSpan RetentionPeriod) : ICommand;

/// <summary>
///     Integration event published when PII entries are physically purged.
///     Used for compliance reporting and audit logs.
/// </summary>
public sealed record PiiPurgedIntegrationEvent(
    /// <summary>
    ///     The ProfileIds that were purged.
    /// </summary>
    List<Guid> ProfileIds,

    /// <summary>
    ///     When the purge occurred.
    /// </summary>
    DateTime PurgedAt,

    /// <summary>
    ///     Correlation ID for audit trail.
    /// </summary>
    string CorrelationId);

/// <summary>
///     Command to rotate encryption keys for PII vault entries.
///
///     Key Rotation Strategy (NIST SP 800-57):
///     1. Generate new Master Key (KEK) in Azure Key Vault / AWS KMS
///     2. Mark old key as "Deactivated" but keep for decryption
///     3. Background job re-encrypts all PII with new key
///     4. Update KeyVersion field to track which key was used
///
///     Why Rotate Keys?
///     - Compliance: PCI-DSS requires annual key rotation
///     - Security: Limits blast radius if key is compromised
///     - Defense in Depth: Even if old key leaks, new data is safe
///
///     Process:
///     1. Fetch PII entry with old key
///     2. Decrypt with old DEK
///     3. Encrypt with new DEK (from new KEK)
///     4. Update KeyVersion field
///     5. Save to database
///
///     Performance:
///     Process in batches (100-1000 entries) to avoid memory exhaustion.
///     Run during low-traffic hours (scheduled maintenance window).
/// </summary>
public sealed record RotatePiiEncryptionKeysCommand(
    /// <summary>
    ///     The new key version to rotate to.
    /// </summary>
    int NewKeyVersion,

    /// <summary>
    ///     Batch size for processing (avoid memory exhaustion).
    /// </summary>
    int BatchSize = 100) : ICommand;
