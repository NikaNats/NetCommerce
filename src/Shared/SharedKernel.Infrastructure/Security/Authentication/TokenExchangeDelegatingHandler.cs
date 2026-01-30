// =============================================================================
// DEPRECATED: This file is kept for backward compatibility only.
// Use NetCommerce.Kernel.Security.Authentication types instead.
// =============================================================================

// Re-export types from Kernel.Security via global using aliases in TypeAliases.cs
// This file exists only to show a deprecation warning to consumers.

namespace NetCommerce.SharedKernel.Infrastructure.Security.Authentication;

/// <summary>
///     DEPRECATED: Use NetCommerce.Kernel.Security.Authentication.TokenExchangeDelegatingHandler instead.
///     The types are re-exported via type aliases in TypeAliases.cs.
/// </summary>
[Obsolete("Use NetCommerce.Kernel.Security.Authentication.TokenExchangeDelegatingHandler instead. " +
          "This namespace is deprecated and will be removed in a future version.")]
public static class TokenExchangeDeprecatedWarning
{
    public const string Message = "Use NetCommerce.Kernel.Security.Authentication types instead.";
}
