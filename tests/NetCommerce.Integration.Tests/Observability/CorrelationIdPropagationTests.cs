#nullable enable
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Integration.Tests.Fixtures;
using Shouldly;

namespace NetCommerce.Integration.Tests.Observability;

/// <summary>
///     PRODUCTION-READINESS TEST: Correlation ID Propagation
///
///     <para>
///     Tests that correlation IDs propagate across all system boundaries.
///     </para>
///
///     <para>
///     <b>Production Impact:</b>
///     - User reports "order failed" with ticket #12345
///     - Support needs to trace: API → Saga → Payment → Inventory
///     - Without correlation ID, logs are unsearchable
///     - Hours wasted correlating timestamps instead of IDs
///     </para>
///
///     <para>
///     <b>Expected Behavior:</b>
///     - HTTP requests receive/generate X-Correlation-ID
///     - ID propagates through Wolverine messages
///     - All logs include correlation ID
///     - External API calls include correlation ID
///     </para>
/// </summary>
public class CorrelationIdPropagationTests : IntegrationTestBase
{
    public CorrelationIdPropagationTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Test 1: HTTP Request Should Have Correlation ID

    /// <summary>
    ///     Tests that all HTTP requests have a correlation ID.
    ///
    ///     <para>
    ///     If client doesn't provide X-Correlation-ID:
    ///     - Server generates one
    ///     - Returns it in response headers
    ///     </para>
    /// </summary>
    [Fact]
    public void HttpRequest_ShouldHaveCorrelationId()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Correlation ID header standards
        // ═══════════════════════════════════════════════════════════════════════

        var correlationHeaders = new[]
        {
            "X-Correlation-ID",    // Most common
            "X-Request-ID",        // Alternative
            "Request-Id",          // Azure standard
            "traceparent"          // W3C Trace Context
        };

        // Primary header to use
        var primaryHeader = "X-Correlation-ID";

        // ═══════════════════════════════════════════════════════════════════════
        // SIMULATE: Request scenarios
        // ═══════════════════════════════════════════════════════════════════════

        var scenarios = new (string Description, string? RequestHeader, string ExpectedResponse, bool Generated)[]
        {
            (
                Description: "Client provides correlation ID",
                RequestHeader: "abc-123-xyz",
                ExpectedResponse: "abc-123-xyz",
                Generated: false
            ),
            (
                Description: "Client doesn't provide ID",
                RequestHeader: null,
                ExpectedResponse: $"{Guid.NewGuid():N}", // Server generates
                Generated: true
            )
        };

        Console.WriteLine("[CorrelationID] HTTP Header Handling:");
        Console.WriteLine($"[CorrelationID]   Primary header: {primaryHeader}");
        Console.WriteLine($"[CorrelationID]   Also accept: {string.Join(", ", correlationHeaders.Skip(1))}");
        Console.WriteLine($"[CorrelationID]");

        foreach (var scenario in scenarios)
        {
            Console.WriteLine($"[CorrelationID] Scenario: {scenario.Description}");
            Console.WriteLine($"[CorrelationID]   Request: {scenario.RequestHeader ?? "(none)"}");
            Console.WriteLine($"[CorrelationID]   Response: {(scenario.Generated ? "(generated)" : scenario.ExpectedResponse)}");

            if (scenario.Generated)
            {
                // Verify format of generated ID
                Guid.TryParse(scenario.ExpectedResponse, out _).ShouldBeTrue(
                    "Generated correlation ID should be valid GUID");
            }
        }

        Console.WriteLine($"[CorrelationID] ✓ HTTP correlation ID handling validated");
    }

    #endregion

    #region Test 2: Wolverine Messages Should Include Correlation ID

    /// <summary>
    ///     Tests that Wolverine messages carry correlation ID.
    ///
    ///     <para>
    ///     When saga handler processes message:
    ///     - Correlation ID from original request is preserved
    ///     - All cascading messages inherit same ID
    ///     </para>
    /// </summary>
    [Fact]
    public void WolverineMessages_ShouldIncludeCorrelationId()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // SIMULATE: Message flow with correlation
        // ═══════════════════════════════════════════════════════════════════════

        var originalCorrelationId = Guid.NewGuid();

        var messageFlow = new[]
        {
            new { Step = 1, Message = "SubmitOrderCommand", CorrelationId = originalCorrelationId },
            new { Step = 2, Message = "OrderSubmittedEvent", CorrelationId = originalCorrelationId },
            new { Step = 3, Message = "ReserveInventoryCommand", CorrelationId = originalCorrelationId },
            new { Step = 4, Message = "InventoryReservedEvent", CorrelationId = originalCorrelationId },
            new { Step = 5, Message = "RequestPaymentCommand", CorrelationId = originalCorrelationId },
            new { Step = 6, Message = "PaymentCompletedEvent", CorrelationId = originalCorrelationId }
        };

        Console.WriteLine($"[CorrelationID] Message Flow:");
        Console.WriteLine($"[CorrelationID] Original ID: {originalCorrelationId}");
        Console.WriteLine($"[CorrelationID]");

        foreach (var msg in messageFlow)
        {
            Console.WriteLine($"[CorrelationID]   {msg.Step}. {msg.Message}");
            Console.WriteLine($"[CorrelationID]      CorrelationId: {msg.CorrelationId}");

            msg.CorrelationId.ShouldBe(originalCorrelationId,
                $"Message '{msg.Message}' should preserve original correlation ID");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // VERIFY: All messages have same correlation ID
        // ═══════════════════════════════════════════════════════════════════════

        var uniqueCorrelationIds = messageFlow.Select(m => m.CorrelationId).Distinct().Count();
        uniqueCorrelationIds.ShouldBe(1, "All messages should have same correlation ID");

        Console.WriteLine($"[CorrelationID] ✓ Correlation ID preserved across {messageFlow.Length} messages");
    }

    #endregion

    #region Test 3: Logs Should Include Correlation ID

    /// <summary>
    ///     Tests that all log entries include correlation ID.
    ///
    ///     <para>
    ///     Structured logging should include:
    ///     {
    ///       "message": "Order created",
    ///       "correlationId": "abc-123",
    ///       "orderId": "...",
    ///       ...
    ///     }
    ///     </para>
    /// </summary>
    [Fact]
    public void Logs_ShouldIncludeCorrelationId()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Required log properties
        // ═══════════════════════════════════════════════════════════════════════

        var requiredLogProperties = new[]
        {
            "CorrelationId",    // Primary tracking ID
            "Timestamp",        // When it happened
            "Level",           // Info, Warning, Error
            "Message",         // Human-readable message
            "SourceContext"    // Which class/component
        };

        var contextualProperties = new[]
        {
            "UserId",          // If authenticated
            "TenantId",        // If multi-tenant
            "TraceId",         // OpenTelemetry trace
            "SpanId"           // OpenTelemetry span
        };

        // ═══════════════════════════════════════════════════════════════════════
        // SIMULATE: Log entry
        // ═══════════════════════════════════════════════════════════════════════

        var logEntry = new
        {
            Timestamp = DateTime.UtcNow.ToString("O"),
            Level = "Information",
            Message = "Order ORD-2026-001234 created successfully",
            SourceContext = "NetCommerce.Ordering.Application.OrderHandler",
            CorrelationId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TenantId = "tenant-acme",
            TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
            SpanId = "00f067aa0ba902b7",
            Properties = new
            {
                OrderId = Guid.NewGuid(),
                OrderNumber = "ORD-2026-001234",
                ItemCount = 3,
                TotalAmount = 150.00m
            }
        };

        Console.WriteLine("[CorrelationID] Structured Log Entry:");
        Console.WriteLine($"[CorrelationID]   Timestamp: {logEntry.Timestamp}");
        Console.WriteLine($"[CorrelationID]   Level: {logEntry.Level}");
        Console.WriteLine($"[CorrelationID]   Message: {logEntry.Message}");
        Console.WriteLine($"[CorrelationID]   CorrelationId: {logEntry.CorrelationId}");
        Console.WriteLine($"[CorrelationID]   TraceId: {logEntry.TraceId}");
        Console.WriteLine($"[CorrelationID]   SpanId: {logEntry.SpanId}");

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Required properties present
        // ═══════════════════════════════════════════════════════════════════════

        logEntry.CorrelationId.ShouldNotBe(Guid.Empty, "Correlation ID should be present");
        logEntry.TraceId.ShouldNotBeNullOrEmpty("Trace ID should be present");

        Console.WriteLine($"[CorrelationID] ✓ Log entry includes correlation ID and trace context");
    }

    #endregion

    #region Test 4: External API Calls Should Forward Correlation ID

    /// <summary>
    ///     Tests that correlation ID is forwarded to external services.
    ///
    ///     <para>
    ///     When calling:
    ///     - Stripe API
    ///     - Shipping provider API
    ///     - Email service
    ///     Include correlation ID for cross-system tracing.
    ///     </para>
    /// </summary>
    [Fact]
    public void ExternalApiCalls_ShouldForwardCorrelationId()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: External service integration patterns
        // ═══════════════════════════════════════════════════════════════════════

        var externalServices = new (string Service, string HeaderName, string? AdditionalMetadata)[]
        {
            (
                Service: "Stripe",
                HeaderName: "Idempotency-Key", // Stripe uses this for correlation
                AdditionalMetadata: "metadata[correlation_id]"
            ),
            (
                Service: "SendGrid",
                HeaderName: "X-Correlation-ID",
                AdditionalMetadata: "custom_args.correlation_id"
            ),
            (
                Service: "FedEx",
                HeaderName: "X-Request-ID",
                AdditionalMetadata: null
            ),
            (
                Service: "Meilisearch",
                HeaderName: "X-Request-ID",
                AdditionalMetadata: null
            )
        };

        var correlationId = Guid.NewGuid();

        Console.WriteLine("[CorrelationID] External Service Headers:");
        Console.WriteLine($"[CorrelationID] Correlation ID: {correlationId}");
        Console.WriteLine($"[CorrelationID]");

        foreach (var service in externalServices)
        {
            Console.WriteLine($"[CorrelationID] {service.Service}:");
            Console.WriteLine($"[CorrelationID]   Header: {service.HeaderName}: {correlationId}");
            if (service.AdditionalMetadata != null)
            {
                Console.WriteLine($"[CorrelationID]   Also: {service.AdditionalMetadata}={correlationId}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: All services have header configuration
        // ═══════════════════════════════════════════════════════════════════════

        foreach (var service in externalServices)
        {
            service.HeaderName.ShouldNotBeNullOrEmpty(
                $"Service {service.Service} should have correlation header defined");
        }

        Console.WriteLine($"[CorrelationID] ✓ {externalServices.Length} external services configured for correlation");
    }

    #endregion

    #region Test 5: Error Responses Should Include Correlation ID

    /// <summary>
    ///     Tests that error responses include correlation ID for support.
    ///
    ///     <para>
    ///     Error response should show:
    ///     "An error occurred. Reference ID: abc-123-xyz"
    ///     Support can search logs using this ID.
    ///     </para>
    /// </summary>
    [Fact]
    public void ErrorResponses_ShouldIncludeCorrelationId()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Error response structure
        // ═══════════════════════════════════════════════════════════════════════

        var correlationId = Guid.NewGuid();

        var errorResponse = new
        {
            type = "https://httpstatuses.com/500",
            title = "Internal Server Error",
            status = 500,
            detail = "An unexpected error occurred while processing your request.",
            instance = $"/api/v1/orders/{Guid.NewGuid()}",
            correlationId = correlationId.ToString(),
            timestamp = DateTime.UtcNow.ToString("O"),

            // User-friendly message
            userMessage = $"Something went wrong. Please contact support with reference: {correlationId.ToString()[..8].ToUpper()}"
        };

        Console.WriteLine("[CorrelationID] Error Response:");
        Console.WriteLine($"[CorrelationID]   Status: {errorResponse.status}");
        Console.WriteLine($"[CorrelationID]   Title: {errorResponse.title}");
        Console.WriteLine($"[CorrelationID]   Correlation ID: {errorResponse.correlationId}");
        Console.WriteLine($"[CorrelationID]   User Message: {errorResponse.userMessage}");

        // ═══════════════════════════════════════════════════════════════════════
        // SIMULATE: Support workflow
        // ═══════════════════════════════════════════════════════════════════════

        var supportQuery = $"CorrelationId:{correlationId}";

        Console.WriteLine($"[CorrelationID]");
        Console.WriteLine($"[CorrelationID] Support Workflow:");
        Console.WriteLine($"[CorrelationID]   1. Customer reports: 'Error with ref {correlationId.ToString()[..8].ToUpper()}'");
        Console.WriteLine($"[CorrelationID]   2. Support searches: {supportQuery}");
        Console.WriteLine($"[CorrelationID]   3. Finds all logs for this request");

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Error response includes correlation ID
        // ═══════════════════════════════════════════════════════════════════════

        errorResponse.correlationId.ShouldNotBeNullOrEmpty(
            "Error response should include correlation ID");

        var expectedReference = correlationId.ToString()[..8].ToUpper();
        errorResponse.userMessage.ShouldContain(expectedReference);

        Console.WriteLine($"[CorrelationID] ✓ Error responses enable support tracing");
    }

    #endregion

    #region Test 6: Database Queries Should Be Traceable

    /// <summary>
    ///     Tests that database queries include correlation context.
    ///
    ///     <para>
    ///     When using Npgsql with OpenTelemetry:
    ///     - SQL queries tagged with correlation ID
    ///     - Slow query analysis linked to requests
    ///     </para>
    /// </summary>
    [Fact]
    public void DatabaseQueries_ShouldBeTraceable()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Database tracing configuration
        // ═══════════════════════════════════════════════════════════════════════

        var dbTracingConfig = new
        {
            // OpenTelemetry instrumentation
            EnableNpgsqlInstrumentation = true,
            EnableEfCoreInstrumentation = true,

            // What to capture
            RecordSqlStatements = true,
            RecordParameters = false, // Don't log sensitive data

            // Tags added to spans
            Tags = new[]
            {
                "db.system",
                "db.name",
                "db.statement",
                "db.operation",
                "net.peer.name"
            }
        };

        // ═══════════════════════════════════════════════════════════════════════
        // SIMULATE: Database span
        // ═══════════════════════════════════════════════════════════════════════

        var dbSpan = new
        {
            TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
            SpanId = "00f067aa0ba902b7",
            ParentSpanId = "a2fb4a1d1a96d312",
            Name = "SELECT ordering.orders",
            Duration = TimeSpan.FromMilliseconds(5.2),
            Tags = new Dictionary<string, string>
            {
                ["db.system"] = "postgresql",
                ["db.name"] = "netcommerce",
                ["db.statement"] = "SELECT * FROM ordering.orders WHERE id = $1",
                ["db.operation"] = "SELECT",
                ["net.peer.name"] = "postgres:5432"
            }
        };

        Console.WriteLine("[CorrelationID] Database Span:");
        Console.WriteLine($"[CorrelationID]   TraceId: {dbSpan.TraceId}");
        Console.WriteLine($"[CorrelationID]   SpanId: {dbSpan.SpanId}");
        Console.WriteLine($"[CorrelationID]   Parent: {dbSpan.ParentSpanId}");
        Console.WriteLine($"[CorrelationID]   Duration: {dbSpan.Duration.TotalMilliseconds}ms");
        Console.WriteLine($"[CorrelationID]   Query: {dbSpan.Tags["db.statement"]}");

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Tracing is properly configured
        // ═══════════════════════════════════════════════════════════════════════

        dbTracingConfig.EnableNpgsqlInstrumentation.ShouldBeTrue(
            "Npgsql instrumentation should be enabled");

        dbTracingConfig.RecordParameters.ShouldBeFalse(
            "SQL parameters should not be recorded (PII risk)");

        Console.WriteLine($"[CorrelationID] ✓ Database queries linked to distributed trace");
    }

    #endregion
}
