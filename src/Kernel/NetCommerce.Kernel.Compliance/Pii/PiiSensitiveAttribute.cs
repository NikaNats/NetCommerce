#nullable enable
namespace NetCommerce.Kernel.Compliance.Pii;

/// <summary>
///     Marks a string property as containing PII that should be automatically encrypted.
///     The PiiEncryptionConverter will be applied during OnModelCreating.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class PiiSensitiveAttribute : Attribute
{
    /// <summary>
    ///     Whether to use deterministic encryption (enables equality searches).
    ///     Default: false (probabilistic encryption for maximum security).
    /// </summary>
    public bool IsDeterministic { get; set; } = false;

    /// <summary>
    ///     Optional blind index column name for searchable encrypted fields.
    ///     If specified, a BlindIndex property will be auto-configured.
    /// </summary>
    public string? BlindIndexColumnName { get; set; }
}
