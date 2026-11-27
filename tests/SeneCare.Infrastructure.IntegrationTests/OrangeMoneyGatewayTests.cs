using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using SeneCare.Application.Payments;
using SeneCare.Domain.Common;
using SeneCare.Infrastructure.Payments;

namespace SeneCare.Infrastructure.IntegrationTests;

/// <summary>The Sonatel client against canned answers shaped like the official documentation (no network).</summary>
public sealed class OrangeMoneyGatewayTests
{
    private sealed class Sonatel : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body, string? Auth, string? Callback)> Calls { get; } = [];

        public Func<string, (HttpStatusCode, string)> Answer { get; set; } = _ => (HttpStatusCode.OK, "{}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            Calls.Add((request.Method, path, body, request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Callback-Url", out var cb) ? cb.Single() : null));
            var (status, json) = path.StartsWith("/oauth/v1/token", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, """{"access_token":"tok-1","token_type":"bearer","expires_in":3600}""")
                : Answer(path);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static (OrangeMoneyGateway Gateway, Sonatel Server) Create(string? callbackKey = "shared-key")
    {
        var server = new Sonatel();
        var options = new OrangeMoneyOptions
        {
            ClientId = "id", ClientSecret = "secret", MerchantCode = "123456", CallbackApiKey = callbackKey, PublicBaseUrl = "https://senecare.net",
        };
        var http = new HttpClient(server) { BaseAddress = options.BaseUrl };
        return (new OrangeMoneyGateway(http, Options.Create(options), new OrangeMoneyTokenCache(), TimeProvider.System), server);
    }

    [Fact]
    public async Task Checkout_sends_the_merchant_amount_and_callback_and_reads_the_links()
    {
        var (gateway, server) = Create();
        var end = DateTimeOffset.UtcNow.AddMinutes(10);
        var endText = end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        server.Answer = _ => (HttpStatusCode.OK, $$$"""
            {"deepLink":"https://sugu.orange-sonatel.com/mp/1","deepLinks":{"MAXIT":"https://sugu.orange-sonatel.com/mp/maxit","OM":"https://sugu.orange-sonatel.com/mp/om"},
             "qrCode":"iVBORw0KGgo=","shortLink":"https://s.orange-sonatel.com/a","qrId":"q1",
             "validFor":{"startDateTime":"2026-11-24T09:00:00Z","endDateTime":"{{{endText}}}"}}
            """);

        var checkout = await gateway.CreateCheckoutAsync(new OrangeMoneyCheckoutRequest("ref1", Money.FromFcfa(18_500), "FAC-1", "/abonnement/orange-money/p1"));

        Assert.Equal("https://sugu.orange-sonatel.com/mp/maxit", checkout.MaxItLink);
        Assert.Equal("https://sugu.orange-sonatel.com/mp/om", checkout.OrangeMoneyLink);
        Assert.Equal("iVBORw0KGgo=", checkout.QrCodePng);
        Assert.Equal(DateTimeOffset.Parse(endText, System.Globalization.CultureInfo.InvariantCulture), checkout.ExpiresAt);
        var call = server.Calls.Single(c => c.Path == "/api/eWallet/v4/qrcode");
        Assert.Equal("Bearer tok-1", call.Auth);
        Assert.Equal("https://senecare.net/paiements/orange-money/notification", call.Callback);
        Assert.Contains("\"code\":\"123456\"", call.Body, StringComparison.Ordinal);
        Assert.Contains("\"value\":18500", call.Body, StringComparison.Ordinal);
        Assert.Contains("\"unit\":\"XOF\"", call.Body, StringComparison.Ordinal);
        Assert.Contains("\"reference\":\"ref1\"", call.Body, StringComparison.Ordinal);
        // Back from Max it: our payment page.
        Assert.Contains("\"callbackSuccessUrl\":\"https://senecare.net/abonnement/orange-money/p1\"", call.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_end_of_validity_already_past_falls_back_to_the_requested_one()
    {
        var (gateway, server) = Create();
        server.Answer = _ => (HttpStatusCode.OK, """
            {"deepLinks":{"MAXIT":"https://sugu.orange-sonatel.com/mp/maxit"},"qrCode":"iVBORw0KGgo=",
             "validFor":{"startDateTime":"2020-01-01T09:00:00Z","endDateTime":"2020-01-01T09:15:00Z"}}
            """);

        var checkout = await gateway.CreateCheckoutAsync(new OrangeMoneyCheckoutRequest("ref2", Money.FromFcfa(8_500), "FAC-2"));

        Assert.True(checkout.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(10));
    }

    [Fact]
    public async Task The_token_is_reused_until_it_expires()
    {
        var (gateway, server) = Create();
        server.Answer = _ => (HttpStatusCode.OK, "[]");
        await gateway.FindPaymentAsync("a");
        await gateway.FindPaymentAsync("b");
        Assert.Single(server.Calls, c => c.Path.StartsWith("/oauth/v1/token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_success_for_our_reference_wins_and_reports_amount_and_merchant()
    {
        var (gateway, server) = Create();
        server.Answer = _ => (HttpStatusCode.OK, """
            [{"reference":"ref1","status":"FAILED","transactionId":"MP1","type":"MERCHANT_PAYMENT","amount":{"value":18500,"unit":"XOF"},"partner":{"idType":"CODE","id":"123456"}},
             {"reference":"ref1","status":"SUCCESS","transactionId":"MP2","type":"MERCHANT_PAYMENT","amount":{"value":18500,"unit":"XOF"},"partner":{"idType":"CODE","id":"123456"}},
             {"reference":"other","status":"SUCCESS","transactionId":"MP3","type":"MERCHANT_PAYMENT","amount":{"value":1,"unit":"XOF"}}]
            """);

        var transaction = await gateway.FindPaymentAsync("ref1");

        Assert.NotNull(transaction);
        Assert.True(transaction.IsSuccess);
        Assert.Equal("MP2", transaction.TransactionId);
        Assert.Equal(18_500, transaction.Amount);
        Assert.Equal("123456", transaction.MerchantCode);
        Assert.Contains(server.Calls, c => c.Path.Contains("reference=ref1", StringComparison.Ordinal) && c.Path.Contains("type=MERCHANT_PAYMENT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Errors_become_orange_money_exceptions_without_the_body()
    {
        var (gateway, server) = Create();
        server.Answer = _ => (HttpStatusCode.BadRequest, """{"status":"400","code":"4099","detail":"secret detail"}""");
        var error = await Assert.ThrowsAsync<OrangeMoneyException>(() => gateway.FindPaymentAsync("x"));
        Assert.Contains("4099", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret detail", error.Message, StringComparison.Ordinal);
        // Finance sees Orange's message in Admin › Paiements (not in the logs).
        Assert.Contains("secret detail", gateway.LastProblem!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_our_shared_key_authenticates_a_notification()
    {
        var (gateway, _) = Create();
        Assert.True(gateway.IsTrustedCallback("Basic shared-key"));
        Assert.True(gateway.IsTrustedCallback("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("shared-key"))));
        Assert.False(gateway.IsTrustedCallback("Basic other"));
        Assert.False(gateway.IsTrustedCallback(null));
        Assert.False(Create(callbackKey: null).Gateway.IsTrustedCallback("Basic "));
    }
}
