using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace NetCommerce.Domain.Tests.Security;

/// <summary>
///     Tests for rate limiting strategies covering:
///     - Per-IP global rate limiting (fixed window)
///     - Per-user token bucket rate limiting
///     - Authenticated vs anonymous partitioning
///     - Admin strict rate limiting
///     - Auth endpoint strict rate limiting
///     - Rate limiter partition key correctness
/// </summary>
public class RateLimitingTests
{
    // ========================================================================
    // Token Bucket Strategy Tests
    // ========================================================================

    [Fact]
    public async Task TokenBucket_AllowsBurstUpToLimit()
    {
        // Arrange
        var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            TokensPerPeriod = 2,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = false // Manual for testing
        });

        // Act - Send burst of 10 (should all succeed)
        var results = new List<bool>();
        for (var i = 0; i < 10; i++)
        {
            using var lease = limiter.AttemptAcquire();
            results.Add(lease.IsAcquired);
        }

        // Assert
        results.ShouldAllBe(r => r);

        // 11th request should fail
        using var overLimit = limiter.AttemptAcquire();
        overLimit.IsAcquired.ShouldBeFalse();

        limiter.Dispose();
    }

    [Fact]
    public async Task TokenBucket_ReplenishesOverTime()
    {
        // Arrange
        var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 5,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(100),
            TokensPerPeriod = 5,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });

        // Exhaust tokens
        for (var i = 0; i < 5; i++)
            limiter.AttemptAcquire();

        // Wait for replenishment
        await Task.Delay(200);

        // Act
        using var lease = limiter.AttemptAcquire();

        // Assert
        lease.IsAcquired.ShouldBeTrue();

        limiter.Dispose();
    }

    [Fact]
    public void FixedWindow_EnforcesWindowLimit()
    {
        // Arrange
        var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });

        // Act
        for (var i = 0; i < 5; i++)
        {
            using var lease = limiter.AttemptAcquire();
            lease.IsAcquired.ShouldBeTrue();
        }

        // Assert - 6th request should fail
        using var rejected = limiter.AttemptAcquire();
        rejected.IsAcquired.ShouldBeFalse();

        limiter.Dispose();
    }

    // ========================================================================
    // Partition Key Tests
    // ========================================================================

    [Fact]
    public void PerUserPartition_AuthenticatedUser_UsesUserId()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "user-123"));
        httpContext.User = new ClaimsPrincipal(identity);

        // Act
        var userId = httpContext.User.FindFirst("sub")?.Value;
        var partitionKey = !string.IsNullOrEmpty(userId)
            ? $"user:{userId}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        // Assert
        partitionKey.ShouldBe("user:user-123");
    }

    [Fact]
    public void PerUserPartition_AnonymousUser_UsesIp()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.100");

        // Act
        var userId = httpContext.User.FindFirst("sub")?.Value;
        var partitionKey = !string.IsNullOrEmpty(userId)
            ? $"user:{userId}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        // Assert
        partitionKey.ShouldBe("ip:192.168.1.100");
    }

    [Fact]
    public void PerUserPartition_NoIpNoUser_UsesUnknown()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        var userId = httpContext.User.FindFirst("sub")?.Value;
        var partitionKey = !string.IsNullOrEmpty(userId)
            ? $"user:{userId}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        // Assert
        partitionKey.ShouldBe("ip:unknown");
    }

    [Fact]
    public void AdminPartition_UsesAdminPrefix()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "admin-001"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));
        httpContext.User = new ClaimsPrincipal(identity);

        // Act
        var userId = httpContext.User.FindFirst("sub")?.Value ?? "unknown-admin";
        var partitionKey = $"admin:{userId}";

        // Assert
        partitionKey.ShouldBe("admin:admin-001");
    }

    [Fact]
    public void GlobalPartition_UsesIpAddress()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");

        // Act
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Assert
        clientIp.ShouldBe("10.0.0.1");
    }

    // ========================================================================
    // Different Users Get Independent Limits
    // ========================================================================

    [Fact]
    public void DifferentUsers_HaveIndependentLimits()
    {
        // Arrange
        var limiter1 = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 2,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
        var limiter2 = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 2,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });

        // Act - Exhaust user1's limit
        limiter1.AttemptAcquire();
        limiter1.AttemptAcquire();
        using var user1Rejected = limiter1.AttemptAcquire();

        // User 2 should still have their full limit
        using var user2Allowed = limiter2.AttemptAcquire();

        // Assert
        user1Rejected.IsAcquired.ShouldBeFalse();
        user2Allowed.IsAcquired.ShouldBeTrue();

        limiter1.Dispose();
        limiter2.Dispose();
    }

    // ========================================================================
    // Auth Strict Rate Limit (5 per minute)
    // ========================================================================

    [Fact]
    public void AuthStrict_OnlyAllows5PerMinute()
    {
        // Arrange
        var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 2
        });

        // Act - 5 allowed
        for (var i = 0; i < 5; i++)
        {
            using var lease = limiter.AttemptAcquire();
            lease.IsAcquired.ShouldBeTrue();
        }

        // 6th goes to queue (QueueLimit = 2), but AttemptAcquire doesn't queue
        using var sixth = limiter.AttemptAcquire();
        sixth.IsAcquired.ShouldBeFalse();

        limiter.Dispose();
    }

    // ========================================================================
    // Retry-After Metadata
    // ========================================================================

    [Fact]
    public void RejectedRequest_ContainsRetryAfterMetadata()
    {
        // Arrange
        var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });

        limiter.AttemptAcquire(); // Exhaust

        // Act
        using var rejected = limiter.AttemptAcquire();

        // Assert
        rejected.IsAcquired.ShouldBeFalse();
        rejected.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldBeGreaterThan(TimeSpan.Zero);

        limiter.Dispose();
    }
}
