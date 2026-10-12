using System.Globalization;
using SeneCare.Application;
using SeneCare.Infrastructure;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services
        .AddApplication()
        .AddInfrastructure();

    builder.Services.AddOpenApi("v1");
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();

    app.MapHealthChecks("/health");

    // Every public endpoint lives under /api/v1 so a future v2 can coexist with the mobile app.
    var v1 = app.MapGroup("/api/v1");
    v1.MapGet("/status", () => TypedResults.Ok(new { service = "SeneCare.Api", version = "v1" }))
        .WithName("GetStatus");

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "SeneCare.Api terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
