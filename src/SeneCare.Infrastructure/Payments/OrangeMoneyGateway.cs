using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SeneCare.Application.Payments;

namespace SeneCare.Infrastructure.Payments;

/// <summary>
/// Orange Money (Sonatel) settings: all in user-secrets / appsettings.Server.json, never in the repository.
/// Environment « Sandbox » (default) or « Live ».
/// </summary>
public sealed class OrangeMoneyOptions
{
    public const string SectionName = "OrangeMoney";

    public string Environment { get; set; } = "Sandbox";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Merchant code (6 digits) given by Sonatel.</summary>
    public string? MerchantCode { get; set; }

    public string MerchantName { get; set; } = "SeneCare";

    /// <summary>Shared secret registered with our callback: Orange Money sends it back in « Authorization: Basic … ».</summary>
    public string? CallbackApiKey { get; set; }

    /// <summary>Public HTTPS address of the site (https://senecare.net): notification and return addresses.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>How long a QR code / link stays valid (max 86 400 s).</summary>
    public int QrValiditySeconds { get; set; } = 900;

    public bool IsLive => string.Equals(Environment, "Live", StringComparison.OrdinalIgnoreCase);

    public Uri BaseUrl => new(IsLive ? "https://api.orange-sonatel.com" : "https://api.sandbox.orange-sonatel.com");

    public const string CallbackPath = "/paiements/orange-money/notification";
}

/// <summary>The OAuth token, shared by all requests and renewed a minute before it expires.</summary>
public sealed class OrangeMoneyTokenCache : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expires;

    public async Task<string> GetAsync(Func<Task<(string Token, int ExpiresIn)>> fetch, TimeProvider clock)
    {
        if (_token is not null && clock.GetUtcNow() < _expires)
        {
            return _token;
        }

        await _lock.WaitAsync();
        try
        {
            if (_token is null || clock.GetUtcNow() >= _expires)
            {
                var (token, expiresIn) = await fetch();
                _token = token;
                _expires = clock.GetUtcNow().AddSeconds(Math.Max(60, expiresIn) - 60);
            }

            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _token = null;

    public void Dispose() => _lock.Dispose();
}

/// <summary>Sonatel Orange Money API: OAuth client credentials, merchant QR code, transaction search, callbacks.</summary>
public sealed class OrangeMoneyGateway(HttpClient http, IOptions<OrangeMoneyOptions> options, OrangeMoneyTokenCache tokens, TimeProvider clock) : IOrangeMoneyGateway
{
    private readonly OrangeMoneyOptions _options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ClientId) && !string.IsNullOrWhiteSpace(_options.ClientSecret) && !string.IsNullOrWhiteSpace(_options.MerchantCode);

    public string? MerchantCode => string.IsNullOrWhiteSpace(_options.MerchantCode) ? null : _options.MerchantCode.Trim();

    public async Task<OrangeMoneyCheckout> CreateCheckoutAsync(OrangeMoneyCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validity = Math.Clamp(_options.QrValiditySeconds, 60, 86_400);
        var body = new Dictionary<string, object?>
        {
            ["code"] = MerchantCode,
            ["name"] = _options.MerchantName,
            ["amount"] = new { value = request.Amount.Amount, unit = "XOF" },
            ["validity"] = validity,
            ["reference"] = request.Reference,
            ["metadata"] = new { invoice = request.Label },
        };
        if (PublicUrl("/abonnement?orange-money=ok") is { } success)
        {
            body["callbackSuccessUrl"] = success;
            body["callbackCancelUrl"] = PublicUrl("/abonnement?orange-money=annule");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/eWallet/v4/qrcode") { Content = JsonContent.Create(body) };
        if (PublicUrl(OrangeMoneyOptions.CallbackPath) is { } callback)
        {
            message.Headers.Add("X-Callback-Url", callback);
        }

        using var json = await SendAsync(message, cancellationToken);
        var root = json.RootElement;
        var links = root.TryGetProperty("deepLinks", out var l) && l.ValueKind == JsonValueKind.Object ? l : default;
        var expires = root.TryGetProperty("validFor", out var v) && v.TryGetProperty("endDateTime", out var end) && end.TryGetDateTimeOffset(out var at)
            ? at
            : clock.GetUtcNow().AddSeconds(validity);
        return new OrangeMoneyCheckout(
            Text(root, "qrCode"),
            Text(links, "MAXIT") ?? Text(root, "deepLink"),
            Text(links, "OM") ?? Text(root, "deepLink"),
            Text(root, "shortLink"),
            expires);
    }

    public async Task<OrangeMoneyTransaction?> FindPaymentAsync(string reference, CancellationToken cancellationToken = default)
    {
        var query = $"/api/eWallet/v1/transactions?reference={Uri.EscapeDataString(reference)}&type=MERCHANT_PAYMENT&size=20";
        using var message = new HttpRequestMessage(HttpMethod.Get, query);
        using var json = await SendAsync(message, cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var found = json.RootElement.EnumerateArray()
            .Where(t => Text(t, "reference") == reference)
            .Select(Read)
            .ToList();
        // A success wins over earlier failed attempts with the same reference; otherwise the latest state.
        return found.FirstOrDefault(t => t.IsSuccess) ?? found.LastOrDefault();
    }

    public async Task RegisterCallbackAsync(CancellationToken cancellationToken = default)
    {
        var callback = PublicUrl(OrangeMoneyOptions.CallbackPath) ?? throw new OrangeMoneyException("OrangeMoney:PublicBaseUrl is not set.");
        if (string.IsNullOrWhiteSpace(_options.CallbackApiKey))
        {
            throw new OrangeMoneyException("OrangeMoney:CallbackApiKey is not set.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/notification/v1/merchantcallback")
        {
            Content = JsonContent.Create(new { code = MerchantCode, name = _options.MerchantName, callbackUrl = callback, apiKey = _options.CallbackApiKey }),
        };
        using var _ = await SendAsync(message, cancellationToken);
    }

    public bool IsTrustedCallback(string? authorizationHeader)
    {
        var key = _options.CallbackApiKey;
        if (string.IsNullOrWhiteSpace(key) || authorizationHeader is null || !authorizationHeader.StartsWith("Basic ", StringComparison.Ordinal))
        {
            return false;
        }

        var sent = Encoding.UTF8.GetBytes(authorizationHeader[6..].Trim());
        // Sent verbatim (documentation), Base64-encoded accepted too: both need the key.
        return CryptographicOperations.FixedTimeEquals(sent, Encoding.UTF8.GetBytes(key))
            || CryptographicOperations.FixedTimeEquals(sent, Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(key))));
    }

    private string? PublicUrl(string path) =>
        Uri.TryCreate(_options.PublicBaseUrl, UriKind.Absolute, out var root) && root.Scheme == Uri.UriSchemeHttps
            ? new Uri(root, path).ToString()
            : null;

    /// <summary>Authenticated call; a 401 renews the token once. Errors carry the status and Orange's code, never the body.</summary>
    private async Task<JsonDocument> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken, bool retried = false)
    {
        if (!IsConfigured)
        {
            throw new OrangeMoneyException("Orange Money is not configured.");
        }

        HttpResponseMessage response;
        try
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(() => TokenAsync(cancellationToken), clock));
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new OrangeMoneyException("Orange Money unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OrangeMoneyException("Orange Money did not answer in time.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized && !retried)
            {
                tokens.Invalidate();
                using var again = await CloneAsync(message);
                return await SendAsync(again, cancellationToken, retried: true);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new OrangeMoneyException($"Orange Money answered {(int)response.StatusCode} (code {ErrorCode(content)}).");
            }

            return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
        }
    }

    private async Task<(string Token, int ExpiresIn)> TokenAsync(CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId!,
            ["client_secret"] = _options.ClientSecret!,
            ["grant_type"] = "client_credentials",
        });
        using var response = await http.PostAsync(new Uri("/oauth/v1/token", UriKind.Relative), form, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new OrangeMoneyException($"Orange Money token refused: {(int)response.StatusCode} (code {ErrorCode(content)}).");
        }

        using var json = JsonDocument.Parse(content);
        var token = Text(json.RootElement, "access_token") ?? throw new OrangeMoneyException("Orange Money token missing.");
        var expiresIn = json.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 3600;
        return (token, expiresIn);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage message)
    {
        var copy = new HttpRequestMessage(message.Method, message.RequestUri);
        foreach (var header in message.Headers.Where(h => h.Key != "Authorization"))
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (message.Content is not null)
        {
            var bytes = await message.Content.ReadAsByteArrayAsync();
            copy.Content = new ByteArrayContent(bytes);
            foreach (var header in message.Content.Headers)
            {
                copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return copy;
    }

    private static OrangeMoneyTransaction Read(JsonElement t)
    {
        var amount = t.TryGetProperty("amount", out var a) ? a : default;
        var value = amount.ValueKind == JsonValueKind.Object && amount.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number
            ? (long)Math.Round(v.GetDecimal())
            : -1;
        var partner = t.TryGetProperty("partner", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        // Our merchant code, when Orange identifies the payee by its code.
        var merchant = Text(partner, "idType") == "CODE" ? Text(partner, "id") : null;
        return new OrangeMoneyTransaction(
            Text(t, "transactionId") ?? string.Empty,
            Text(t, "reference") ?? string.Empty,
            Text(t, "status") ?? string.Empty,
            value,
            Text(amount, "unit") ?? "XOF",
            merchant,
            Text(t, "type"));
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static string ErrorCode(string content)
    {
        try
        {
            using var json = JsonDocument.Parse(content);
            return Text(json.RootElement, "code") ?? "?";
        }
        catch (JsonException)
        {
            return "?";
        }
    }
}
