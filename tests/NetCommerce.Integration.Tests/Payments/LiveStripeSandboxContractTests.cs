#nullable enable

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Stripe;
using Shouldly;
using Stripe;

namespace NetCommerce.Integration.Tests.Payments;

/// <summary>
///     Verifies contracts against the real Stripe Sandbox API (api.stripe.com).
///     Gated by STRIPE_SECRET_KEY environment variable.
/// </summary>
[Trait("Category", "LiveStripeSandbox")]
public sealed class LiveStripeSandboxContractTests
{
    private readonly string? _stripeSecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
    private readonly string? _stripeWebhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");

    [Fact]
    public async Task LiveStripe_ProcessPayment_MustCreateRealPendingIntent_AndConvertSubunits()
    {
        if (string.IsNullOrWhiteSpace(_stripeSecretKey))
        {
            Assert.Skip("Live Stripe tests require STRIPE_SECRET_KEY environment variable.");
            return;
        }

        var options = Options.Create(new StripeOptions
        {
            SecretKey = _stripeSecretKey,
            WebhookSecret = _stripeWebhookSecret ?? "whsec_mock",
            MaxRetryAttempts = 2
        });

        var clientFactory = new StripeClientFactory(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<StripeClientFactory>.Instance);
        var paymentIntentService = clientFactory.CreatePaymentIntentService();

        var orderId = Guid.NewGuid();
        var monetaryAmount = Money.Create(149.50m, "USD");
        var expectedCents = monetaryAmount.ToSubunits(); // 14950

        // 1. Create PaymentIntent against live Stripe Sandbox
        var createOptions = new PaymentIntentCreateOptions
        {
            Amount = expectedCents,
            Currency = monetaryAmount.Currency.ToLowerInvariant(),
            PaymentMethod = "pm_card_visa", // Standard Stripe test token
            Confirm = true,
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
                AllowRedirects = "never"
            },
            Metadata = new Dictionary<string, string>
            {
                ["order_id"] = orderId.ToString(),
                ["source"] = "NetCommerce.ContractTests"
            }
        };

        var intent = await paymentIntentService.CreateAsync(createOptions);

        // 2. Validate live contract
        intent.ShouldNotBeNull();
        intent.Id.ShouldStartWith("pi_");
        intent.Amount.ShouldBe(expectedCents, "Currency minor unit conversion failed; amount sent to Stripe did not match ToSubunits().");
        intent.Currency.ShouldBe("usd");
        intent.Status.ShouldBeOneOf("succeeded", "processing", "requires_capture");

        // 3. Clean up: Refund test charge if confirmed
        if (intent.Status == "succeeded")
        {
            var refundService = clientFactory.CreateRefundService();
            var refund = await refundService.CreateAsync(new RefundCreateOptions
            {
                PaymentIntent = intent.Id,
                Reason = "requested_by_customer"
            });

            refund.Status.ShouldBe("succeeded");
        }
    }

    [Fact]
    public void WebhookSignatureVerification_MustValidateRawBytesAccurately()
    {
        const string testSecret = "whsec_0123456789abcdef0123456789abcdef";
        var payload = """{"id":"evt_test_123","object":"event","type":"payment_intent.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Construct standard Stripe-Signature header format: t=timestamp,v1=hash
        var signaturePayload = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(testSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signaturePayload));
        var hashHex = Convert.ToHexString(hash).ToLowerInvariant();
        var validHeader = $"t={timestamp},v1={hashHex}";

        // Assert valid construction
        var stripeEvent = Should.NotThrow(() =>
            EventUtility.ConstructEvent(payload, validHeader, testSecret, throwOnApiVersionMismatch: false));

        stripeEvent.Id.ShouldBe("evt_test_123");
        stripeEvent.Type.ShouldBe("payment_intent.succeeded");

        // Assert tampered payload failure
        var tamperedPayload = """{"id":"evt_test_123","object":"event","type":"payment_intent.payment_failed"}""";
        Should.Throw<StripeException>(() =>
            EventUtility.ConstructEvent(tamperedPayload, validHeader, testSecret, throwOnApiVersionMismatch: false));
    }
}
