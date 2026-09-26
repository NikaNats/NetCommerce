#nullable enable
using System.Net;
using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Infrastructure.Metrics;

namespace NetCommerce.Domain.Tests.Ordering;

/// <summary>
///     Unit tests for <see cref="StuckSagaAlertService"/> PagerDuty dispatch.
///     The sweep query itself is covered by integration tests; here we verify the
///     alert channel contract: success reporting, failure tolerance (never throws),
///     and correct request shape against PagerDuty Events API v2.
/// </summary>
public class StuckSagaAlertServiceTests
{
    private readonly ILogger _logger = Substitute.For<ILogger>();

    private static StuckSagaInfo StuckOrder() => new(
        Guid.NewGuid(),
        "ORD-20240101-ABCD1234",
        129.99m,
        "GEL",
        "Refund failed: gateway timeout",
        DateTime.UtcNow.AddHours(-5));

    [Fact]
    public async Task SendPagerDutyAlert_AcceptedResponse_ShouldReturnTrue()
    {
        using var client = new HttpClient(new FakeHandler(HttpStatusCode.Accepted, "{}"))
        {
            BaseAddress = new Uri("https://events.pagerduty.com/v2/")
        };

        var sent = await StuckSagaAlertService.SendPagerDutyAlertAsync(
            client, "routing-key", StuckOrder(), _logger, CancellationToken.None);

        sent.ShouldBeTrue();
    }

    [Fact]
    public async Task SendPagerDutyAlert_RejectedResponse_ShouldReturnFalseWithoutThrowing()
    {
        using var client = new HttpClient(new FakeHandler(HttpStatusCode.BadRequest, "invalid routing key"))
        {
            BaseAddress = new Uri("https://events.pagerduty.com/v2/")
        };

        var sent = await StuckSagaAlertService.SendPagerDutyAlertAsync(
            client, "bad-key", StuckOrder(), _logger, CancellationToken.None);

        sent.ShouldBeFalse();
    }

    [Fact]
    public async Task SendPagerDutyAlert_TransportFailure_ShouldReturnFalseWithoutThrowing()
    {
        using var client = new HttpClient(new ThrowingHandler(new HttpRequestException("no route")))
        {
            BaseAddress = new Uri("https://events.pagerduty.com/v2/")
        };

        var sent = await StuckSagaAlertService.SendPagerDutyAlertAsync(
            client, "routing-key", StuckOrder(), _logger, CancellationToken.None);

        sent.ShouldBeFalse();
    }

    [Fact]
    public async Task SendPagerDutyAlert_ShouldPostTriggerPayloadToEnqueue()
    {
        var capturing = new CapturingHandler(HttpStatusCode.Accepted, "{}");
        using var client = new HttpClient(capturing)
        {
            BaseAddress = new Uri("https://events.pagerduty.com/v2/")
        };

        var saga = StuckOrder();
        await StuckSagaAlertService.SendPagerDutyAlertAsync(
            client, "routing-key", saga, _logger, CancellationToken.None);

        capturing.Request.ShouldNotBeNull();
        capturing.Request!.Method.ShouldBe(HttpMethod.Post);
        capturing.Request.RequestUri!.AbsolutePath.ShouldBe("/v2/enqueue");

        var body = await capturing.Request.Content!.ReadAsStringAsync();
        body.ShouldContain("trigger");
        body.ShouldContain($"netcommerce-stuck-saga-{saga.OrderId}");
        body.ShouldContain(saga.OrderNumber);
    }

    private sealed class FakeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            });
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CapturingHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            });
        }
    }
}
