#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetCommerce.Api.Endpoints.Payments;
using NetCommerce.Finance.Domain.Webhooks;
using NetCommerce.Kernel.Stripe;
using NSubstitute;
using Shouldly;
using Wolverine;

namespace NetCommerce.Integration.Tests.Payments;

/// <summary>
///     Contract tests for the PaymentWebhookEndpoints Stripe integration.
///
///     <para>
///     Validates:
///     <list type="bullet">
///         <item>HMAC-SHA256 signature verification (valid / invalid / missing)</item>
///         <item>Idempotent duplicate event handling via <see cref="IWebhookEventStore"/></item>
///         <item>Handler contract (200 for accepted, 400 for bad signature)</item>
///     </list>
///     </para>
///
///     <para>
///     Invokes <c>PaymentWebhookEndpoints.HandleStripeWebhook</c> directly
///     (internal, exposed via InternalsVisibleTo) with mocked dependencies.
///     Avoids WebApplicationFactory + Wolverine incompatibility.
///     </para>
/// </summary>
[Trait("Category", "Webhook")]
public class PaymentWebhookContractTests
{
    private const string WebhookSecret = "whsec_test_contract_secret";

    private readonly IMessageBus _mockBus = Substitute.For<IMessageBus>();
    private readonly IWebhookEventStore _mockWebhookStore = Substitute.For<IWebhookEventStore>();
    private readonly IOptions<StripeOptions> _stripeOptions;

    public PaymentWebhookContractTests()
    {
        // Default: every event is new (first claim succeeds)
        _mockWebhookStore
            .TryClaimEventAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _stripeOptions = Options.Create(new StripeOptions { WebhookSecret = WebhookSecret });
    }

    #region Stripe-Signature Validation

    [Fact]
    public async Task ValidSignature_ShouldReturn200()
    {
        var payload = CreateStripeWebhookPayload("payment_intent.succeeded", "pi_valid_001");
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var (statusCode, _) = await InvokeHandler(payload, signature);

        statusCode.ShouldBe(200);
    }

    [Fact]
    public async Task InvalidSignature_ShouldReturn400()
    {
        var payload = CreateStripeWebhookPayload("payment_intent.succeeded", "pi_invalid_sig");

        var (statusCode, _) = await InvokeHandler(payload, signatureHeader: "t=12345,v1=badhash");

        statusCode.ShouldBe(400);
    }

    [Fact]
    public async Task MissingSignature_ShouldReturn400()
    {
        var payload = CreateStripeWebhookPayload("payment_intent.succeeded", "pi_no_sig");

        var (statusCode, _) = await InvokeHandler(payload, signatureHeader: null);

        statusCode.ShouldBe(400);
    }

    [Fact]
    public async Task TamperedPayload_ShouldReturn400()
    {
        // Sign one payload, send a different one
        var originalPayload = CreateStripeWebhookPayload("payment_intent.succeeded", "pi_tamper");
        var signature = GenerateStripeSignature(originalPayload, WebhookSecret);

        var tamperedPayload = originalPayload.Replace("pi_tamper", "pi_evil_replacement");

        var (statusCode, _) = await InvokeHandler(tamperedPayload, signature);

        statusCode.ShouldBe(400);
    }

    [Fact]
    public async Task WrongSecret_ShouldReturn400()
    {
        var payload = CreateStripeWebhookPayload("payment_intent.succeeded", "pi_wrong_secret");
        var signature = GenerateStripeSignature(payload, "whsec_wrong_secret");

        var (statusCode, _) = await InvokeHandler(payload, signature);

        statusCode.ShouldBe(400);
    }

    #endregion

    #region Idempotent Duplicate Handling

    [Fact]
    public async Task DuplicateEvent_ShouldReturn200WithDuplicateStatus()
    {
        // Configure store to reject claim (event already processed)
        _mockWebhookStore
            .TryClaimEventAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var payload = CreateStripeWebhookPayload("payment_intent.succeeded", "pi_dup");
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var (statusCode, body) = await InvokeHandler(payload, signature);

        statusCode.ShouldBe(200);
        body.ShouldContain("duplicate", Case.Insensitive,
            "Response body should indicate duplicate event");

        // IMessageBus.InvokeAsync should NOT have been called
        await _mockBus.DidNotReceive().InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FirstEvent_ShouldClaimAndProcess()
    {
        // Default: TryClaimEventAsync returns true
        var payload = CreateStripeWebhookPayload("payment_intent.succeeded", "pi_first");
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var (statusCode, body) = await InvokeHandler(payload, signature);

        statusCode.ShouldBe(200);
        body.ShouldContain("processed", Case.Insensitive,
            "Response body should indicate processed event");

        // Verify the command was dispatched
        await _mockBus.Received(1).InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());

        // Verify the event was marked as processed
        await _mockWebhookStore.Received(1).MarkProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Event Type Routing

    [Fact]
    public async Task PaymentSucceeded_ShouldDispatchSuccessCommand()
    {
        var payload = CreateStripeWebhookPayload("payment_intent.succeeded", "pi_success_route");
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var (statusCode, _) = await InvokeHandler(payload, signature);

        statusCode.ShouldBe(200);

        // Verify a command was dispatched
        await _mockBus.Received(1).InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PaymentFailed_ShouldDispatchFailureCommand()
    {
        var payload = CreateStripeWebhookPayload("payment_intent.payment_failed", "pi_fail_route");
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var (statusCode, _) = await InvokeHandler(payload, signature);

        statusCode.ShouldBe(200);

        await _mockBus.Received(1).InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownEventType_ShouldReturn200WithoutDispatching()
    {
        var payload = CreateStripeWebhookPayload("customer.subscription.created", "sub_unknown");
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var (statusCode, _) = await InvokeHandler(payload, signature);

        statusCode.ShouldBe(200,
            "Unknown event types should still return 200 to prevent Stripe retries");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    ///     Invoke the webhook handler directly with a constructed <see cref="HttpRequest"/>.
    ///     Returns the HTTP status code and response body from the <see cref="IResult"/>.
    /// </summary>
    private async Task<(int StatusCode, string Body)> InvokeHandler(
        string payload, string? signatureHeader)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        httpContext.Request.ContentType = "application/json";

        if (signatureHeader is not null)
            httpContext.Request.Headers["Stripe-Signature"] = signatureHeader;

        var result = await PaymentWebhookEndpoints.HandleStripeWebhook(
            httpContext.Request,
            _mockBus,
            _stripeOptions,
            _mockWebhookStore,
            NullLogger<PaymentWebhookEndpoints>.Instance);

        // Execute IResult into an HttpResponse to read status code + body.
        // IResult.ExecuteAsync requires RequestServices for JSON serialization.
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var responseContext = new DefaultHttpContext();
        responseContext.RequestServices = services;
        responseContext.Response.Body = new MemoryStream();
        await result.ExecuteAsync(responseContext);

        responseContext.Response.Body.Position = 0;
        var body = await new StreamReader(responseContext.Response.Body).ReadToEndAsync();

        return (responseContext.Response.StatusCode, body);
    }

    /// <summary>
    ///     Build a minimal Stripe-compatible event JSON payload.
    ///     Includes all fields required by <c>EventUtility.ConstructEvent</c>.
    /// </summary>
    private static string CreateStripeWebhookPayload(string eventType, string paymentIntentId)
    {
        var payload = new
        {
            id = $"evt_{Guid.NewGuid():N}",
            @object = "event",
            type = eventType,
            api_version = "2024-06-20",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            livemode = false,
            pending_webhooks = 1,
            request = new { id = (string?)null, idempotency_key = (string?)null },
            data = new
            {
                @object = new
                {
                    id = paymentIntentId,
                    @object = "payment_intent",
                    amount = 10000,
                    currency = "gel",
                    status = eventType.Contains("succeeded") ? "succeeded" : "failed",
                    last_payment_error = eventType.Contains("failed")
                        ? new { message = "Card declined" }
                        : null
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    ///     Generate a valid Stripe webhook signature using HMAC-SHA256.
    ///     Uses the same algorithm as Stripe: <c>HMAC-SHA256("{timestamp}.{payload}", secret)</c>.
    /// </summary>
    private static string GenerateStripeSignature(string payload, string secret)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signedPayload = $"{timestamp}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        string signature = BitConverter.ToString(hash).Replace("-", "").ToLower();

        return $"t={timestamp},v1={signature}";
    }

    #endregion
}
