using System.Net;
using AgencyHub.Api.IntegrationTests.Infrastructure;
using AgencyHub.Modules.Identity.Contracts;

namespace AgencyHub.Api.IntegrationTests.Portfolio;

/// <summary>Viewer: read only. Member: read + create/update. Owner/Admin: everything incl. delete.</summary>
public sealed class RoleAuthorizationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public RoleAuthorizationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<AgencyRole, HttpStatusCode, HttpStatusCode, HttpStatusCode, HttpStatusCode> Matrix => new()
    {
        // role,              GET,               POST,                    PUT,                     DELETE
        { AgencyRole.Viewer, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden },
        { AgencyRole.Member, HttpStatusCode.OK, HttpStatusCode.Created,   HttpStatusCode.OK,        HttpStatusCode.Forbidden },
        { AgencyRole.Admin,  HttpStatusCode.OK, HttpStatusCode.Created,   HttpStatusCode.OK,        HttpStatusCode.NoContent },
        { AgencyRole.Owner,  HttpStatusCode.OK, HttpStatusCode.Created,   HttpStatusCode.OK,        HttpStatusCode.NoContent },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Client_endpoints_enforce_the_role_matrix(
        AgencyRole role, HttpStatusCode get, HttpStatusCode post, HttpStatusCode put, HttpStatusCode delete)
    {
        using var owner = await _factory.CreateAgencyUserClientAsync(AgencyRole.Owner);
        using var user = await _factory.CreateAgencyUserClientAsync(role, owner.User.AgencyId);
        var existing = await owner.Http.CreateAndReadIdAsync("/api/v1/clients", new { name = "Existing" });

        using var getResponse = await user.Http.GetPathAsync($"/api/v1/clients/{existing}");
        using var postResponse = await user.Http.PostJsonAsync("/api/v1/clients", new { name = "New" });
        using var putResponse = await user.Http.PutJsonAsync($"/api/v1/clients/{existing}", new { name = "Renamed", status = "Active" });
        using var deleteResponse = await user.Http.DeletePathAsync($"/api/v1/clients/{existing}");

        Assert.Equal(get, getResponse.StatusCode);
        Assert.Equal(post, postResponse.StatusCode);
        Assert.Equal(put, putResponse.StatusCode);
        Assert.Equal(delete, deleteResponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/clients")]
    [InlineData("/api/v1/projects")]
    [InlineData("/api/v1/websites")]
    [InlineData("/api/v1/agency")]
    public async Task Anonymous_callers_get_401(string path)
    {
        using var http = _factory.CreateHttpsClient();

        using var response = await http.GetPathAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_create_projects_or_websites()
    {
        using var owner = await _factory.CreateAgencyUserClientAsync(AgencyRole.Owner);
        using var viewer = await _factory.CreateAgencyUserClientAsync(AgencyRole.Viewer, owner.User.AgencyId);
        var clientId = await owner.Http.CreateAndReadIdAsync("/api/v1/clients", new { name = "C" });
        var projectId = await owner.Http.CreateAndReadIdAsync("/api/v1/projects", new { clientId, name = "P" });

        using var project = await viewer.Http.PostJsonAsync("/api/v1/projects", new { clientId, name = "Nope" });
        using var website = await viewer.Http.PostJsonAsync("/api/v1/websites", new { projectId, url = "https://nope.example" });

        Assert.Equal(HttpStatusCode.Forbidden, project.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, website.StatusCode);
    }
}
