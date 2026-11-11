using System.Net;
using AgencyHub.Api.IntegrationTests.Infrastructure;

namespace AgencyHub.Api.IntegrationTests;

public sealed class PipelineTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly SqlServerTestDatabase _database;

    public PipelineTests(ApiFactory factory, SqlServerTestDatabase database)
    {
        _factory = factory;
        _database = database;
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_report_healthy(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unknown_route_is_401_for_anonymous_callers_so_routes_cannot_be_enumerated()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_route_returns_404_problem_details_for_authenticated_callers()
    {
        var user = await _factory.CreateUserAsync();
        using var client = _factory.CreateHttpsClient();
        var login = await client.LoginAsync(user);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/does-not-exist", UriKind.Relative));
        request.Headers.Authorization = new("Bearer", login.Body!.AccessToken);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Security_headers_are_present_and_server_header_is_hidden()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task OpenApi_document_is_available_in_development()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_not_exposed_in_production()
    {
        // Authenticated, so a 404 proves the endpoint is not mapped at all (anonymous callers get 401 anyway).
        await using var production = new ApiFactory(_database.ConnectionString, "Production");
        var user = await production.CreateUserAsync();
        using var client = production.CreateHttpsClient();
        var login = await client.LoginAsync(user);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/openapi/v1.json", UriKind.Relative));
        request.Headers.Authorization = new("Bearer", login.Body!.AccessToken);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Production_redirects_http_to_https()
    {
        await using var production = new ApiFactory(_database.ConnectionString, "Production");
        using var client = production
            .WithWebHostBuilder(b => b.UseSetting("https_port", "443"))
            .CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal(Uri.UriSchemeHttps, response.Headers.Location?.Scheme);
    }
}
