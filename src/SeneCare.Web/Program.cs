using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using SeneCare.Application;
using SeneCare.Infrastructure;
using SeneCare.Infrastructure.Persistence;
using SeneCare.Web.Localization;
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

    // Emit French accents as-is (é, à…) instead of &#xE9; entities: lighter, readable pages.
    builder.Services.Configure<WebEncoderOptions>(options =>
        options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

    builder.Services.AddSeneCareLocalization();
    builder.Services
        .AddControllersWithViews()
        .AddViewLocalization()
        .AddDataAnnotationsLocalization(options =>
            options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));


    var app = builder.Build();

    await app.Services.InitializeDatabaseAsync(app.Configuration);

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();
    app.UseRequestLocalization();
    app.UseRouting();

    app.UseAuthorization();

    app.MapStaticAssets();
    // Liveness (process up) and readiness (database reachable) probes.
    app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => !check.Tags.Contains(SeneCare.Infrastructure.DependencyInjection.ReadinessTag) });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(SeneCare.Infrastructure.DependencyInjection.ReadinessTag) });

    app.MapControllerRoute(
            name: "areas",
            pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}")
        .WithStaticAssets();
    app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
        .WithStaticAssets();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "SeneCare.Web terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
