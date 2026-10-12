using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SeneCare.Api.Endpoints;
using SeneCare.Application;
using SeneCare.Infrastructure;
using SeneCare.Infrastructure.Identity;
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
        .AddInfrastructure(builder.Configuration);

    builder.Services.AddSeneCareIdentity();
    builder.Services.AddJwtAuthentication(builder.Configuration);
    builder.Services.AddAuthorization(options => options.AddSeneCarePolicies());
    var otpPermitLimit = builder.Configuration.GetValue("RateLimits:Otp:PermitLimit", 10);
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(AuthEndpoints.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = otpPermitLimit, Window = TimeSpan.FromMinutes(10) }));
    });

    // Enums travel as text ("Household", "WhatsApp"): readable and stable for the mobile app.
    builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddOpenApi("v1");

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // Liveness (process up) and readiness (database reachable) probes.
    app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => !check.Tags.Contains(SeneCare.Infrastructure.DependencyInjection.ReadinessTag) });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(SeneCare.Infrastructure.DependencyInjection.ReadinessTag) });

    // Every public endpoint lives under /api/v1 so a future v2 can coexist with the mobile app.
    var v1 = app.MapGroup("/api/v1");
    v1.MapGet("/status", () => TypedResults.Ok(new { service = "SeneCare.Api", version = "v1" }))
        .WithName("GetStatus");
    v1.MapAuthEndpoints();

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
