#region

using NetCommerce.Kernel.Compliance.Audit;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Events;
using Wolverine;
using PiiVaultEntry = NetCommerce.Kernel.Compliance.Pii.PiiVaultEntry;
using AuditEntry = NetCommerce.Kernel.Compliance.Audit.AuditEntry;

#endregion

namespace NetCommerce.SharedKernel.Infrastructure.Handlers;

/// <summary>
///     2025 Elite Pattern: GDPR "Right to be Forgotten" handler.
///     This Wolverine handler implements the complete GDPR erasure flow:
///     1. Soft delete PII vault entry (instant anonymization)
///     2. Publish integration event to notify all modules
///     3. Audit log the deletion request
///     GDPR Compliance:
///     - Article 17: Right to erasure ("right to be forgotten")
///     - Article 30: Records of processing activities
///     - Article 33: Breach notification (if deletion fails)
///     Idempotency:
///     This handler is idempotent - can be retried safely.
///     If ProfileId is already deleted, the operation succeeds (no-op).
/// </summary>
public static class ForgetCustomerHandler
{
    /// <summary>
    ///     Handles the ForgetCustomerCommand using Wolverine message bus.
    ///     Wolverine Benefits:
    ///     - Automatic retries on transient failures
    ///     - Dead letter queue for permanent failures
    ///     - Distributed tracing with correlation IDs
    ///     - Transactional outbox (event publishing guaranteed)
    /// </summary>
    public static async Task<CustomerForgottenIntegrationEvent> Handle(
        ForgetCustomerCommand command,
        IPiiVaultRepository piiVaultRepository,
        NetCommerce.SharedKernel.Application.IAuditRepository auditRepository,
        IUserContext userContext,
        Envelope envelope,
        CancellationToken cancellationToken)
    {
        // Find the PII vault entry
        PiiVaultEntry? vaultEntry = await piiVaultRepository.FindByProfileIdAsync(
            command.ProfileId,
            cancellationToken);

        if (vaultEntry == null)
            // Idempotency: If already deleted, treat as success
            // This handles duplicate requests or message bus retries
            return new CustomerForgottenIntegrationEvent(
                command.ProfileId,
                DateTime.UtcNow,
                envelope.CorrelationId ?? Guid.NewGuid().ToString());

        if (vaultEntry.IsDeleted)
            // Already soft-deleted, treat as success
            return new CustomerForgottenIntegrationEvent(
                command.ProfileId,
                vaultEntry.DeletedAt ?? DateTime.UtcNow,
                envelope.CorrelationId ?? Guid.NewGuid().ToString());

        // Mark as deleted (soft delete)
        vaultEntry.MarkAsDeleted();

        // Update in repository
        await piiVaultRepository.UpdateAsync(vaultEntry, cancellationToken);

        // Create audit entry for compliance (GDPR Article 30)
        var auditEntry = AuditEntry.Create(
            command.RequestedByUserId,
            userContext.Role,
            "ForgetCustomer",
            command.ProfileId.ToString(),
            "PiiVault",
            $"{{\"reason\":\"{command.Reason}\",\"profileId\":\"{command.ProfileId}\"}}",
            envelope.CorrelationId ?? Guid.NewGuid().ToString(),
            userContext.IpAddress,
            userContext.UserAgent);

        await auditRepository.StoreAsync(auditEntry, cancellationToken);

        // Return integration event (Wolverine will publish via transactional outbox)
        // This notifies all modules to scrub cached PII
        return new CustomerForgottenIntegrationEvent(
            command.ProfileId,
            DateTime.UtcNow,
            envelope.CorrelationId ?? Guid.NewGuid().ToString());
    }
}

/// <summary>
///     Handler for purging soft-deleted PII entries after retention period.
///     This handler is invoked by a scheduled background job (Hangfire/Quartz).
///     It physically deletes PII entries that have been soft-deleted for longer
///     than the retention period (typically 90 days).
///     CRITICAL SAFETY:
///     This handler requires elevated database privileges (DELETE permission).
///     It should NOT be accessible from public APIs.
/// </summary>
public static class PurgeForgottenCustomersHandler
{
    public static async Task<PiiPurgedIntegrationEvent> Handle(
        PurgeForgottenCustomersCommand command,
        IPiiVaultRepository piiVaultRepository,
        NetCommerce.SharedKernel.Application.IAuditRepository auditRepository,
        Envelope envelope,
        CancellationToken cancellationToken)
    {
        // Purge entries that have been soft-deleted longer than retention period
        await piiVaultRepository.PurgeDeletedEntriesAsync(
            command.RetentionPeriod,
            cancellationToken);

        // Note: The repository handles the ProfileId collection and deletion.
        // We don't return the specific ProfileIds for security reasons (avoid exposing in logs).

        // Audit the purge operation
        var auditEntry = AuditEntry.Create(
            "system@netcommerce.com",
            "System",
            "PurgeForgottenCustomers",
            "PiiVault",
            "PiiVault",
            $"{{\"retentionPeriodDays\":{command.RetentionPeriod.TotalDays}}}",
            envelope.CorrelationId ?? Guid.NewGuid().ToString(),
            "127.0.0.1",
            "BackgroundJob");

        await auditRepository.StoreAsync(auditEntry, cancellationToken);

        // Return integration event for compliance reporting
        return new PiiPurgedIntegrationEvent(
            new List<Guid>(), // Don't expose ProfileIds in event (security)
            DateTime.UtcNow,
            envelope.CorrelationId ?? Guid.NewGuid().ToString());
    }
}

/// <summary>
///     Handler for rotating encryption keys for PII vault entries.
///     This handler is invoked by a scheduled background job for compliance
///     with key rotation policies (PCI-DSS, NIST SP 800-57).
///     Key Rotation Process:
///     1. Fetch batch of entries with old KeyVersion
///     2. For each entry:
///     a. Decrypt PII with old key
///     b. Encrypt PII with new key
///     c. Update KeyVersion field
///     3. Audit the rotation operation
/// </summary>
public static class RotatePiiEncryptionKeysHandler
{
    public static async Task Handle(
        RotatePiiEncryptionKeysCommand command,
        IPiiVaultRepository piiVaultRepository,
        IEncryptionService encryptionService,
        NetCommerce.SharedKernel.Application.IAuditRepository auditRepository,
        Envelope envelope,
        CancellationToken cancellationToken)
    {
        // Get entries that need key rotation (in batches)
        List<PiiVaultEntry> entriesToRotate = await piiVaultRepository.GetEntriesNeedingKeyRotationAsync(
            command.NewKeyVersion,
            command.BatchSize,
            cancellationToken);

        int rotatedCount = 0;

        foreach (PiiVaultEntry entry in entriesToRotate)
            try
            {
                // Decrypt with old key
                string fullName = await encryptionService.DecryptAsync(
                    EncryptedData.FromStorageFormat(entry.EncryptedFullName),
                    cancellationToken);

                string email = await encryptionService.DecryptAsync(
                    EncryptedData.FromStorageFormat(entry.EncryptedEmail),
                    cancellationToken);

                string phone = await encryptionService.DecryptAsync(
                    EncryptedData.FromStorageFormat(entry.EncryptedPhoneNumber),
                    cancellationToken);

                string address = await encryptionService.DecryptAsync(
                    EncryptedData.FromStorageFormat(entry.EncryptedAddress),
                    cancellationToken);

                string? dateOfBirth = null;
                if (!string.IsNullOrEmpty(entry.EncryptedDateOfBirth))
                    dateOfBirth = await encryptionService.DecryptAsync(
                        EncryptedData.FromStorageFormat(entry.EncryptedDateOfBirth),
                        cancellationToken);

                string? nationalId = null;
                if (!string.IsNullOrEmpty(entry.EncryptedNationalId))
                    nationalId = await encryptionService.DecryptAsync(
                        EncryptedData.FromStorageFormat(entry.EncryptedNationalId),
                        cancellationToken);

                // Encrypt with new key
                EncryptedData newFullName = await encryptionService.EncryptAsync(fullName, false, cancellationToken);
                EncryptedData newEmail = await encryptionService.EncryptAsync(email, true, cancellationToken);
                EncryptedData newPhone = await encryptionService.EncryptAsync(phone, true, cancellationToken);
                EncryptedData newAddress = await encryptionService.EncryptAsync(address, false, cancellationToken);

                string? newDateOfBirth = null;
                if (dateOfBirth != null)
                {
                    EncryptedData encrypted =
                        await encryptionService.EncryptAsync(dateOfBirth, false, cancellationToken);
                    newDateOfBirth = encrypted.ToStorageFormat();
                }

                string? newNationalId = null;
                if (nationalId != null)
                {
                    EncryptedData encrypted =
                        await encryptionService.EncryptAsync(nationalId, false, cancellationToken);
                    newNationalId = encrypted.ToStorageFormat();
                }

                // Update entry with new encrypted data and key version
                entry.ReEncrypt(
                    newFullName.ToStorageFormat(),
                    newEmail.ToStorageFormat(),
                    newPhone.ToStorageFormat(),
                    newAddress.ToStorageFormat(),
                    newDateOfBirth,
                    newNationalId,
                    command.NewKeyVersion);

                await piiVaultRepository.UpdateAsync(entry, cancellationToken);
                rotatedCount++;
            }
            catch (Exception ex)
            {
                // Log error but continue with next entry
                // In production, use structured logging (Serilog, OpenTelemetry)
                Console.WriteLine($"Failed to rotate key for ProfileId {entry.ProfileId}: {ex.Message}");
            }

        // Audit the key rotation operation
        var auditEntry = AuditEntry.Create(
            "system@netcommerce.com",
            "System",
            "RotatePiiEncryptionKeys",
            "PiiVault",
            "PiiVault",
            $"{{\"newKeyVersion\":{command.NewKeyVersion},\"rotatedCount\":{rotatedCount}}}",
            envelope.CorrelationId ?? Guid.NewGuid().ToString(),
            "127.0.0.1",
            "BackgroundJob");

        await auditRepository.StoreAsync(auditEntry, cancellationToken);
    }
}
