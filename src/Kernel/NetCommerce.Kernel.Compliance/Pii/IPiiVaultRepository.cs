#nullable enable
namespace NetCommerce.Kernel.Compliance.Pii;

/// <summary>
///     PII Vault Repository for confidential data isolation.
///     Provides CQRS-style access to the PII vault.
/// </summary>
/// <typeparam name="TPiiEntry">The concrete PII entry type.</typeparam>
public interface IPiiVaultRepository<TPiiEntry> where TPiiEntry : PiiVaultEntryBase
{
    /// <summary>
    ///     Finds a PII vault entry by ProfileId.
    ///     Records access timestamp for audit trail.
    /// </summary>
    Task<TPiiEntry?> FindByProfileIdAsync(
        Guid profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Finds PII vault entries by UserId (authentication ID).
    ///     One user can have multiple profiles.
    /// </summary>
    Task<List<TPiiEntry>> FindByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Adds a new PII vault entry.
    /// </summary>
    Task AddAsync(
        TPiiEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates an existing PII vault entry.
    /// </summary>
    Task UpdateAsync(
        TPiiEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks a PII vault entry as deleted (GDPR "Right to be Forgotten").
    /// </summary>
    Task DeleteAsync(
        Guid profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Hard deletes a PII vault entry (after retention period).
    /// </summary>
    Task HardDeleteAsync(
        Guid profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all entries that need key rotation.
    /// </summary>
    Task<List<TPiiEntry>> GetEntriesForKeyRotationAsync(
        int currentKeyVersion,
        int batchSize = 100,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Extension interface for searchable PII fields using blind indexes.
/// </summary>
public interface ISearchablePiiVaultRepository<TPiiEntry> : IPiiVaultRepository<TPiiEntry>
    where TPiiEntry : PiiVaultEntryBase
{
    /// <summary>
    ///     Searches for PII vault entries by blind index.
    ///     This enables searching without decrypting all entries.
    /// </summary>
    Task<List<TPiiEntry>> FindByBlindIndexAsync(
        string fieldName,
        string blindIndexValue,
        CancellationToken cancellationToken = default);
}
