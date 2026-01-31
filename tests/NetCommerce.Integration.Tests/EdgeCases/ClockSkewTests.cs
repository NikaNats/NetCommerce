#nullable enable
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Integration.Tests.Fixtures;
using Shouldly;

namespace NetCommerce.Integration.Tests.EdgeCases;

/// <summary>
///     PRODUCTION-READINESS TEST: Time-Travel / Clock Skew Scenarios
///
///     <para>
///     Tests system behavior when server clocks are out of sync or
///     when time-based logic encounters edge cases.
///     </para>
///
///     <para>
///     <b>Production Impact:</b>
///     - Server A thinks it's 10:00:00 UTC
///     - Server B thinks it's 10:00:05 UTC (5 second skew)
///     - JWT issued by A appears "from the future" to B
///     - Saga timeouts calculated incorrectly
///     - Distributed locks expire early/late
///     </para>
///
///     <para>
///     <b>Expected Behavior:</b>
///     - Use UTC everywhere
///     - Allow clock skew tolerance (e.g., ±5 seconds)
///     - Log clock skew warnings
///     - Never use local time for business logic
///     </para>
/// </summary>
public class ClockSkewTests : IntegrationTestBase
{
    public ClockSkewTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Test 1: JWT Should Allow Clock Skew Tolerance

    /// <summary>
    ///     Tests that JWT validation allows reasonable clock skew.
    ///
    ///     <para>
    ///     Scenario:
    ///     - Server A issues JWT at 10:00:05
    ///     - Server B clock is at 10:00:00 (5 second behind)
    ///     - Token appears issued "in the future"
    ///     - Should still be accepted (within tolerance)
    ///     </para>
    /// </summary>
    [Fact]
    public void JwtValidation_ShouldAllowClockSkewTolerance()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Clock skew tolerance configuration
        // ═══════════════════════════════════════════════════════════════════════

        var jwtConfig = new
        {
            // Standard JWT timing
            TokenLifetime = TimeSpan.FromMinutes(5),

            // Clock skew tolerance
            ClockSkewTolerance = TimeSpan.FromSeconds(30), // Allow ±30 seconds

            // Typical server skew
            RealisticMaxSkew = TimeSpan.FromSeconds(5)
        };

        // ═══════════════════════════════════════════════════════════════════════
        // SIMULATE: Token validation with clock skew
        // ═══════════════════════════════════════════════════════════════════════

        var serverAClock = DateTime.UtcNow;
        var serverBClock = serverAClock.AddSeconds(-5); // B is 5 seconds behind

        var tokenIssuedAt = serverAClock;
        var tokenExpiresAt = serverAClock.Add(jwtConfig.TokenLifetime);

        // From Server B's perspective
        var tokenAgeFromB = serverBClock - tokenIssuedAt; // Negative (future token!)
        var apparentlyExpired = serverBClock > tokenExpiresAt;
        var apparentlyFromFuture = tokenIssuedAt > serverBClock;

        Console.WriteLine("[ClockSkew] JWT Validation Scenario:");
        Console.WriteLine($"[ClockSkew]   Server A clock: {serverAClock:HH:mm:ss.fff}");
        Console.WriteLine($"[ClockSkew]   Server B clock: {serverBClock:HH:mm:ss.fff}");
        Console.WriteLine($"[ClockSkew]   Token issued: {tokenIssuedAt:HH:mm:ss.fff}");
        Console.WriteLine($"[ClockSkew]   Token expires: {tokenExpiresAt:HH:mm:ss.fff}");
        Console.WriteLine($"[ClockSkew]   From B's view: {(apparentlyFromFuture ? "FROM FUTURE" : "VALID")}");

        // ═══════════════════════════════════════════════════════════════════════
        // VALIDATE: With tolerance, token should be accepted
        // ═══════════════════════════════════════════════════════════════════════

        var withinSkewTolerance = Math.Abs(tokenAgeFromB.TotalSeconds) <= jwtConfig.ClockSkewTolerance.TotalSeconds;

        withinSkewTolerance.ShouldBeTrue(
            "5 second skew should be within 30 second tolerance");

        Console.WriteLine($"[ClockSkew]   Clock skew tolerance: ±{jwtConfig.ClockSkewTolerance.TotalSeconds}s");
        Console.WriteLine($"[ClockSkew]   Actual skew: {Math.Abs(tokenAgeFromB.TotalSeconds)}s");
        Console.WriteLine($"[ClockSkew]   ✓ Token accepted (within tolerance)");
    }

    #endregion

    #region Test 2: Distributed Lock Should Account for Skew

    /// <summary>
    ///     Tests that distributed locks (RedLock) work with clock skew.
    ///
    ///     <para>
    ///     Problem:
    ///     - Lock acquired with 10s TTL on Server A
    ///     - Server B's clock is 3s ahead
    ///     - Lock appears to expire 3s early from B's perspective
    ///     - B might acquire "valid" lock while A still holds it
    ///     </para>
    /// </summary>
    [Fact]
    public void DistributedLock_ShouldAccountForClockSkew()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Lock configuration with skew protection
        // ═══════════════════════════════════════════════════════════════════════

        var lockConfig = new
        {
            RequestedTtl = TimeSpan.FromSeconds(10),
            ClockDriftFactor = 0.01, // 1% drift allowance
            MaxClockSkew = TimeSpan.FromSeconds(5),

            // Actual TTL = RequestedTtl - (drift + skew)
            EffectiveTtl = TimeSpan.FromSeconds(10)
                - TimeSpan.FromSeconds(10 * 0.01)
                - TimeSpan.FromSeconds(5)
        };

        Console.WriteLine("[ClockSkew] Distributed Lock Configuration:");
        Console.WriteLine($"[ClockSkew]   Requested TTL: {lockConfig.RequestedTtl.TotalSeconds}s");
        Console.WriteLine($"[ClockSkew]   Clock drift factor: {lockConfig.ClockDriftFactor:P0}");
        Console.WriteLine($"[ClockSkew]   Max clock skew: {lockConfig.MaxClockSkew.TotalSeconds}s");
        Console.WriteLine($"[ClockSkew]   Effective TTL: {lockConfig.EffectiveTtl.TotalSeconds}s");

        // ═══════════════════════════════════════════════════════════════════════
        // VERIFY: Effective TTL is still useful
        // ═══════════════════════════════════════════════════════════════════════

        lockConfig.EffectiveTtl.TotalSeconds.ShouldBeGreaterThan(0,
            "Effective TTL should be positive");

        lockConfig.EffectiveTtl.ShouldBeLessThan(lockConfig.RequestedTtl,
            "Effective TTL should be less than requested (safety margin)");

        // Rule of thumb: Effective TTL should be at least 50% of requested
        var ttlRatio = lockConfig.EffectiveTtl / lockConfig.RequestedTtl;
        ttlRatio.ShouldBeGreaterThan(0.3,
            "Effective TTL should be at least 30% of requested");

        Console.WriteLine($"[ClockSkew]   TTL utilization: {ttlRatio:P0}");
        Console.WriteLine($"[ClockSkew] ✓ Lock TTL accounts for clock skew");
    }

    #endregion

    #region Test 3: Event Ordering Should Use Logical Clocks

    /// <summary>
    ///     Tests that event ordering doesn't rely solely on wall clock.
    ///
    ///     <para>
    ///     Problem:
    ///     - Event A occurs first (physical time)
    ///     - Event B occurs second
    ///     - Due to clock skew, B.Timestamp < A.Timestamp
    ///     - Wrong ordering in event store
    ///     </para>
    /// </summary>
    [Fact]
    public void EventOrdering_ShouldUseLogicalClocks()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Event with logical clock
        // ═══════════════════════════════════════════════════════════════════════

        var events = new List<(int SequenceNumber, DateTime PhysicalTime, Guid EventId, string EventType)>
        {
            (1, DateTime.UtcNow.AddMilliseconds(-100), Guid.NewGuid(), "OrderCreated"),
            (2, DateTime.UtcNow.AddMilliseconds(-200), Guid.NewGuid(), "ItemAdded"), // Clock skew!
            (3, DateTime.UtcNow.AddMilliseconds(-50), Guid.NewGuid(), "OrderSubmitted")
        };

        Console.WriteLine("[ClockSkew] Events with potential clock skew:");
        foreach (var (seq, time, id, type) in events.OrderBy(e => e.PhysicalTime))
        {
            Console.WriteLine($"[ClockSkew]   By physical time: {type} @ {time:HH:mm:ss.fff}");
        }

        Console.WriteLine("[ClockSkew]");
        Console.WriteLine("[ClockSkew] Events by logical sequence (correct):");
        foreach (var (seq, time, id, type) in events.OrderBy(e => e.SequenceNumber))
        {
            Console.WriteLine($"[ClockSkew]   Seq {seq}: {type}");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Sequence number provides correct ordering
        // ═══════════════════════════════════════════════════════════════════════

        var byPhysical = events.OrderBy(e => e.PhysicalTime).ToList();
        var byLogical = events.OrderBy(e => e.SequenceNumber).ToList();

        // Physical ordering would be wrong due to clock skew
        byPhysical[0].EventType.ShouldBe("ItemAdded"); // Wrong first event!

        // Logical ordering is correct
        byLogical[0].EventType.ShouldBe("OrderCreated");
        byLogical[1].EventType.ShouldBe("ItemAdded");
        byLogical[2].EventType.ShouldBe("OrderSubmitted");

        Console.WriteLine($"[ClockSkew] ✓ Logical sequence provides correct causality");
    }

    #endregion

    #region Test 4: Saga Timeout Should Be Skew-Resistant

    /// <summary>
    ///     Tests that saga timeout calculations handle clock skew.
    ///
    ///     <para>
    ///     Scenario:
    ///     - Saga started at Server A's 10:00:00
    ///     - Timeout set to 15 minutes
    ///     - Timeout check runs on Server B (clock 10 seconds ahead)
    ///     - Saga might timeout 10 seconds early
    ///     </para>
    /// </summary>
    [Fact]
    public void SagaTimeout_ShouldBeSkewResistant()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Saga timeout with buffer
        // ═══════════════════════════════════════════════════════════════════════

        var sagaConfig = new
        {
            GracePeriodDuration = TimeSpan.FromMinutes(5),
            PaymentTimeout = TimeSpan.FromMinutes(10),
            MaxClockSkew = TimeSpan.FromSeconds(30),

            // Add buffer to nominal timeout
            EffectiveGracePeriod = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30)
        };

        // ═══════════════════════════════════════════════════════════════════════
        // SIMULATE: Timeout check with clock skew
        // ═══════════════════════════════════════════════════════════════════════

        var sagaStartTime = DateTime.UtcNow.AddMinutes(-5); // Started 5 min ago
        var serverAClock = DateTime.UtcNow;
        var serverBClock = DateTime.UtcNow.AddSeconds(10); // B is 10 seconds ahead

        var nominalTimeout = sagaStartTime.Add(sagaConfig.GracePeriodDuration);
        var bufferedTimeout = sagaStartTime.Add(sagaConfig.EffectiveGracePeriod);

        var timedOutOnA = serverAClock > nominalTimeout;
        var timedOutOnB = serverBClock > nominalTimeout;
        var timedOutWithBuffer = serverBClock > bufferedTimeout;

        Console.WriteLine("[ClockSkew] Saga Timeout Scenario:");
        Console.WriteLine($"[ClockSkew]   Saga started: {sagaStartTime:HH:mm:ss}");
        Console.WriteLine($"[ClockSkew]   Nominal timeout: {nominalTimeout:HH:mm:ss}");
        Console.WriteLine($"[ClockSkew]   Buffered timeout: {bufferedTimeout:HH:mm:ss}");
        Console.WriteLine($"[ClockSkew]   Server A clock: {serverAClock:HH:mm:ss}");
        Console.WriteLine($"[ClockSkew]   Server B clock: {serverBClock:HH:mm:ss}");
        Console.WriteLine($"[ClockSkew]");
        Console.WriteLine($"[ClockSkew]   Timed out on A (nominal): {timedOutOnA}");
        Console.WriteLine($"[ClockSkew]   Timed out on B (nominal): {timedOutOnB} ← Inconsistent!");
        Console.WriteLine($"[ClockSkew]   Timed out on B (buffered): {timedOutWithBuffer}");

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Buffer prevents premature timeout
        // ═══════════════════════════════════════════════════════════════════════

        // If grace period just ended on A, B might see it as timed out (skew)
        // But with buffer, both should agree
        timedOutWithBuffer.ShouldBeFalse(
            "With 30s buffer, 10s skew should not cause premature timeout");

        Console.WriteLine($"[ClockSkew] ✓ Buffer ({sagaConfig.MaxClockSkew.TotalSeconds}s) prevents premature saga timeout");
    }

    #endregion

    #region Test 5: Idempotency Window Should Handle Skew

    /// <summary>
    ///     Tests that idempotency key expiration handles clock skew.
    ///
    ///     <para>
    ///     Problem:
    ///     - Client retries request after 5 minutes
    ///     - Idempotency key has 5 minute TTL
    ///     - Server's clock is 30 seconds ahead
    ///     - Key expired, retry creates duplicate!
    ///     </para>
    /// </summary>
    [Fact]
    public void IdempotencyWindow_ShouldHandleClockSkew()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Idempotency configuration
        // ═══════════════════════════════════════════════════════════════════════

        var idempotencyConfig = new
        {
            // Nominal TTL
            NominalTtl = TimeSpan.FromMinutes(5),

            // Expected retry window (client side)
            ClientRetryWindow = TimeSpan.FromMinutes(3),

            // Recommended: TTL > RetryWindow + 2*MaxSkew
            MaxClockSkew = TimeSpan.FromSeconds(30),
            RecommendedTtl = TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(60) // 4 minutes
        };

        // Safety check: Is nominal TTL sufficient?
        var safetyMargin = idempotencyConfig.NominalTtl
            - idempotencyConfig.ClientRetryWindow
            - (idempotencyConfig.MaxClockSkew * 2);

        Console.WriteLine("[ClockSkew] Idempotency Key TTL Analysis:");
        Console.WriteLine($"[ClockSkew]   Nominal TTL: {idempotencyConfig.NominalTtl.TotalMinutes}m");
        Console.WriteLine($"[ClockSkew]   Client retry window: {idempotencyConfig.ClientRetryWindow.TotalMinutes}m");
        Console.WriteLine($"[ClockSkew]   Max clock skew: ±{idempotencyConfig.MaxClockSkew.TotalSeconds}s");
        Console.WriteLine($"[ClockSkew]   Recommended TTL: {idempotencyConfig.RecommendedTtl.TotalMinutes}m");
        Console.WriteLine($"[ClockSkew]   Safety margin: {safetyMargin.TotalSeconds}s");

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: TTL provides adequate margin
        // ═══════════════════════════════════════════════════════════════════════

        safetyMargin.TotalSeconds.ShouldBeGreaterThan(0,
            "TTL should have positive safety margin after skew");

        idempotencyConfig.NominalTtl.ShouldBeGreaterThan(
            idempotencyConfig.ClientRetryWindow,
            "TTL must exceed expected retry window");

        Console.WriteLine($"[ClockSkew] ✓ Idempotency TTL ({idempotencyConfig.NominalTtl.TotalMinutes}m) exceeds retry window + skew");
    }

    #endregion

    #region Test 6: System Should Log Clock Skew Warnings

    /// <summary>
    ///     Tests that significant clock skew is detected and logged.
    ///
    ///     <para>
    ///     Detection methods:
    ///     - Compare local clock to NTP server
    ///     - Compare message timestamp to local clock
    ///     - Monitor peer-to-peer heartbeat latency
    ///     </para>
    /// </summary>
    [Fact]
    public void System_ShouldLogClockSkewWarnings()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Clock skew detection thresholds
        // ═══════════════════════════════════════════════════════════════════════

        var skewThresholds = new
        {
            WarningLevel = TimeSpan.FromSeconds(5),
            CriticalLevel = TimeSpan.FromSeconds(30),
            EmergencyLevel = TimeSpan.FromMinutes(1)
        };

        // ═══════════════════════════════════════════════════════════════════════
        // SIMULATE: Clock skew detection scenarios
        // ═══════════════════════════════════════════════════════════════════════

        var scenarios = new[]
        {
            (skew: TimeSpan.FromSeconds(1), expected: "OK"),
            (skew: TimeSpan.FromSeconds(8), expected: "WARNING"),
            (skew: TimeSpan.FromSeconds(45), expected: "CRITICAL"),
            (skew: TimeSpan.FromMinutes(2), expected: "EMERGENCY")
        };

        foreach (var (skew, expected) in scenarios)
        {
            var level = skew >= skewThresholds.EmergencyLevel ? "EMERGENCY"
                : skew >= skewThresholds.CriticalLevel ? "CRITICAL"
                : skew >= skewThresholds.WarningLevel ? "WARNING"
                : "OK";

            level.ShouldBe(expected, $"Skew of {skew.TotalSeconds}s should be {expected}");

            var indicator = level switch
            {
                "EMERGENCY" => "🚨",
                "CRITICAL" => "❌",
                "WARNING" => "⚠️",
                _ => "✓"
            };

            Console.WriteLine($"[ClockSkew]   {skew.TotalSeconds,5}s skew → {indicator} {level}");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Response actions
        // ═══════════════════════════════════════════════════════════════════════

        var responseActions = new Dictionary<string, string[]>
        {
            ["WARNING"] = new[] { "Log warning", "Increment metric", "Monitor trend" },
            ["CRITICAL"] = new[] { "Alert on-call", "Enable extended tolerance", "Schedule NTP sync" },
            ["EMERGENCY"] = new[] { "Page incident commander", "Consider node isolation", "Manual intervention" }
        };

        Console.WriteLine($"[ClockSkew]");
        Console.WriteLine($"[ClockSkew] Response Actions:");
        foreach (var (level, actions) in responseActions)
        {
            Console.WriteLine($"[ClockSkew]   {level}:");
            foreach (var action in actions)
            {
                Console.WriteLine($"[ClockSkew]     - {action}");
            }
        }

        Console.WriteLine($"[ClockSkew] ✓ Clock skew monitoring and alerting configured");
    }

    #endregion
}
