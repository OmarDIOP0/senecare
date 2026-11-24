using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Auth;
using SeneCare.Application.Technicians;
using SeneCare.Infrastructure.Documents;
using SeneCare.Infrastructure.Identity;
using SeneCare.Infrastructure.Messaging;
using SeneCare.Infrastructure.Persistence;
using SeneCare.Infrastructure.Persistence.Seed;
using SeneCare.Infrastructure.Realtime;
using SeneCare.Infrastructure.Security;
using SeneCare.Infrastructure.Storage;

namespace SeneCare.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";
    public const string ReadinessTag = "ready";

    /// <summary>Registers infrastructure services: SQL Server (EF Core), messaging, hashing, seeding, health checks.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton<Caching.PublicCacheSignals>();
        services.AddSingleton<Caching.PublicCacheInvalidator>();
        services.AddSingleton<QueryDiagnostics>();
        services.AddScoped<NotificationInterceptor>();
        services.AddDbContext<SeneCareDbContext>((sp, options) => options
            .UseSqlServer(
                configuration.GetConnectionString(ConnectionStringName),
                sql => sql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                sp.GetRequiredService<Caching.PublicCacheInvalidator>(),
                sp.GetRequiredService<QueryDiagnostics>(),
                sp.GetRequiredService<NotificationInterceptor>()));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<SeneCareDbContext>());

        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<AdminBootstrapper>();
        services.AddSingleton(TimeProvider.System);

        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.AddSingleton<ISecretHasher, HmacSecretHasher>();
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IPdfRenderer, QuestPdfRenderer>();
        services.AddSingleton<ILivePositions, MemoryLivePositions>();

        // Orange Money (Sonatel): merchant QR code / Max it link, payment confirmation (10 s per call).
        services.Configure<Payments.OrangeMoneyOptions>(configuration.GetSection(Payments.OrangeMoneyOptions.SectionName));
        services.AddSingleton<Payments.OrangeMoneyTokenCache>();
        services.AddHttpClient<SeneCare.Application.Payments.IOrangeMoneyGateway, Payments.OrangeMoneyGateway>((sp, client) =>
        {
            client.BaseAddress = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Payments.OrangeMoneyOptions>>().Value.BaseUrl;
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        AddMessaging(services, configuration);

        services.AddHealthChecks()
            .AddDbContextCheck<SeneCareDbContext>("database", tags: [ReadinessTag]);

        return services;
    }

    /// <summary>
    /// ASP.NET Identity user store (phone + OTP sign-in, no password required). The Web adds
    /// <c>.AddSignInManager()</c> for cookies; the API adds <see cref="AddJwtAuthentication"/>.
    /// </summary>
    public static IdentityBuilder AddSeneCareIdentity(this IServiceCollection services)
    {
        services.AddScoped<UserClaimsBuilder>();
        services.AddScoped<IIdentityService, IdentityService>();

        return services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.User.AllowedUserNameCharacters = "+0123456789";
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
                // Rules are checked in PasswordRules (8 characters, not the phone number): no imposed symbols,
                // a password people can type on any phone keyboard.
                options.Password.RequiredLength = Application.Auth.PasswordRules.MinLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
            })
            .AddEntityFrameworkStores<SeneCareDbContext>()
            .AddClaimsPrincipalFactory<SeneCareUserClaimsPrincipalFactory>();
    }

    /// <summary>JWT bearer authentication + refresh tokens for the REST API.</summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(section);
        services.AddScoped<IAuthTokenService, JwtTokenService>();

        var jwt = section.Get<JwtOptions>() ?? new JwtOptions();
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.GetSecurityKey(),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        return services;
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        var sms = configuration.GetSection(MessagingOptions.SmsSection).Get<MessagingOptions>() ?? new MessagingOptions();
        var whatsApp = configuration.GetSection(MessagingOptions.WhatsAppSection).Get<MessagingOptions>() ?? new MessagingOptions();

        // Real providers (Orange SMS API, aggregator, WhatsApp Business) arrive in Phase 11.
        services.AddSingleton<ISmsSender>(sp => sms.Provider switch
        {
            "Console" => ActivatorUtilities.CreateInstance<ConsoleSmsSender>(sp),
            "None" => ActivatorUtilities.CreateInstance<NoMessageSender>(sp),
            _ => throw new InvalidOperationException($"Unknown SMS provider « {sms.Provider} »."),
        });
        services.AddSingleton<IWhatsAppSender>(sp => whatsApp.Provider switch
        {
            "Console" => ActivatorUtilities.CreateInstance<ConsoleWhatsAppSender>(sp),
            "None" => ActivatorUtilities.CreateInstance<NoMessageSender>(sp),
            _ => throw new InvalidOperationException($"Unknown WhatsApp provider « {whatsApp.Provider} »."),
        });
    }
}
