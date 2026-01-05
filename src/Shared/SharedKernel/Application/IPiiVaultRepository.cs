#region

using NetCommerce.SharedKernel.Domain;

#endregion

namespace NetCommerce.SharedKernel.Application;

/// <summary>
///     2025 Elite Pattern: PII Vault Repository for confidential data isolation.
///     This repository provides CQRS-style access to the PII vault:
///     - Commands: Create, Update, Delete (GDPR forget)
///     - Queries: Find by ProfileId, Search by blind index
///     Security Properties:
///     1. All queries log access for GDPR Article 15 compliance
///     2. Searches use blind indexes (never decrypt entire table)
///     3. Soft delete by default (hard delete after audit period)
///     4. Row-level security (only authenticated services can access)
/// </summary>
public interface IPiiVaultRepository
{
    /// <summary>
    ///     Finds a PII vault entry by ProfileId.
    ///     Security:
    ///     - Records access timestamp for audit trail
    ///     - Returns null if entry is soft-deleted (IsDeleted = true)
    ///     - Logs access for GDPR compliance
    /// </summary>
    Task<PiiVaultEntry?> FindByProfileIdAsync(
        Guid profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Finds a PII vault entry by UserId (authentication ID).
    ///     One user can have multiple profiles (home address, work address).
    /// </summary>
    Task<List<PiiVaultEntry>> FindByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Searches for PII vault entries by email blind index.
    ///     This enables "Find orders by email" without decrypting all emails.
    ///     Process:
    ///     1. Compute blind index: HMAC-SHA256(email + salt)
    ///     2. Query: WHERE email_blind_index = computed_hash
    ///     3. Return matching profiles (O(1) lookup)
    /// </summary>
    Task<List<PiiVaultEntry>> FindByEmailBlindIndexAsync(
        string emailBlindIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Searches for PII vault entries by phone blind index.
    /// </summary>
    Task<List<PiiVaultEntry>> FindByPhoneBlindIndexAsync(
        string phoneBlindIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Adds a new PII vault entry.
    /// </summary>
    Task AddAsync(
        PiiVaultEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates an existing PII vault entry.
    /// </summary>
    Task UpdateAsync(
        PiiVaultEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks a PII vault entry as deleted (GDPR "Right to be Forgotten").
    ///     This is a soft delete - data remains for audit period.
    /// </summary>
    Task DeleteAsync(
        Guid profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Physically deletes PII vault entries that have been soft-deleted
    ///     longer than the audit retention period.
    ///     CRITICAL: This is called by a background job, not user requests.
    ///     Process:
    ///     1. Find entries where IsDeleted = true AND DeletedAt
    ///     < (now - retentionPeriod)
    ///         2. Call entry.PurgeData() to overwrite PII with random data
    ///         3. Physically delete row from database
    /// </summary>
    Task PurgeDeletedEntriesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all PII vault entries that need key rotation.
    ///     Returns entries where KeyVersion
    ///     < currentKeyVersion.
    ///         Use Case:
    ///         Background job periodically rotates encryption keys for compliance.
    ///         This query finds entries encrypted with old keys.
    /// </summary>
    Task<List<PiiVaultEntry>> GetEntriesNeedingKeyRotationAsync(
        int currentKeyVersion,
        int batchSize = 100,
        CancellationToken cancellationToken = default);
}
