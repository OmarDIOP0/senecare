using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace SeneCare.Web.Localization;

public static class LocalizationSetup
{
    public const string DefaultCulture = "fr";

    public static readonly string[] SupportedCultures = [DefaultCulture, "wo"];

    public static IServiceCollection AddSeneCareLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var cultures = SupportedCultures.Select(c => new CultureInfo(c)).ToList();
            options.DefaultRequestCulture = new RequestCulture(DefaultCulture);
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
            // Cookie first (user choice), then ?culture= for testing; ignore Accept-Language
            // so that phones set to English still get French by default.
            options.RequestCultureProviders =
            [
                new CookieRequestCultureProvider(),
                new QueryStringRequestCultureProvider(),
            ];
        });

        return services;
    }
}
