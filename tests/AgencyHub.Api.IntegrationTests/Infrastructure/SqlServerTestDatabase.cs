using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Infrastructure.Tenancy;
using AgencyHub.Modules.Agency.Infrastructure;
using AgencyHub.Modules.Client.Infrastructure;
using AgencyHub.Modules.Identity.Infrastructure;
using AgencyHub.Modules.Project.Infrastructure;
using AgencyHub.Modules.Website.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

[assembly: AssemblyFixture(typeof(AgencyHub.Api.IntegrationTests.Infrastructure.SqlServerTestDatabase))]

namespace AgencyHub.Api.IntegrationTests.Infrastructure;

/// <summary>
/// One real SQL Server database per test run, created from every module's migrations and dropped afterwards.
/// Server: AGENCYHUB_TEST_SQLSERVER environment variable, default <c>.\SQLEXPRESS</c> (Windows authentication).
/// Only databases named AgencyHub_Test_* are ever created or dropped.
/// </summary>
public sealed class SqlServerTestDatabase : IAsyncLifetime
{
    public SqlServerTestDatabase()
    {
        var server = Environment.GetEnvironmentVariable("AGENCYHUB_TEST_SQLSERVER") ?? @".\SQLEXPRESS";
        ConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = $"AgencyHub_Test_{Guid.NewGuid():N}",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
        }.ConnectionString;
    }

    public string ConnectionString { get; }

    public async ValueTask InitializeAsync()
    {
        await MigrateAsync<IdentityModuleDbContext>(o => new IdentityModuleDbContext(o, new TenantContext()), IdentityModuleDbContext.Schema);
        await MigrateAsync<AgencyModuleDbContext>(o => new AgencyModuleDbContext(o, new TenantContext()), AgencyModuleDbContext.Schema);
        await MigrateAsync<ClientModuleDbContext>(o => new ClientModuleDbContext(o, new TenantContext()), ClientModuleDbContext.Schema);
        await MigrateAsync<ProjectModuleDbContext>(o => new ProjectModuleDbContext(o, new TenantContext()), ProjectModuleDbContext.Schema);
        await MigrateAsync<WebsiteModuleDbContext>(o => new WebsiteModuleDbContext(o, new TenantContext()), WebsiteModuleDbContext.Schema);
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var context = Create<IdentityModuleDbContext>(o => new IdentityModuleDbContext(o, new TenantContext()), IdentityModuleDbContext.Schema);
        await context.Database.EnsureDeletedAsync();
    }

    private async Task MigrateAsync<TContext>(Func<DbContextOptions<TContext>, TContext> factory, string schema)
        where TContext : TenantAwareDbContext
    {
        await using var context = Create(factory, schema);
        await context.Database.MigrateAsync();
    }

    private TContext Create<TContext>(Func<DbContextOptions<TContext>, TContext> factory, string schema)
        where TContext : TenantAwareDbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseModuleSqlServer(ConnectionString, schema);
        return factory(builder.Options);
    }
}
