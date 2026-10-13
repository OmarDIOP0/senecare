using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Auth;
using SeneCare.Infrastructure.Identity;
using SeneCare.Infrastructure.Messaging;
using SeneCare.Infrastructure.Persistence;
using SeneCare.Infrastructure.Persistence.Seed;
using SeneCare.Infrastructure.Security;
using SeneCare.Infrastructure.Storage;

namespace SeneCare.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";
    public const string ReadinessTag = "ready";

    /// <summary>Registers infrastructure services: PostgreSQL (EF Core), messaging, hashing, seeding, health checks.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SeneCareDbContext>(options => options
            .UseNpgsql(
                configuration.GetConnectionString(ConnectionStringName),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<SeneCareDbContext>());

        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<AdminBootstrapper>();
        services.AddSingleton(TimeProvider.System);

        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.AddSingleton<ISecretHasher, HmacSecretHasher>();
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

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
            _ => throw new InvalidOperationException($"Unknown SMS provider « {sms.Provider} »."),
        });
        services.AddSingleton<IWhatsAppSender>(sp => whatsApp.Provider switch
        {
            "Console" => ActivatorUtilities.CreateInstance<ConsoleWhatsAppSender>(sp),
            _ => throw new InvalidOperationException($"Unknown WhatsApp provider « {whatsApp.Provider} »."),
        });
    }
}
