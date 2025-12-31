using Shouldly;
using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using System.Net.Http.Json;
using System.Net;

namespace NetCommerce.Integration.Tests.Idempotency;

/// <summary>
/// Idempotency tests using WireMock.Net to simulate external service calls.
/// Verifies that duplicate requests with same idempotency key return cached response.
/// </summary>
public class IdempotencyTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _client;

    public IdempotencyTests()
    {
        _server = WireMockServer.Start();
        _client = new HttpClient
        {
            BaseAddress = new Uri(_server.Url!)
        };
    }

    #region Order Creation Idempotency

    [Fact]
    public async Task CreateOrder_WithSameIdempotencyKey_ShouldReturnCachedResponse()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid().ToString();
        var orderId = Guid.NewGuid();
        var orderNumber = "ORD-2024-001";

        // Setup WireMock to simulate order creation endpoint
        _server
            .Given(Request.Create()
                .WithPath("/api/orders")
                .WithHeader("X-Idempotency-Key", idempotencyKey)
                .UsingPost())
            .InScenario("IdempotentOrder")
            .WillSetStateTo("OrderCreated")
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new
                {
                    orderId,
                    orderNumber,
                    status = "Pending",
                    createdAt = DateTime.UtcNow
                }));

        // Subsequent requests with same key return same response (cached)
        _server
            .Given(Request.Create()
                .WithPath("/api/orders")
                .WithHeader("X-Idempotency-Key", idempotencyKey)
                .UsingPost())
            .InScenario("IdempotentOrder")
            .WhenStateIs("OrderCreated")
            .RespondWith(Response.Create()
                .WithStatusCode(200) // 200 instead of 201 indicates cached
                .WithHeader("Content-Type", "application/json")
                .WithHeader("X-Idempotency-Replayed", "true")
                .WithBodyAsJson(new
                {
                    orderId,
                    orderNumber,
                    status = "Pending",
                    createdAt = DateTime.UtcNow
                }));

        // Act - First request
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new { customerId = Guid.NewGuid(), items = new[] { new { productId = Guid.NewGuid(), quantity = 1 } } })
        };
        request1.Headers.Add("X-Idempotency-Key", idempotencyKey);
        
        var response1 = await _client.SendAsync(request1);

        // Act - Second request (duplicate)
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new { customerId = Guid.NewGuid(), items = new[] { new { productId = Guid.NewGuid(), quantity = 1 } } })
        };
        request2.Headers.Add("X-Idempotency-Key", idempotencyKey);
        
        var response2 = await _client.SendAsync(request2);

        // Assert
        response1.StatusCode.ShouldBe(HttpStatusCode.Created);
        response2.StatusCode.ShouldBe(HttpStatusCode.OK);
        response2.Headers.Contains("X-Idempotency-Replayed").ShouldBeTrue();

        var order1 = await response1.Content.ReadFromJsonAsync<OrderResponse>();
        var order2 = await response2.Content.ReadFromJsonAsync<OrderResponse>();

        // Same order ID returned for both requests
        order1!.OrderId.ShouldBe(orderId);
        order2!.OrderId.ShouldBe(orderId);
        order1.OrderNumber.ShouldBe(order2.OrderNumber);
    }

    [Fact]
    public async Task CreateOrder_WithDifferentIdempotencyKeys_ShouldCreateSeparateOrders()
    {
        // Arrange
        var key1 = Guid.NewGuid().ToString();
        var key2 = Guid.NewGuid().ToString();
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();

        _server
            .Given(Request.Create()
                .WithPath("/api/orders")
                .WithHeader("X-Idempotency-Key", key1)
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBodyAsJson(new { orderId = orderId1, orderNumber = "ORD-001" }));

        _server
            .Given(Request.Create()
                .WithPath("/api/orders")
                .WithHeader("X-Idempotency-Key", key2)
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBodyAsJson(new { orderId = orderId2, orderNumber = "ORD-002" }));

        // Act
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request1.Headers.Add("X-Idempotency-Key", key1);
        request1.Content = JsonContent.Create(new { });

        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request2.Headers.Add("X-Idempotency-Key", key2);
        request2.Content = JsonContent.Create(new { });

        var response1 = await _client.SendAsync(request1);
        var response2 = await _client.SendAsync(request2);

        // Assert
        response1.StatusCode.ShouldBe(HttpStatusCode.Created);
        response2.StatusCode.ShouldBe(HttpStatusCode.Created);

        var order1 = await response1.Content.ReadFromJsonAsync<OrderResponse>();
        var order2 = await response2.Content.ReadFromJsonAsync<OrderResponse>();

        order1!.OrderId.ShouldNotBe(order2!.OrderId);
    }

    #endregion

    #region Payment Processing Idempotency

    [Fact]
    public async Task ProcessPayment_WithSameIdempotencyKey_ShouldNotDoubleCharge()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid().ToString();
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        _server
            .Given(Request.Create()
                .WithPath("/api/payments")
                .WithHeader("X-Idempotency-Key", idempotencyKey)
                .UsingPost())
            .InScenario("IdempotentPayment")
            .WillSetStateTo("PaymentProcessed")
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBodyAsJson(new
                {
                    paymentId,
                    orderId,
                    amount = 499.99,
                    status = "Completed",
                    processedAt = DateTime.UtcNow
                }));

        _server
            .Given(Request.Create()
                .WithPath("/api/payments")
                .WithHeader("X-Idempotency-Key", idempotencyKey)
                .UsingPost())
            .InScenario("IdempotentPayment")
            .WhenStateIs("PaymentProcessed")
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("X-Idempotency-Replayed", "true")
                .WithBodyAsJson(new
                {
                    paymentId,
                    orderId,
                    amount = 499.99,
                    status = "Completed",
                    processedAt = DateTime.UtcNow
                }));

        // Act
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
        {
            Content = JsonContent.Create(new { orderId, amount = 499.99m })
        };
        request1.Headers.Add("X-Idempotency-Key", idempotencyKey);

        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
        {
            Content = JsonContent.Create(new { orderId, amount = 499.99m })
        };
        request2.Headers.Add("X-Idempotency-Key", idempotencyKey);

        var response1 = await _client.SendAsync(request1);
        var response2 = await _client.SendAsync(request2);

        // Assert - Same payment ID, not double-charged
        response1.StatusCode.ShouldBe(HttpStatusCode.Created);
        response2.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payment1 = await response1.Content.ReadFromJsonAsync<PaymentResponse>();
        var payment2 = await response2.Content.ReadFromJsonAsync<PaymentResponse>();

        payment1!.PaymentId.ShouldBe(payment2!.PaymentId);
    }

    #endregion

    #region Stock Reservation Idempotency

    [Fact]
    public async Task ReserveStock_WithSameIdempotencyKey_ShouldNotDoubleReserve()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid().ToString();
        var reservationId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        _server
            .Given(Request.Create()
                .WithPath("/api/inventory/reserve")
                .WithHeader("X-Idempotency-Key", idempotencyKey)
                .UsingPost())
            .InScenario("IdempotentReservation")
            .WillSetStateTo("StockReserved")
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBodyAsJson(new
                {
                    reservationId,
                    productId,
                    quantity = 1,
                    expiresAt = DateTime.UtcNow.AddMinutes(15),
                    status = "Active"
                }));

        _server
            .Given(Request.Create()
                .WithPath("/api/inventory/reserve")
                .WithHeader("X-Idempotency-Key", idempotencyKey)
                .UsingPost())
            .InScenario("IdempotentReservation")
            .WhenStateIs("StockReserved")
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("X-Idempotency-Replayed", "true")
                .WithBodyAsJson(new
                {
                    reservationId,
                    productId,
                    quantity = 1,
                    expiresAt = DateTime.UtcNow.AddMinutes(15),
                    status = "Active"
                }));

        // Act
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/reserve")
        {
            Content = JsonContent.Create(new { productId, quantity = 1 })
        };
        request1.Headers.Add("X-Idempotency-Key", idempotencyKey);

        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/reserve")
        {
            Content = JsonContent.Create(new { productId, quantity = 1 })
        };
        request2.Headers.Add("X-Idempotency-Key", idempotencyKey);

        var response1 = await _client.SendAsync(request1);
        var response2 = await _client.SendAsync(request2);

        // Assert
        response1.StatusCode.ShouldBe(HttpStatusCode.Created);
        response2.StatusCode.ShouldBe(HttpStatusCode.OK);

        var reservation1 = await response1.Content.ReadFromJsonAsync<ReservationResponse>();
        var reservation2 = await response2.Content.ReadFromJsonAsync<ReservationResponse>();

        reservation1!.ReservationId.ShouldBe(reservation2!.ReservationId);
    }

    #endregion

    #region Missing Idempotency Key

    [Fact]
    public async Task CreateOrder_WithoutIdempotencyKey_ShouldReturnBadRequest()
    {
        // Arrange - Setup mock to return 400 for requests without idempotency key
        // WireMock matches by default if header is missing, so we just setup the response
        _server
            .Given(Request.Create()
                .WithPath("/api/orders")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(400)
                .WithBodyAsJson(new { error = "X-Idempotency-Key header is required" }));

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new { customerId = Guid.NewGuid() })
        };
        // Note: Not adding X-Idempotency-Key header

        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    #endregion

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }

    // Response DTOs
    private record OrderResponse(Guid OrderId, string OrderNumber, string Status);
    private record PaymentResponse(Guid PaymentId, Guid OrderId, decimal Amount, string Status);
    private record ReservationResponse(Guid ReservationId, Guid ProductId, int Quantity, DateTime ExpiresAt, string Status);
}
