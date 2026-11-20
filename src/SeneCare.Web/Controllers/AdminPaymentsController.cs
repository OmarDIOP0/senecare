using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Payments;
using SeneCare.Domain.Billing;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>
/// Finance: Wave and Orange Money payments declared by customers (verify → invoice paid + subscription active,
/// or reject), and their merchant settings. Every decision goes to the audit log.
/// </summary>
[Authorize(Policy = Policies.AdminFinance)]
[Route("/admin/paiements")]
public sealed class AdminPaymentsController(PaymentVerificationHandler payments) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken, int page = 1)
    {
        ViewData["WaveLink"] = await payments.WaveLinkAsync(cancellationToken);
        var (orangeCode, orangeLink) = await payments.OrangeMoneyAsync(cancellationToken);
        ViewData["OrangeMoneyCode"] = orangeCode;
        ViewData["OrangeMoneyLink"] = orangeLink;
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

    [HttpPost("orange-money")]
    public async Task<IActionResult> SetOrangeMoney(string? code, string? link, CancellationToken cancellationToken)
    {
        var result = await payments.SetOrangeMoneyAsync(User.GetUserId()!.Value, code, link, ClientIp, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["AdminPay_OrangeSaved"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
