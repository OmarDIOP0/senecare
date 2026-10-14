using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.WebEncoders;
using SeneCare.Application;
using SeneCare.Infrastructure;
using SeneCare.Infrastructure.Identity;
using SeneCare.Infrastructure.Persistence;
using SeneCare.Web.Infrastructure;
using SeneCare.Web.Localization;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // preserveStaticLogger: the static bootstrap logger is never frozen, so several hosts can run
    // in one process (integration tests). The app logs through ILogger<T> only.
    builder.Services.AddSerilog(
        (services, configuration) => configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext(),
        preserveStaticLogger: true);

    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration);

    // Authentication: phone + OTP, then a long-lived cookie (no SMS at every visit on mobile data).
    builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
    builder.Services.AddSeneCareIdentity().AddSignInManager();
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.Name = "senecare.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.LoginPath = "/connexion";
        options.LogoutPath = "/deconnexion";
        options.AccessDeniedPath = "/acces-refuse";
    });
    builder.Services.AddAuthorization(options => options.AddSeneCarePolicies());
    builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, RegistrationRedirectHandler>();
    builder.Services.AddSeneCareRateLimits(builder.Configuration);

    // Emit French accents as-is (é, à…) instead of &#xE9; entities: lighter, readable pages.
    builder.Services.Configure<WebEncoderOptions>(options =>
        options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

    builder.Services.AddSeneCareLocalization();
    builder.Services.AddSignalR();
    // Public site texts (Content/site-content.json): sample blocks only shown in development.
    builder.Services.AddSingleton(SeneCare.Web.Infrastructure.SiteContent.Load(
        builder.Environment.ContentRootPath, builder.Environment.WebRootPath ?? string.Empty, builder.Environment.IsDevelopment()));
    builder.Services
        .AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
        .AddViewLocalization()
        .AddDataAnnotationsLocalization(options =>
            options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));

    var app = builder.Build();

    if (!app.Environment.IsDevelopment()
        && app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SeneCare.Application.Auth.OtpOptions>>().Value.IsDevelopmentOnly)
    {
        throw new InvalidOperationException("Otp:SkipVerification and Otp:ShowCodeOnPage are for development only.");
    }

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

    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapStaticAssets();
    // Liveness (process up) and readiness (database reachable) probes.
    app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => !check.Tags.Contains(SeneCare.Infrastructure.DependencyInjection.ReadinessTag) });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(SeneCare.Infrastructure.DependencyInjection.ReadinessTag) });

    app.MapHub<SeneCare.Web.Hubs.TrackingHub>(SeneCare.Web.Hubs.TrackingHub.Path);
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
