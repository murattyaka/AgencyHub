using AgencyHub.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyHub.Api.IntegrationTests.Security;

/// <summary>Regression guards over the complete endpoint table of the running application.</summary>
public sealed class EndpointSecurityTests : IClassFixture<ApiFactory>
{
    /// <summary>Adding an anonymous endpoint requires updating this list deliberately (and a security review).</summary>
    private static readonly string[] AllowedAnonymousRoutes =
    [
        "/api/v1/agencies/register",
        "/api/v1/auth/login",
        "/api/v1/auth/logout",
        "/api/v1/auth/refresh",
        "/health/live",
        "/health/ready",
        "/openapi/{documentName}.json",
    ];

    private readonly ApiFactory _factory;

    public EndpointSecurityTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Only_allow_listed_endpoints_are_anonymous()
    {
        var anonymous = Endpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedAnonymousRoutes, anonymous);
    }

    [Fact]
    public void No_endpoint_exposes_a_persistence_entity()
    {
        var offending = Endpoints()
            .SelectMany(e => e.Metadata.OfType<IProducesResponseTypeMetadata>().Select(m => (Endpoint: e, m.Type)))
            .Where(x => x.Type is not null && ExpandTypes(x.Type).Any(t => typeof(Entity).IsAssignableFrom(t)))
            .Select(x => $"{x.Endpoint.RoutePattern.RawText} -> {x.Type!.Name}")
            .ToArray();

        Assert.Empty(offending);
    }

    private RouteEndpoint[] Endpoints() =>
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();

    private static IEnumerable<Type> ExpandTypes(Type type)
    {
        yield return type;
        foreach (var argument in type.GetGenericArguments().SelectMany(ExpandTypes))
        {
            yield return argument;
        }

        if (type.IsArray)
        {
            foreach (var element in ExpandTypes(type.GetElementType()!))
            {
                yield return element;
            }
        }
    }
}
