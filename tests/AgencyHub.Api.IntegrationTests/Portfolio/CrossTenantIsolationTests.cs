using System.Net;
using AgencyHub.Api.IntegrationTests.Infrastructure;
using AgencyHub.Modules.Client.Application;
using AgencyHub.Modules.Project.Application;
using AgencyHub.Modules.Website.Application;
using AgencyHub.SharedKernel.Pagination;

namespace AgencyHub.Api.IntegrationTests.Portfolio;

/// <summary>
/// IDOR / broken access control: agency B knows the ids of agency A's resources and tries every operation.
/// Expected: indistinguishable from "does not exist", and A's data is unchanged.
/// </summary>
public sealed class CrossTenantIsolationTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private AgencyUserClient _agencyA = null!;
    private AgencyUserClient _agencyB = null!;
    private Guid _clientOfA;
    private Guid _projectOfA;
    private Guid _websiteOfA;

    public CrossTenantIsolationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string> ResourcePaths =>
    [
        "/api/v1/clients/{client}",
        "/api/v1/projects/{project}",
        "/api/v1/websites/{website}",
    ];

    public async ValueTask InitializeAsync()
    {
        _agencyA = await _factory.CreateAgencyUserClientAsync();
        _agencyB = await _factory.CreateAgencyUserClientAsync();

        _clientOfA = await _agencyA.Http.CreateAndReadIdAsync("/api/v1/clients", new { name = "Secret client of A" });
        _projectOfA = await _agencyA.Http.CreateAndReadIdAsync("/api/v1/projects", new { clientId = _clientOfA, name = "Secret project" });
        _websiteOfA = await _agencyA.Http.CreateAndReadIdAsync("/api/v1/websites", new { projectId = _projectOfA, url = "https://secret-a.example" });
    }

    [Theory]
    [MemberData(nameof(ResourcePaths))]
    public async Task Reading_another_agencys_resource_returns_404(string pathTemplate)
    {
        using var response = await _agencyB.Http.GetPathAsync(Resolve(pathTemplate));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ResourcePaths))]
    public async Task Deleting_another_agencys_resource_returns_404_and_keeps_it(string pathTemplate)
    {
        using var response = await _agencyB.Http.DeletePathAsync(Resolve(pathTemplate));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var stillThere = await _agencyA.Http.GetPathAsync(Resolve(pathTemplate));
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task Updating_another_agencys_resources_returns_404_and_changes_nothing()
    {
        using var client = await _agencyB.Http.PutJsonAsync($"/api/v1/clients/{_clientOfA}", new { name = "Hijacked", status = "Active" });
        using var project = await _agencyB.Http.PutJsonAsync($"/api/v1/projects/{_projectOfA}", new { name = "Hijacked", status = "Active" });
        using var website = await _agencyB.Http.PutJsonAsync($"/api/v1/websites/{_websiteOfA}", new { url = "https://evil.example", status = "Active" });

        Assert.Equal(HttpStatusCode.NotFound, client.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, project.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, website.StatusCode);
        var unchanged = await (await _agencyA.Http.GetPathAsync($"/api/v1/clients/{_clientOfA}")).ReadAsync<ClientResponse>();
        Assert.Equal("Secret client of A", unchanged.Name);
    }

    [Fact]
    public async Task Lists_never_contain_another_agencys_resources()
    {
        var clients = await (await _agencyB.Http.GetPathAsync("/api/v1/clients?search=Secret")).ReadAsync<PagedResult<ClientResponse>>();
        var projectsByForeignClient = await (await _agencyB.Http.GetPathAsync($"/api/v1/projects?clientId={_clientOfA}")).ReadAsync<PagedResult<ProjectResponse>>();
        var websitesByForeignProject = await (await _agencyB.Http.GetPathAsync($"/api/v1/websites?projectId={_projectOfA}")).ReadAsync<PagedResult<WebsiteResponse>>();

        Assert.Equal(0, clients.TotalCount);
        Assert.Equal(0, projectsByForeignClient.TotalCount);
        Assert.Equal(0, websitesByForeignProject.TotalCount);
    }

    [Fact]
    public async Task Children_cannot_be_attached_to_another_agencys_parents()
    {
        using var project = await _agencyB.Http.PostJsonAsync("/api/v1/projects", new { clientId = _clientOfA, name = "Parasite" });
        using var website = await _agencyB.Http.PostJsonAsync("/api/v1/websites", new { projectId = _projectOfA, url = "https://parasite.example" });

        // Same answer as for a random id: existence of A's resources is not revealed.
        using var randomParent = await _agencyB.Http.PostJsonAsync("/api/v1/projects", new { clientId = Guid.NewGuid(), name = "Parasite" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, project.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, website.StatusCode);
        Assert.Equal(randomParent.StatusCode, project.StatusCode);
    }

    [Fact]
    public async Task Agency_id_in_the_request_body_is_ignored()
    {
        // Mass assignment / tenant id manipulation: the request DTO has no agencyId; extra JSON members are ignored
        // and the new row belongs to the caller's agency.
        var id = await _agencyB.Http.CreateAndReadIdAsync("/api/v1/clients", new { name = "Planted", agencyId = _agencyA.User.AgencyId });

        using var visibleToA = await _agencyA.Http.GetPathAsync($"/api/v1/clients/{id}");
        using var visibleToB = await _agencyB.Http.GetPathAsync($"/api/v1/clients/{id}");
        Assert.Equal(HttpStatusCode.NotFound, visibleToA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, visibleToB.StatusCode);
    }

    [Fact]
    public async Task Different_agencies_may_monitor_the_same_website()
    {
        var clientOfB = await _agencyB.Http.CreateAndReadIdAsync("/api/v1/clients", new { name = "B client" });
        var projectOfB = await _agencyB.Http.CreateAndReadIdAsync("/api/v1/projects", new { clientId = clientOfB, name = "B project" });

        using var response = await _agencyB.Http.PostJsonAsync("/api/v1/websites", new { projectId = projectOfB, url = "https://secret-a.example" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public ValueTask DisposeAsync()
    {
        _agencyA.Dispose();
        _agencyB.Dispose();
        return ValueTask.CompletedTask;
    }

    private string Resolve(string template) => template
        .Replace("{client}", _clientOfA.ToString(), StringComparison.Ordinal)
        .Replace("{project}", _projectOfA.ToString(), StringComparison.Ordinal)
        .Replace("{website}", _websiteOfA.ToString(), StringComparison.Ordinal);
}
