using AgencyHub.Modules.Website.Domain;

namespace AgencyHub.UnitTests.Website;

public sealed class WebsiteUrlPolicyTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com", "example.com")]
    [InlineData("https://example.com/", "https://example.com", "example.com")]
    [InlineData("  HTTPS://WWW.Example.COM/Shop/  ", "https://www.example.com/Shop", "www.example.com")]
    [InlineData("http://example.com:80/blog", "http://example.com/blog", "example.com")]
    [InlineData("https://example.com:443/#contact", "https://example.com", "example.com")]
    [InlineData("https://bücher.example", "https://xn--bcher-kva.example", "xn--bcher-kva.example")]
    [InlineData("https://example.com./tr", "https://example.com/tr", "example.com")]
    public void Public_websites_are_accepted_and_normalized(string input, string expectedUrl, string expectedHost)
    {
        Assert.True(WebsiteUrlPolicy.TryNormalize(input, out var result, out var error), error);
        Assert.Equal(expectedUrl, result!.Url);
        Assert.Equal(expectedHost, result.Host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("example.com")]                       // not absolute
    [InlineData("ftp://example.com")]                 // scheme
    [InlineData("file:///c:/windows/win.ini")]        // scheme
    [InlineData("javascript:alert(1)")]               // scheme
    [InlineData("https://user:pass@example.com")]     // credentials
    [InlineData("https://example.com:8080")]          // port
    [InlineData("http://example.com:443")]            // port/scheme mismatch
    [InlineData("http://127.0.0.1")]                  // IPv4 literal
    [InlineData("http://2130706433")]                 // decimal IPv4
    [InlineData("http://0x7f000001")]                 // hex IPv4
    [InlineData("http://169.254.169.254")]            // cloud metadata
    [InlineData("http://[::1]")]                      // IPv6 literal
    [InlineData("http://[::ffff:127.0.0.1]")]         // IPv4-mapped IPv6
    [InlineData("http://localhost")]
    [InlineData("http://api.localhost")]
    [InlineData("http://nas.local")]
    [InlineData("http://metadata.google.internal")]
    [InlineData("http://router.home.arpa")]
    [InlineData("http://intranet")]                   // single label
    [InlineData("https://example.com/?q=1")]          // query string
    public void Unsafe_or_invalid_urls_are_rejected(string? input)
    {
        Assert.False(WebsiteUrlPolicy.TryNormalize(input, out var result, out var error));
        Assert.Null(result);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Overlong_urls_are_rejected()
    {
        var url = "https://example.com/" + new string('a', WebsiteUrlPolicy.MaxLength);

        Assert.False(WebsiteUrlPolicy.TryNormalize(url, out _, out _));
    }
}
