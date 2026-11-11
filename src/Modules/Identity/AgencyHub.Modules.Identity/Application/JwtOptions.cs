using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AgencyHub.Modules.Identity.Application;

internal sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Keys starting with this prefix are accepted only in Development.</summary>
    public const string DevelopmentKeyPrefix = "DEV-ONLY-";

    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Production: environment variable Jwt__SigningKey, never appsettings.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);
}

/// <summary>Fails application start-up when token settings are unsafe.</summary>
internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private readonly IHostEnvironment _environment;

    public JwtOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Audience is required.");
        }

        var signingKey = options.SigningKey ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(signingKey) < JwtOptions.MinimumKeyBytes)
        {
            failures.Add($"Jwt:SigningKey must be at least {JwtOptions.MinimumKeyBytes} bytes.");
        }
        else if (!_environment.IsDevelopment() && signingKey.StartsWith(JwtOptions.DevelopmentKeyPrefix, StringComparison.Ordinal))
        {
            failures.Add("Jwt:SigningKey is a development key and cannot be used outside Development.");
        }

        if (options.AccessTokenLifetime < TimeSpan.FromMinutes(1) || options.AccessTokenLifetime > TimeSpan.FromMinutes(60))
        {
            failures.Add("Jwt:AccessTokenLifetime must be between 1 and 60 minutes.");
        }

        if (options.RefreshTokenLifetime < TimeSpan.FromHours(1) || options.RefreshTokenLifetime > TimeSpan.FromDays(30))
        {
            failures.Add("Jwt:RefreshTokenLifetime must be between 1 hour and 30 days.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
