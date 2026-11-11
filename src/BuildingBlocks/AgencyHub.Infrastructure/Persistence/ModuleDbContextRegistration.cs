using AgencyHub.Infrastructure.Tenancy;
using AgencyHub.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyHub.Infrastructure.Persistence;

public static class ModuleDbContextRegistration
{
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// Registers a module DbContext on the shared SQL Server database with its own schema and migration history,
    /// retry-on-failure, and the tenant/audit interceptors. Every module must register its context through this method.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : TenantAwareDbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
            }

            options.UseModuleSqlServer(connectionString, schema, enableRetry: true);
            options.UsePersistenceInterceptors(sp);
        });

        return services;
    }

    public static DbContextOptionsBuilder UseModuleSqlServer(
        this DbContextOptionsBuilder options,
        string connectionString,
        string schema,
        bool enableRetry = false) =>
        options.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
            if (enableRetry)
            {
                sql.EnableRetryOnFailure();
            }
        });

    /// <summary>Count + page in two round trips. Callers must apply a deterministic OrderBy first.</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> orderedQuery,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var total = await orderedQuery.LongCountAsync(cancellationToken);
        var items = await orderedQuery.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }
}

/// <summary>
/// Base for the `dotnet ef` design-time factory of each module. Not used at runtime.
/// Connection string: AGENCYHUB_DESIGN_CONNECTION environment variable, otherwise local SQL Express.
/// </summary>
public abstract class ModuleDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
    where TContext : TenantAwareDbContext
{
    public const string DefaultDesignConnection = @"Server=.\SQLEXPRESS;Database=AgencyHub;Trusted_Connection=True;TrustServerCertificate=True";

    protected abstract string Schema { get; }

    public TContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENCYHUB_DESIGN_CONNECTION") ?? DefaultDesignConnection;
        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseModuleSqlServer(connectionString, Schema);
        return Create(builder.Options, new TenantContext());
    }

    protected abstract TContext Create(DbContextOptions<TContext> options, TenantContext tenantContext);
}
