#nullable enable
using System.ComponentModel.DataAnnotations;

namespace NetCommerce.Kernel.Security.Authorization;

/// <summary>
///     Security posture for admin elevated authorization.
///     Determines which authentication factors are required.
/// </summary>
public enum AdminElevatedSecurityMode
{
    /// <summary>
    ///     BOTH a valid API key AND a fresh auth_time are required.
    ///     This is the production default. No fallback, no silent degradation.
    /// </summary>
    Strict = 0,

    /// <summary>
    ///     A valid API key OR a fresh auth_time is sufficient.
    ///     Use only when API key distribution to all operators is impractical.
    ///     The API key must still be configured — an unconfigured key
    ///     results in denial regardless of mode.
    /// </summary>
    Flexible = 1,

    /// <summary>
    ///     Development/testing only. Allows auth_time alone.
    ///     Every elevated access grant is logged at WARNING level.
    ///     Startup validation rejects this mode in Production/Staging.
    /// </summary>
    DevelopmentOnly = 2
}

/// <summary>
///     Configuration for admin elevated authorization.
///     Replaces the previous <c>AdminApiKeyOptions</c> with explicit
///     security posture to eliminate silent bypass paths.
/// </summary>
public sealed class AdminElevatedAuthOptions
{
    public const string SectionName = "Auth:AdminElevated";

    /// <summary>
    ///     The shared API key for elevated admin operations.
    ///     MUST be set in all non-development environments.
    ///     An empty or null value causes ALL elevated requests to be denied.
    /// </summary>
    [MinLength(32, ErrorMessage = "AdminElevated ApiKey must be at least 32 characters.")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    ///     Maximum age of the auth_time claim in minutes.
    ///     If the admin's authentication is older than this,
    ///     step-up re-authentication is required.
    /// </summary>
    [Range(1, 60, ErrorMessage = "MaxAuthAgeMinutes must be between 1 and 60.")]
    public int MaxAuthAgeMinutes { get; set; } = 15;

    /// <summary>
    ///     The security posture. Defaults to <see cref="AdminElevatedSecurityMode.Strict"/>.
    ///     Must be explicitly set to any other value in configuration.
    /// </summary>
    public AdminElevatedSecurityMode SecurityMode { get; set; } = AdminElevatedSecurityMode.Strict;

    /// <summary>
    ///     Returns true when the API key is configured (non-empty, meets minimum length).
    ///     When false, ALL elevated requests are denied regardless of SecurityMode.
    /// </summary>
    public bool IsApiKeyConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && ApiKey.Length >= 32;
}
