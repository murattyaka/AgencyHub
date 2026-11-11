using System.Net;
using AgencyHub.Api.IntegrationTests.Infrastructure;
using AgencyHub.Modules.Agency.Application;
using AgencyHub.Modules.Agency.Infrastructure;
using AgencyHub.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyHub.Api.IntegrationTests.Agency;

public sealed class AgencyRegistrationTests : IClassFixture<ApiFactory>
{
    private const string RegisterPath = "/api/v1/agencies/register";

    private readonly ApiFactory _factory;
    private readonly SqlServerTestDatabase _database;

    public AgencyRegistrationTests(ApiFactory factory, SqlServerTestDatabase database)
    {
        _factory = factory;
        _database = database;
    }

    [Fact]
    public async Task Registered_owner_can_sign_in_and_manage_the_new_agency()
    {
        using var http = _factory.CreateHttpsClient();
        var request = NewRequest();

        using var response = await http.PostJsonAsync(RegisterPath, request);
        var registered = await response.ReadAsync<RegisterAgencyResult>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var login = await http.LoginAsync(request.OwnerEmail, request.OwnerPassword);
        Assert.Equal(HttpStatusCode.OK, login.Response.StatusCode);
        Assert.Equal(registered.AgencyId, login.Body!.AgencyId);
        Assert.Equal(nameof(AgencyRole.Owner), login.Body.Role);

        http.DefaultRequestHeaders.Authorization = new("Bearer", login.Body.AccessToken);
        using var renamed = await http.PutJsonAsync("/api/v1/agency", new { name = "Renamed Agency" });
        var agency = await (await http.GetPathAsync("/api/v1/agency")).ReadAsync<AgencyResponse>();
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(registered.AgencyId, agency.Id);
        Assert.Equal("Renamed Agency", agency.Name);
        Assert.Equal("Active", agency.Status);
    }

    [Fact]
    public async Task Failed_owner_creation_rolls_back_the_agency()
    {
        // Duplicate owner email: the agency row must be removed again (compensation).
        var existing = await _factory.CreateUserAsync();
        using var http = _factory.CreateHttpsClient();
        var agencyName = $"Compensated {Guid.NewGuid():N}";

        using var duplicate = await http.PostJsonAsync(RegisterPath, NewRequest(agencyName) with { OwnerEmail = existing.Email });
        using var weakPassword = await http.PostJsonAsync(RegisterPath, NewRequest(agencyName) with { OwnerPassword = "short" });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);
        Assert.Equal(0, await CountAgenciesNamedAsync(agencyName));
    }

    [Fact]
    public async Task Registration_is_not_available_when_disabled()
    {
        await using var factory = new ApiFactory(
            _database.ConnectionString,
            settings: new Dictionary<string, string?> { ["AgencyRegistration:Enabled"] = "false" });
        using var http = factory.CreateHttpsClient();

        using var response = await http.PostJsonAsync(RegisterPath, NewRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Signed_in_agency_user_cannot_register_another_agency()
    {
        using var owner = await _factory.CreateAgencyUserClientAsync();

        using var response = await owner.Http.PostJsonAsync(RegisterPath, NewRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(AgencyRole.Member)]
    [InlineData(AgencyRole.Viewer)]
    public async Task Only_owner_or_admin_can_change_agency_settings(AgencyRole role)
    {
        using var user = await _factory.CreateAgencyUserClientAsync(role);

        using var response = await user.Http.PutJsonAsync("/api/v1/agency", new { name = "Hijacked" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static RegisterAgencyRequest NewRequest(string? agencyName = null) => new(
        agencyName ?? $"Agency {Guid.NewGuid():N}",
        $"owner-{Guid.NewGuid():N}@test.local",
        "Owner User",
        TestUsers.DefaultPassword);

    private async Task<int> CountAgenciesNamedAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgencyModuleDbContext>();
        return await db.Agencies.CountAsync(a => a.Name == name, TestContext.Current.CancellationToken);
    }
}
