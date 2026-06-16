using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MSSQLPlanViewer.Web.Showplans;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class EstimatedShowplanApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EstimatedShowplanApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EstimatedShowplan_ReturnsParsedPlanMetadata()
    {
        var provider = new StubEstimatedShowplanProvider(_ =>
            Task.FromResult<IReadOnlyList<EstimatedShowplanXml>>(
            [
                new EstimatedShowplanXml(1, SamplePlanLoader.Load("nested-loops-2022.sqlplan"))
            ]));
        using var client = CreateClient(provider);

        var response = await client.PostAsJsonAsync("/api/showplans/estimated", new
        {
            connectionString = "Server=.;Database=Test;Integrated Security=true;",
            query = "SELECT 1;",
            label = "Customer query",
            commandTimeoutSeconds = 30
        });

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var plans = json.RootElement.GetProperty("plans");
        Assert.Equal(1, plans.GetArrayLength());

        var plan = plans[0];
        Assert.Equal("Customer query", plan.GetProperty("label").GetString());
        Assert.Contains("<ShowPlanXML", plan.GetProperty("showplanXml").GetString(), StringComparison.Ordinal);
        Assert.True(plan.GetProperty("statementCount").GetInt32() > 0);
        Assert.True(plan.GetProperty("totalNodeCount").GetInt32() > 0);
        Assert.Equal("SqlServer2022", plan.GetProperty("schemaVersion").GetString());

        var request = Assert.Single(provider.Requests);
        Assert.Equal("SELECT 1;", request.Query);
        Assert.Equal(30, request.CommandTimeoutSeconds);
    }

    [Fact]
    public async Task EstimatedShowplan_ReturnsBadRequestForMissingConnectionString()
    {
        var provider = new StubEstimatedShowplanProvider(_ => throw new InvalidOperationException("Provider should not be called."));
        using var client = CreateClient(provider);

        var response = await client.PostAsJsonAsync("/api/showplans/estimated", new
        {
            connectionString = "",
            query = "SELECT 1;"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("connectionString", body, StringComparison.Ordinal);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task EstimatedShowplan_ReturnsBadGatewayForSqlFailure()
    {
        var provider = new StubEstimatedShowplanProvider(_ =>
            throw new EstimatedShowplanException(
                EstimatedShowplanFailureKind.SqlExecution,
                "SHOWPLAN permission denied."));
        using var client = CreateClient(provider);

        var response = await client.PostAsJsonAsync("/api/showplans/estimated", new
        {
            connectionString = "Server=.;Database=Test;Integrated Security=true;",
            query = "SELECT 1;"
        });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SHOWPLAN permission denied", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EstimatedShowplan_ReturnsGatewayTimeoutForTimeout()
    {
        var provider = new StubEstimatedShowplanProvider(_ =>
            throw new EstimatedShowplanException(
                EstimatedShowplanFailureKind.Timeout,
                "Timed out while retrieving the estimated execution plan."));
        using var client = CreateClient(provider);

        var response = await client.PostAsJsonAsync("/api/showplans/estimated", new
        {
            connectionString = "Server=.;Database=Test;Integrated Security=true;",
            query = "SELECT 1;"
        });

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("timed out", body, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateClient(IEstimatedShowplanProvider provider) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEstimatedShowplanProvider>();
                services.AddSingleton(provider);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private sealed class StubEstimatedShowplanProvider : IEstimatedShowplanProvider
    {
        private readonly Func<EstimatedShowplanRequest, Task<IReadOnlyList<EstimatedShowplanXml>>> _handler;

        public StubEstimatedShowplanProvider(Func<EstimatedShowplanRequest, Task<IReadOnlyList<EstimatedShowplanXml>>> handler)
        {
            _handler = handler;
        }

        public List<EstimatedShowplanRequest> Requests { get; } = new();

        public Task<IReadOnlyList<EstimatedShowplanXml>> GetEstimatedShowplansAsync(
            EstimatedShowplanRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return _handler(request);
        }
    }
}
