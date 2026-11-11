using AgencyHub.Infrastructure.Persistence.Interceptors;
using AgencyHub.Infrastructure.Tenancy;
using AgencyHub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgencyHub.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers tenant context and persistence interceptors shared by all modules.
    /// Hosts must also register an <c>ICurrentUser</c> implementation.
    /// </summary>
    public static IServiceCollection AddPersistenceCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());

        services.AddScoped<TenantSaveChangesInterceptor>();
        services.AddScoped<AuditableEntityInterceptor>();

        return services;
    }

    /// <summary>
    /// Attaches the shared interceptors to a module DbContext. Every module DbContext registration must call this.
    /// </summary>
    public static DbContextOptionsBuilder UsePersistenceInterceptors(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return optionsBuilder.AddInterceptors(
            serviceProvider.GetRequiredService<TenantSaveChangesInterceptor>(),
            serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
    }
}
