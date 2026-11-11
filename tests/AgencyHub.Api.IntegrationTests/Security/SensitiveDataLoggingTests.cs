using System.Collections.Concurrent;
using AgencyHub.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace AgencyHub.Api.IntegrationTests.Security;

public sealed class SensitiveDataLoggingTests
{
    private readonly SqlServerTestDatabase _database;

    public SensitiveDataLoggingTests(SqlServerTestDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task Passwords_and_tokens_never_reach_the_logs()
    {
        var sink = new CollectingSink();
        await using var factory = new ApiFactory(
            _database.ConnectionString,
            configureTestServices: services => services.AddSingleton<ILogEventSink>(sink));
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateHttpsClient();

        var failed = await client.LoginAsync(user.Email, "Wrong-Secret-Password-42");
        var login = await client.LoginAsync(user);
        var refreshed = await client.RefreshAsync(login.RefreshCookie);
        using var me = await client.GetMeAsync(refreshed.Body!.AccessToken);
        await client.LogoutAsync(refreshed.RefreshCookie);

        string[] secrets =
        [
            user.Password,
            "Wrong-Secret-Password-42",
            login.RefreshCookie!,
            refreshed.RefreshCookie!,
            login.Body!.AccessToken,
            refreshed.Body.AccessToken,
        ];

        Assert.NotEmpty(sink.Rendered);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(sink.Rendered, line => line.Contains(secret, StringComparison.Ordinal));
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<string> _rendered = new();

        public IReadOnlyCollection<string> Rendered => _rendered;

        public void Emit(LogEvent logEvent)
        {
            var properties = string.Join(" ", logEvent.Properties.Select(p => $"{p.Key}={p.Value}"));
            _rendered.Enqueue($"{logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)} {properties} {logEvent.Exception}");
        }
    }
}
