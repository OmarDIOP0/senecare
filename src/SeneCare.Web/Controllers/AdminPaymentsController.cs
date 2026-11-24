using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Payments;
using SeneCare.Domain.Billing;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>
/// Finance: Wave payments declared by customers (verify → invoice paid + subscription active, or reject), the Wave
/// merchant link, and the state of the Orange Money API (confirmed by Orange itself). Every decision is audited.
/// </summary>
[Authorize(Policy = Policies.AdminFinance)]
[Route("/admin/paiements")]
public sealed class AdminPaymentsController(
    PaymentVerificationHandler payments,
    IOrangeMoneyGateway orangeMoney,
    Microsoft.Extensions.Options.IOptions<SeneCare.Infrastructure.Payments.OrangeMoneyOptions> orangeMoneyOptions) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken, int page = 1)
    {
        ViewData["WaveLink"] = await payments.WaveLinkAsync(cancellationToken);
        ViewData["OrangeMoney"] = new OrangeMoneyStatus(
            orangeMoney.IsConfigured,
            orangeMoneyOptions.Value.IsLive,
            orangeMoney.MerchantCode,
            Uri.TryCreate(orangeMoneyOptions.Value.PublicBaseUrl, UriKind.Absolute, out var site) && site.Scheme == Uri.UriSchemeHttps
                ? new Uri(site, SeneCare.Infrastructure.Payments.OrangeMoneyOptions.CallbackPath).ToString()
                : null,
            !string.IsNullOrWhiteSpace(orangeMoneyOptions.Value.CallbackApiKey));
        ViewData["Unpaid"] = await payments.UnpaidAsync(page, cancellationToken);
        return View(await payments.PendingAsync(cancellationToken));
    }

    [HttpPost("{id:guid}/valider")]
    public async Task<IActionResult> Verify(Guid id, CancellationToken cancellationToken)
    {
        var result = await payments.VerifyAsync(User.GetUserId()!.Value, id, ClientIp, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["AdminPay_Verified"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/refuser")]
    public async Task<IActionResult> Reject(Guid id, string? reason, CancellationToken cancellationToken)
    {
        var result = await payments.RejectAsync(User.GetUserId()!.Value, id, reason, ClientIp, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["AdminPay_Rejected"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Cash or bank transfer received for an invoice (receipt / bank reference in the note).</summary>
    [HttpPost("factures/{invoiceId:guid}/encaisser")]
    public async Task<IActionResult> RecordManual(Guid invoiceId, PaymentChannel channel, string? note, CancellationToken cancellationToken)
    {
        var result = await payments.RecordManualAsync(User.GetUserId()!.Value, invoiceId, channel, note, ClientIp, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["AdminPay_Recorded"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("lien-wave")]
    public async Task<IActionResult> SetWaveLink(string? link, CancellationToken cancellationToken)
    {
        var result = await payments.SetWaveLinkAsync(User.GetUserId()!.Value, link, ClientIp, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["AdminPay_LinkSaved"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Gives Orange Money our notification address and shared key (to do once per merchant code).</summary>
    [HttpPost("orange-money/notification")]
    public async Task<IActionResult> RegisterOrangeMoneyCallback(CancellationToken cancellationToken)
    {
        try
        {
            await orangeMoney.RegisterCallbackAsync(cancellationToken);
            TempData["flash.toast"] = this.Localizer()["AdminPay_OmRegistered"].Value;
        }
        catch (OrangeMoneyException)
        {
            TempData["flash.toast"] = this.Localizer()["AdminPay_OmRegisterFailed"].Value;
        }

        return RedirectToAction(nameof(Index));
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>Orange Money API as configured on the server (secrets never shown).</summary>
public sealed record OrangeMoneyStatus(bool Configured, bool Live, string? MerchantCode, string? CallbackUrl, bool HasCallbackKey);
