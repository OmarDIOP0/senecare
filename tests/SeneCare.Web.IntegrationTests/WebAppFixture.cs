using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>The site on a throw-away PostgreSQL database, with SMS/WhatsApp captured in memory.</summary>
public sealed class WebAppFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;

    public IServiceProvider Services => _factory!.Services;

    public PostgresFixture Database { get; } = new();

    public CapturingMessageSender Messages { get; } = new();

    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        if (!PostgresFixture.IsConfigured)
        {
            return;
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in TestData.HostSettings(Database.ConnectionString))
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISmsSender>(Messages);
                services.AddSingleton<IWhatsAppSender>(Messages);
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await Database.DisposeAsync();
    }

    /// <summary>A browser-like client: keeps cookies, HTTPS (secure cookies), does not follow redirects.</summary>
    public HttpClient CreateClient() =>
        _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
}

[CollectionDefinition(Name)]
public sealed class WebAppTestGroup : ICollectionFixture<WebAppFixture>
{
    public const string Name = "web-app";
}

/// <summary>Form posting with the antiforgery token read from the page, like a browser.</summary>
internal static partial class Browser
{
    public static async Task<HttpResponseMessage> SubmitAsync(this HttpClient client, string pageUrl, string postUrl, IDictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(new Uri(pageUrl, UriKind.Relative));
        var token = AntiforgeryToken().Match(page);
        Assert.True(token.Success, $"No antiforgery token on {pageUrl}");

        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value) };
        using var content = new FormUrlEncodedContent(form);
        return await client.PostAsync(new Uri(postUrl, UriKind.Relative), content);
    }

    /// <summary>Redirect target as a local path (cookie auth redirects are absolute URLs).</summary>
    public static string Location(this HttpResponseMessage response)
    {
        var location = response.Headers.Location ?? throw new InvalidOperationException($"Expected a redirect, got {(int)response.StatusCode}.");
        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    /// <summary>Phone → code → (registration redirect). Returns the redirect after the code step.</summary>
    public static async Task<string> SignInAsync(this HttpClient client, CapturingMessageSender messages, PhoneNumber phone, string signInUrl = "/connexion")
    {
        var sent = await client.SubmitAsync(signInUrl, "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Channel"] = "Sms" });
        Assert.Equal("/connexion/code", sent.Location());

        var verified = await client.SubmitAsync("/connexion/code", "/connexion/code", new Dictionary<string, string> { ["Code"] = messages.LastCodeFor(phone) });
        Assert.Equal(HttpStatusCode.Redirect, verified.StatusCode);
        return verified.Location();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
