using System.Globalization;
using Serilog;

// The Worker hosts long-running and scheduled work (Hangfire server, uptime scheduler, crawlers).
// It runs as a Windows Service, NOT inside IIS, so app pool recycling cannot kill jobs. See ADR-003.
Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddWindowsService(options => options.ServiceName = "AgencyHub Worker");
    builder.Services.AddSerilog(
        (services, logger) => logger
            .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
            .ReadFrom.Services(services)
            .Enrich.FromLogContext(),
        preserveStaticLogger: true);

    // Hangfire server and job registrations are added in Sprint 2 (see PROJECT_ROADMAP.md).

    using var host = builder.Build();
    await host.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "AgencyHub.Worker terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
