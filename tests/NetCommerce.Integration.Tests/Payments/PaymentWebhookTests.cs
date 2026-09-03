#nullable enable
using Amazon.S3;
using JasperFx;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Kernel.Application.Notifications;
using NetCommerce.Kernel.Stripe;
using NSubstitute;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Wolverine;

namespace NetCommerce.Integration.Tests.Payments;

/// <summary>
///     Integration tests for Stripe webhook endpoint via WebApplicationFactory.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class PaymentWebhookTests : IntegrationTestBase
{
    private const string WebhookSecret = "whsec_test_secret";
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    static PaymentWebhookTests()
    {
        // Must be set at the process level before WebApplication.CreateBuilder(args) runs
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
    }

    public PaymentWebhookTests(IntegrationTestFixture fixture) : base(fixture)
    {
        JasperFxEnvironment.AutoStartHost = true;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("AutoMigrate", "false");

            // Point all connection strings to Testcontainers
            builder.UseSetting("ConnectionStrings:CatalogDb", fixture.PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:InventoryDb", fixture.PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:OrderingDb", fixture.PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:PaymentsDb", fixture.PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:FinanceDb", fixture.PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:postgres", fixture.PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", fixture.RedisConnectionString);

            builder.ConfigureServices(services =>
            {
                // Override Stripe configuration with test values
                services.Configure<StripeOptions>(options =>
                {
                    options.SecretKey = "sk_test_mock";
                    options.PublishableKey = "pk_test_mock";
                    options.WebhookSecret = WebhookSecret;
                });

                services.AddDistributedMemoryCache();

                // Register test doubles for external I/O
                services.AddScoped<IAmazonS3>(_ => Substitute.For<IAmazonS3>());

                // Mock IMessageBus for isolated signature testing
                services.RemoveAll<IMessageBus>();
                services.AddSingleton<IMessageBus>(Substitute.For<IMessageBus>());

                services.AddScoped<IEmailProvider>(_ => Substitute.For<IEmailProvider>());
                services.AddScoped<ITemplateEngine>(_ => Substitute.For<ITemplateEngine>());

                var mockTenantContext = Substitute.For<NetCommerce.Kernel.Application.ITenantContext>();
                mockTenantContext.TenantId.Returns("test-tenant");
                mockTenantContext.HasTenant.Returns(true);
                services.AddSingleton(mockTenantContext);

                var mockUserContext = Substitute.For<NetCommerce.Kernel.Application.IUserContext>();
                mockUserContext.UserId.Returns("test-user");
                services.AddSingleton(mockUserContext);
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact(Skip = "Covered by PaymentWebhookContractTests")]
    public async Task WebhookEndpoint_ValidSignature_ShouldReturn200()
    {
        string paymentIntentId = "pi_test_123456789";
        string webhookPayload = CreateStripeWebhookPayload("payment_intent.succeeded", paymentIntentId);
        string signature = GenerateStripeSignature(webhookPayload, WebhookSecret);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", signature);

        HttpResponseMessage response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WebhookEndpoint_InvalidSignature_ShouldReturn400()
    {
        string paymentIntentId = "pi_test_123456789";
        string webhookPayload = CreateStripeWebhookPayload("payment_intent.succeeded", paymentIntentId);
        string invalidSignature = "t=1234567890,v1=invalid_signature_hash";

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", invalidSignature);

        HttpResponseMessage response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WebhookEndpoint_MissingSignature_ShouldReturn400()
    {
        string paymentIntentId = "pi_test_123456789";
        string webhookPayload = CreateStripeWebhookPayload("payment_intent.succeeded", paymentIntentId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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
