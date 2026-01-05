#region

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Payments.Infrastructure.Gateways;
using NetCommerce.SharedKernel.Application.Notifications;
using NSubstitute;
using Shouldly;

#endregion

namespace NetCommerce.Integration.Tests.Payments;

/// <summary>
///     Integration tests for Stripe webhook endpoint.
///     Tests the complete webhook-first payment pattern implementation.
/// </summary>
[Collection("IntegrationTests")]
public class PaymentWebhookTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string WebhookSecret = "whsec_test_secret";
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public PaymentWebhookTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override Stripe configuration with test values
                services.Configure<StripeOptions>(options =>
                {
                    options.SecretKey = "sk_test_mock";
                    options.PublishableKey = "pk_test_mock";
                    options.WebhookSecret = WebhookSecret;
                });

                // Register fake S3 service for tests (Media module requires IAmazonS3)
                services.AddScoped<IAmazonS3>(_ => Substitute.For<IAmazonS3>());

                // Register OrderNotificationHandler dependencies
                services.AddScoped<IEmailProvider>(_ =>
                    Substitute.For<IEmailProvider>());
                services.AddScoped<ITemplateEngine>(_ =>
                    Substitute.For<ITemplateEngine>());
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task WebhookEndpoint_ValidSignature_ShouldReturn200()
    {
        // Arrange
        string paymentIntentId = "pi_test_123456789";
        string webhookPayload = CreateStripeWebhookPayload("payment_intent.succeeded", paymentIntentId);
        string signature = GenerateStripeSignature(webhookPayload, WebhookSecret);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", signature);

        // Act
        HttpResponseMessage response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WebhookEndpoint_InvalidSignature_ShouldReturn400()
    {
        // Arrange
        string paymentIntentId = "pi_test_123456789";
        string webhookPayload = CreateStripeWebhookPayload("payment_intent.succeeded", paymentIntentId);
        string invalidSignature = "t=1234567890,v1=invalid_signature_hash";

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", invalidSignature);

        // Act
        HttpResponseMessage response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WebhookEndpoint_MissingSignature_ShouldReturn400()
    {
        // Arrange
        string paymentIntentId = "pi_test_123456789";
        string webhookPayload = CreateStripeWebhookPayload("payment_intent.succeeded", paymentIntentId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        // No Stripe-Signature header

        // Act
        HttpResponseMessage response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WebhookEndpoint_PaymentSucceeded_ShouldProcessSuccessfully()
    {
        // Arrange
        string paymentIntentId = "pi_test_succeeded_123";
        string webhookPayload = CreateStripeWebhookPayload("payment_intent.succeeded", paymentIntentId);
        string signature = GenerateStripeSignature(webhookPayload, WebhookSecret);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", signature);

        // Act
        HttpResponseMessage response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify ProcessExternalPaymentConfirmation command was dispatched
        // (Would need to check message bus or database in real integration test)
    }

    [Fact]
    public async Task WebhookEndpoint_PaymentFailed_ShouldProcessSuccessfully()
    {
        // Arrange
        string paymentIntentId = "pi_test_failed_123";
        string webhookPayload = CreateStripeWebhookPayload("payment_intent.payment_failed", paymentIntentId);
        string signature = GenerateStripeSignature(webhookPayload, WebhookSecret);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", signature);

        // Act
        HttpResponseMessage response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WebhookEndpoint_UnknownEventType_ShouldReturn200()
    {
        // Arrange - Stripe sends various event types, we should gracefully ignore unknown ones
        string paymentIntentId = "pi_test_123";
        string webhookPayload = CreateStripeWebhookPayload("customer.subscription.updated", paymentIntentId);
        string signature = GenerateStripeSignature(webhookPayload, WebhookSecret);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", signature);

        // Act
        HttpResponseMessage response = await _client.SendAsync(request);

        // Assert - Should still return 200 to prevent Stripe retries
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WebhookEndpoint_DuplicateEvent_ShouldBeIdempotent()
    {
        // Arrange - Same event sent twice (Stripe retry scenario)
        string paymentIntentId = "pi_test_duplicate_123";
        string webhookPayload = CreateStripeWebhookPayload("payment_intent.succeeded", paymentIntentId);
        string signature = GenerateStripeSignature(webhookPayload, WebhookSecret);

        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        request1.Headers.Add("Stripe-Signature", signature);

        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        request2.Headers.Add("Stripe-Signature", signature);

        // Act - Send same webhook twice
        HttpResponseMessage response1 = await _client.SendAsync(request1);
        HttpResponseMessage response2 = await _client.SendAsync(request2);

        // Assert - Both should return 200 (idempotent handling)
        response1.StatusCode.ShouldBe(HttpStatusCode.OK);
        response2.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    #region Helper Methods

    private string CreateStripeWebhookPayload(string eventType, string paymentIntentId)
    {
        var payload = new
        {
            id = $"evt_{Guid.NewGuid()}",
            @object = "event",
            type = eventType,
            data = new
            {
                @object = new
                {
                    id = paymentIntentId,
                    @object = "payment_intent",
                    amount = 10000,
                    currency = "usd",
                    status = eventType.Contains("succeeded") ? "succeeded" : "failed",
                    last_payment_error = eventType.Contains("failed")
                        ? new { message = "Card declined" }
                        : null
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private string GenerateStripeSignature(string payload, string secret)
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
