#nullable enable

using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Api.Extensions;
using Shouldly;

namespace NetCommerce.Integration.Tests.Security;

[Trait("Category", "SecurityPenetration")]
public sealed class ForwardedHeadersAndRateLimitPenetrationTests
{
    [Fact]
    public void SpoofedForwardedForHeader_FromUntrustedNetwork_MustNotBypassRateLimitPartitioning()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        // 1. Simulate an attacker sending multiple requests with randomized X-Forwarded-For headers
        var realAttackerIp = IPAddress.Parse("198.51.100.45"); // Public Internet IP (Untrusted)

        var context1 = new DefaultHttpContext { RequestServices = sp };
        context1.Connection.RemoteIpAddress = realAttackerIp;
        context1.Request.Headers["X-Forwarded-For"] = "203.0.113.199"; // Spoofed IP 1

        var context2 = new DefaultHttpContext { RequestServices = sp };
        context2.Connection.RemoteIpAddress = realAttackerIp;
        context2.Request.Headers["X-Forwarded-For"] = "203.0.113.200"; // Spoofed IP 2

        // 2. Extract partition keys
        var key1 = context1.GetRateLimitPartitionKey();
        var key2 = context2.GetRateLimitPartitionKey();

        // 3. ASSERT: Partition key must bind to the physical RemoteIpAddress, NOT the spoofed header
        key1.ShouldBe($"ip:{realAttackerIp}");
        key2.ShouldBe($"ip:{realAttackerIp}");
        key1.ShouldBe(key2, "Attacker successfully bypassed rate limiting by manipulating X-Forwarded-For!");
    }

    [Fact]
    public void AuthStrictRateLimiter_MustBlockAttacker_OnSixthAttemptWithinOneMinute()
    {
        // Configure strict rate limiter (5 requests per minute)
        var options = new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        };

        using var limiter = new FixedWindowRateLimiter(options);

        // 1. First 5 attempts must succeed
        for (var i = 1; i <= 5; i++)
        {
            using var lease = limiter.AttemptAcquire();
            lease.IsAcquired.ShouldBeTrue($"Attempt {i} within quota was unexpectedly rejected.");
        }

        // 2. 6th attempt MUST be rejected with HTTP 429 semantics
        using var blockedLease = limiter.AttemptAcquire();
        blockedLease.IsAcquired.ShouldBeFalse("Rate limiter failed to block the 6th request exceeding the permit limit!");

        blockedLease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
    }
}
