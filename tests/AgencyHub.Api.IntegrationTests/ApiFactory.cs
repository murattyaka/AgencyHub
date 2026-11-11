using AgencyHub.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyHub.Api.IntegrationTests;

/// <summary>Runs the real API pipeline in memory against the test SQL Server database.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Non-development signing key used when the factory runs in Production.</summary>
    public const string ProductionTestSigningKey = "TEST-production-signing-key-0123456789-abcdefghij";

    private readonly string _environment;
    private readonly Dictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureTestServices;

    /// <summary>Used by xUnit as a class fixture.</summary>
    public ApiFactory(SqlServerTestDatabase database)
        : this(database.ConnectionString)
    {
    }

    /// <summary>For tests that need their own environment, settings or service overrides.</summary>
    internal ApiFactory(
        string connectionString,
        string environment = "Development",
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureTestServices = null)
    {
        _environment = environment;
        _configureTestServices = configureTestServices;
        _settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:Default"] = connectionString,
            // Tests share one client IP; rate-limit behaviour is tested separately with production values.
            ["RateLimiting:GlobalPermitLimitPerMinute"] = "100000",
            ["RateLimiting:AuthenticationPermitLimitPerMinute"] = "100000",
        };

        if (environment == "Production")
        {
            // Production config restricts hosts and writes logs to %AGENCYHUB_LOG_DIR%; neutralise both for the in-memory server.
            _settings["AllowedHosts"] = "*";
            _settings["Serilog:WriteTo:1:Args:path"] = Path.Combine(Path.GetTempPath(), "agencyhub-tests", "api-.json");
            _settings["Jwt:SigningKey"] = ProductionTestSigningKey;
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(_settings));
        if (_configureTestServices is not null)
        {
            builder.ConfigureTestServices(_configureTestServices);
        }
    }
}
