using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Modules.Identity.Application;
using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.Modules.Identity.Domain;
using AgencyHub.Modules.Identity.Endpoints;
using AgencyHub.Modules.Identity.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AgencyHub.Modules.Identity;

public static class IdentityModule
{
    /// <summary>Rate-limit policy the host must register (login, refresh, logout: brute-force protection).</summary>
    public const string AuthenticationRateLimitPolicy = "authentication";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.AddModuleDbContext<IdentityModuleDbContext>(IdentityModuleDbContext.Schema);

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<TokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IIdentityProvisioning, IdentityProvisioning>();
        services.AddValidatorsFromAssemblyContaining<AuthService>(includeInternalTypes: true);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = TokenService.CreateSigningKey(jwt.Value.SigningKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = IdentityClaimTypes.UserId,
                    RoleClaimType = IdentityClaimTypes.Role,
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.AgencyMember, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(IdentityClaimTypes.AgencyId))
            .AddPolicy(AuthorizationPolicies.AgencyEditor, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(IdentityClaimTypes.AgencyId)
                .RequireRole(nameof(AgencyRole.Owner), nameof(AgencyRole.Admin), nameof(AgencyRole.Member)))
            .AddPolicy(AuthorizationPolicies.AgencyAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(IdentityClaimTypes.AgencyId)
                .RequireRole(nameof(AgencyRole.Owner), nameof(AgencyRole.Admin)));

        services.AddHealthChecks().AddDbContextCheck<IdentityModuleDbContext>("identity-db", tags: ["ready"]);

        return services;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints) =>
        IdentityEndpoints.Map(endpoints);
}
