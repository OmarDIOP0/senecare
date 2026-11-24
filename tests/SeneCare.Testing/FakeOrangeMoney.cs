using System.Collections.Concurrent;
using SeneCare.Application.Payments;

namespace SeneCare.Testing;

/// <summary>
/// Orange Money in tests: off by default (Wave-only pages unchanged); a test turns it on, « pays » a reference
/// and the site asks it for the transaction state as it would ask Sonatel.
/// </summary>
public sealed class FakeOrangeMoneyGateway : IOrangeMoneyGateway
{
    public const string CallbackKey = "test-callback-key";

    private readonly ConcurrentDictionary<string, OrangeMoneyTransaction> _transactions = new();

    public bool Configured { get; set; }

    public bool IsConfigured => Configured;

    public string? MerchantCode => "123456";

    public ConcurrentQueue<OrangeMoneyCheckoutRequest> Requests { get; } = new();

    public bool CallbackRegistered { get; private set; }

    public Task<OrangeMoneyCheckout> CreateCheckoutAsync(OrangeMoneyCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Enqueue(request);
        return Task.FromResult(new OrangeMoneyCheckout(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==",
            "https://sugu.orange-sonatel.com/mp/test-maxit",
            "https://sugu.orange-sonatel.com/mp/test-om",
            null,
            DateTimeOffset.UtcNow.AddMinutes(15)));
    }

    public Task<OrangeMoneyTransaction?> FindPaymentAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_transactions.TryGetValue(reference, out var t) ? t : null);

    public Task RegisterCallbackAsync(CancellationToken cancellationToken = default)
    {
        CallbackRegistered = true;
        return Task.CompletedTask;
    }

    public bool IsTrustedCallback(string? authorizationHeader) => authorizationHeader == "Basic " + CallbackKey;

    /// <summary>The customer paid (or not) in Max it: what Orange Money will report for this reference.</summary>
    public void Report(string reference, long amount, string status = "SUCCESS", string? merchant = "123456") =>
        _transactions[reference] = new OrangeMoneyTransaction("MP" + Guid.NewGuid().ToString("N")[..18], reference, status, amount, "XOF", merchant, "MERCHANT_PAYMENT");
}
