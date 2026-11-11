using System.Net;
using AgencyHub.Api.IntegrationTests.Infrastructure;
using AgencyHub.Modules.Client.Application;
using AgencyHub.Modules.Client.Domain;
using AgencyHub.Modules.Project.Application;
using AgencyHub.Modules.Website.Application;
using AgencyHub.Modules.Website.Domain;
using AgencyHub.SharedKernel.Pagination;

namespace AgencyHub.Api.IntegrationTests.Portfolio;

/// <summary>Client → Project → Website happy paths, paging, normalisation and dependency rules.</summary>
public sealed class PortfolioLifecycleTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PortfolioLifecycleTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Full_hierarchy_can_be_created_read_updated_and_deleted_in_order()
    {
        using var owner = await _factory.CreateAgencyUserClientAsync();
        var http = owner.Http;

        using var createdClient = await http.PostJsonAsync("/api/v1/clients", new { name = "Acme", contactEmail = "it@acme.example" });
        var client = await createdClient.ReadAsync<ClientResponse>();
        Assert.Equal(HttpStatusCode.Created, createdClient.StatusCode);
        Assert.Equal($"/api/v1/clients/{client.Id}", createdClient.Headers.Location?.OriginalString);

        var projectId = await http.CreateAndReadIdAsync("/api/v1/projects", new { clientId = client.Id, name = "Relaunch" });
        var website = await (await http.PostJsonAsync("/api/v1/websites", new { projectId, url = "HTTPS://Www.Acme.Example/Shop/" })).ReadAsync<WebsiteResponse>();
        Assert.Equal("https://www.acme.example/Shop", website.Url);
        Assert.Equal("www.acme.example", website.Host);
        Assert.Equal("www.acme.example", website.Name);

        var updated = await (await http.PutJsonAsync($"/api/v1/clients/{client.Id}", new
        {
            name = "Acme Corp",
            contactEmail = (string?)null,
            status = ClientStatus.Archived,
        })).ReadAsync<ClientResponse>();
        Assert.Equal("Acme Corp", updated.Name);
        Assert.Equal(ClientStatus.Archived, updated.Status);
        Assert.Null(updated.ContactEmail);
        Assert.NotNull(updated.UpdatedAtUtc);

        // Parents cannot be deleted while children exist.
        using var deleteClientWithProjects = await http.DeletePathAsync($"/api/v1/clients/{client.Id}");
        using var deleteProjectWithWebsites = await http.DeletePathAsync($"/api/v1/projects/{projectId}");
        Assert.Equal(HttpStatusCode.Conflict, deleteClientWithProjects.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deleteProjectWithWebsites.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await http.DeletePathAsync($"/api/v1/websites/{website.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.DeletePathAsync($"/api/v1/projects/{projectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.DeletePathAsync($"/api/v1/clients/{client.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetPathAsync($"/api/v1/clients/{client.Id}")).StatusCode);
    }

    [Fact]
    public async Task Lists_are_paged_sorted_and_filtered()
    {
        using var owner = await _factory.CreateAgencyUserClientAsync();
        foreach (var name in new[] { "Delta", "alpha", "Charlie", "Bravo", "Echo" })
        {
            await owner.Http.CreateAndReadIdAsync("/api/v1/clients", new { name });
        }

        var firstPage = await (await owner.Http.GetPathAsync("/api/v1/clients?page=1&pageSize=2")).ReadAsync<PagedResult<ClientResponse>>();
        var lastPage = await (await owner.Http.GetPathAsync("/api/v1/clients?page=3&pageSize=2")).ReadAsync<PagedResult<ClientResponse>>();
        var search = await (await owner.Http.GetPathAsync("/api/v1/clients?search=ar")).ReadAsync<PagedResult<ClientResponse>>();
        var oversized = await (await owner.Http.GetPathAsync("/api/v1/clients?pageSize=100000")).ReadAsync<PagedResult<ClientResponse>>();

        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(["alpha", "Bravo"], firstPage.Items.Select(c => c.Name));
        Assert.Equal(["Echo"], lastPage.Items.Select(c => c.Name));
        Assert.Equal(["Charlie"], search.Items.Select(c => c.Name));
        Assert.Equal(PageRequest.MaxPageSize, oversized.PageSize);
    }

    [Fact]
    public async Task Same_website_url_cannot_be_registered_twice_in_an_agency()
    {
        using var owner = await _factory.CreateAgencyUserClientAsync();
        var clientId = await owner.Http.CreateAndReadIdAsync("/api/v1/clients", new { name = "Dup" });
        var projectId = await owner.Http.CreateAndReadIdAsync("/api/v1/projects", new { clientId, name = "Dup" });

        using var first = await owner.Http.PostJsonAsync("/api/v1/websites", new { projectId, url = "https://dup.example" });
        using var normalizedDuplicate = await owner.Http.PostJsonAsync("/api/v1/websites", new { projectId, url = "https://DUP.example/" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, normalizedDuplicate.StatusCode);
    }

    [Fact]
    public async Task Websites_can_be_paused_and_filtered_by_status_and_project()
    {
        using var owner = await _factory.CreateAgencyUserClientAsync();
        var clientId = await owner.Http.CreateAndReadIdAsync("/api/v1/clients", new { name = "Filter" });
        var projectA = await owner.Http.CreateAndReadIdAsync("/api/v1/projects", new { clientId, name = "A" });
        var projectB = await owner.Http.CreateAndReadIdAsync("/api/v1/projects", new { clientId, name = "B" });
        var siteA = await owner.Http.CreateAndReadIdAsync("/api/v1/websites", new { projectId = projectA, url = "https://a.example" });
        await owner.Http.CreateAndReadIdAsync("/api/v1/websites", new { projectId = projectB, url = "https://b.example" });

        using var paused = await owner.Http.PutJsonAsync($"/api/v1/websites/{siteA}", new { url = "https://a.example", status = WebsiteStatus.Paused });
        var pausedList = await (await owner.Http.GetPathAsync("/api/v1/websites?status=Paused")).ReadAsync<PagedResult<WebsiteResponse>>();
        var projectBList = await (await owner.Http.GetPathAsync($"/api/v1/websites?projectId={projectB}")).ReadAsync<PagedResult<WebsiteResponse>>();
        var projectsOfClient = await (await owner.Http.GetPathAsync($"/api/v1/projects?clientId={clientId}")).ReadAsync<PagedResult<ProjectResponse>>();

        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        Assert.Equal([siteA], pausedList.Items.Select(w => w.Id));
        Assert.Equal(["b.example"], projectBList.Items.Select(w => w.Host));
        Assert.Equal(2, projectsOfClient.TotalCount);
    }
}
