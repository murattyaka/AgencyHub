namespace AgencyHub.Modules.Website.Domain;

internal sealed record NormalizedWebsiteUrl(string Url, string Host);

/// <summary>
/// Input-level rules for website URLs (first SSRF layer). Rejects anything that is obviously not a public website:
/// non-HTTP schemes, credentials in the URL, non-standard ports, IP literals, single-label and internal host names.
/// </summary>
/// <remarks>
/// This cannot stop a public DNS name that resolves to an internal address (e.g. "127.0.0.1.nip.io") or DNS rebinding.
/// Those are blocked at connection time by the SafeHttpClient (Sprint 2), which validates the resolved IP of every request.
/// </remarks>
internal static class WebsiteUrlPolicy
{
    public const int MaxLength = 500;

    private static readonly string[] BlockedHostSuffixes =
    [
        ".localhost", ".local", ".internal", ".intranet", ".lan", ".home", ".corp", ".home.arpa", ".localdomain",
    ];

    public static bool TryNormalize(string? input, out NormalizedWebsiteUrl? result, out string error)
    {
        result = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "URL is required.";
            return false;
        }

        var trimmed = input.Trim();
        if (trimmed.Length > MaxLength)
        {
            error = $"URL must be at most {MaxLength} characters.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "URL must be an absolute http or https address.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "URL must not contain a user name or password.";
            return false;
        }

        if (!uri.IsDefaultPort)
        {
            error = "Only the standard ports (80 for http, 443 for https) are allowed.";
            return false;
        }

        if (uri.HostNameType is not UriHostNameType.Dns)
        {
            error = "Use a domain name; IP addresses are not allowed.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            error = "URL must not contain a query string.";
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (!host.Contains('.', StringComparison.Ordinal)
            || host == "localhost"
            || BlockedHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal))
            || host.Split('.').Last().All(char.IsAsciiDigit))
        {
            error = "The host must be a public domain name.";
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        result = new NormalizedWebsiteUrl($"{uri.Scheme}://{host}{path}", host);
        return true;
    }
}
