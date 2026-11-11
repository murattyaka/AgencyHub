using System.Net;
using System.Text;
using System.Text.Json;
using AgencyHub.Api.IntegrationTests.Infrastructure;

namespace AgencyHub.Api.IntegrationTests.Portfolio;

public sealed class InputValidationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public InputValidationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]")]
    [InlineData("https://example.com:8443")]
    [InlineData("ftp://example.com")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://intranet")]
    [InlineData("https://printer.local")]
    [InlineData("not a url")]
    public async Task Unsafe_or_invalid_website_urls_are_rejected_with_400(string url)
    {
        using var owner = await _factory.CreateAgencyUserClientAsync();
        var clientId = await owner.Http.CreateAndReadIdAsync("/api/v1/clients", new { name = "V" });
        var projectId = await owner.Http.CreateAndReadIdAsync("/api/v1/projects", new { clientId, name = "V" });

        using var response = await owner.Http.PostJsonAsync("/api/v1/websites", new { projectId, url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("Url", out _));
    }

    [Fact]
    public async Task Missing_required_fields_return_field_errors()
    {
        using var owner = await _factory.CreateAgencyUserClientAsync();

        using var response = await owner.Http.PostJsonAsync("/api/v1/clients", new { name = "", contactEmail = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var errors = json.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("ContactEmail", out _));
    }

    [Fact]
    public async Task Malformed_json_returns_400_not_500()
    {
        using var owner = await _factory.CreateAgencyUserClientAsync();
        using var content = new StringContent("{ \"name\": ", Encoding.UTF8, "application/json");

        using var response = await owner.Http.PostAsync(new Uri("/api/v1/clients", UriKind.Relative), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/clients?status=Deleted")]
    [InlineData("/api/v1/clients?page=abc")]
    [InlineData("/api/v1/projects?clientId=not-a-guid")]
    public async Task Malformed_query_parameters_return_400(string path)
    {
        using var owner = await _factory.CreateAgencyUserClientAsync();

        using var response = await owner.Http.GetPathAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Overlong_search_is_rejected()
    {
        using var owner = await _factory.CreateAgencyUserClientAsync();

        using var response = await owner.Http.GetPathAsync($"/api/v1/clients?search={new string('x', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
