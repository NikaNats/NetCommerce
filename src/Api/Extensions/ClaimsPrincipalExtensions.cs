#nullable enable
using System.Security.Claims;

namespace NetCommerce.Api.Extensions;

/// <summary>
///     Single owner for "who is the caller" claim extraction (pragmatic DRY).
/// </summary>
/// <remarks>
/// <para>
///     The Keycloak deployment backing this API omits <c>sub</c> from access
///     tokens (see <c>SessionHandlers</c>), so callers are identified by the
///     <c>user_id</c> protocol mapper, with legacy <c>NameIdentifier</c> last.
///     This precedence lived copy-pasted in every endpoint with DIVERGENT
///     fallback chains — order reads/cancels dropped <c>user_id</c>, so a user
///     authenticating through the documented flow could create an order but got
///     <c>403</c> reading or cancelling it. All endpoints now derive from here.
/// </para>
/// <para>
///     Ordering needs a <see cref="Guid"/> customer id; basket keys are opaque
///     strings and additionally accept <c>preferred_username</c>. Both shapes
///     share <see cref="TryGetSubjectId"/> and differ only afterwards.
/// </para>
/// </remarks>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    ///     Canonical subject: OIDC <c>sub</c>, Keycloak <c>user_id</c>, legacy
    ///     <c>NameIdentifier</c>. Null when the token carries none of them.
    /// </summary>
    public static string? TryGetSubjectId(this ClaimsPrincipal user)
    {
        return user.FindFirst("sub")?.Value
            ?? user.FindFirst("user_id")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    ///     Subject parsed as the ordering <see cref="Guid"/> customer id.
    ///     False when absent or not a Guid — callers fail closed (Forbid).
    /// </summary>
    public static bool TryGetCustomerId(this ClaimsPrincipal user, out Guid customerId)
    {
        var subject = user.TryGetSubjectId();
        return Guid.TryParse(subject, out customerId);
    }
}
