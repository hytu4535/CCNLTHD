
using System.Net;
using FluentAssertions;
using Xunit;

namespace StripeWebhooks.Tests.Integration;

public class HealthAndInfrastructureTests : IClassFixture<Utils.TestWebApplicationFactory>
{
    private readonly Utils.TestWebApplicationFactory _factory;

    public HealthAndInfrastructureTests(Utils.TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Root_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("stripe-webhooks-dotnet");
        body.Should().Contain("\"status\":\"ok\"");
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Healthy");
    }

    [Fact]
    public async Task CorrelationId_IsReturned_AndPreservesIncomingValue()
    {
        var client = _factory.CreateClient();
        const string correlationId = "test-correlation-123";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Correlation-Id", correlationId);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Correlation-Id", out var values).Should().BeTrue();
        values!.Single().Should().Be(correlationId);
    }
}
