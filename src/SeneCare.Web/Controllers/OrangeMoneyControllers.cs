using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Payments;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>
/// Paying an invoice with Orange Money (Sonatel API): the customer gets a QR code (computer) or a Max it / Orange
/// Money link (phone), pays in the app, and the page follows the payment until Orange Money confirms it.
/// </summary>
[Authorize(Policy = Policies.Billing)]
[Route("/abonnement")]
public sealed class OrangeMoneyController(OrangeMoneyPayments payments) : Controller
{

    /// <summary>« Payer avec Orange Money »: a new payment request for what the invoice still owes.</summary>
    [HttpPost("factures/{invoiceId:guid}/orange-money")]
    public async Task<IActionResult> Start(Guid invoiceId, CancellationToken cancellationToken)
    {
        var result = await payments.StartAsync(User.GetUserId()!.Value, invoiceId, cancellationToken);
        if (!result.IsSuccess)
        {
            TempData["flash.toast"] = this.ErrorMessage(result.Error);
            return Redirect("/abonnement");
        }

        // The QR code and links are kept with the payment until Orange Money's request expires.
        return Redirect(OrangeMoneyPayments.PayPagePath(result.Value!.PaymentId));
    }

    [HttpGet("orange-money/{paymentId:guid}")]
    public async Task<IActionResult> Pay(Guid paymentId, CancellationToken cancellationToken)
    {
        var state = await payments.StateAsync(User.GetUserId()!.Value, paymentId, cancellationToken);
        if (state is null)
        {
            return NotFound();
        }

        if (state == OrangeMoneyState.Paid)
        {
            TempData["flash.toast"] = this.Localizer()["Om_Paid"].Value;
            return Redirect("/abonnement");
        }

        ViewData["State"] = state;
        ViewData["PaymentId"] = paymentId;
        return View(await payments.CheckoutAsync(User.GetUserId()!.Value, paymentId, cancellationToken));
    }

    /// <summary>« Vérifier mon paiement »: asks Orange Money now.</summary>
    [HttpPost("orange-money/{paymentId:guid}/verifier")]
    public async Task<IActionResult> Check(Guid paymentId, CancellationToken cancellationToken)
    {
        var result = await payments.CheckAsync(User.GetUserId()!.Value, paymentId, cancellationToken);
        if (!result.IsSuccess)
        {
            TempData["flash.toast"] = this.ErrorMessage(result.Error);
            return Redirect($"/abonnement/orange-money/{paymentId}");
        }

        TempData["flash.toast"] = this.Localizer()["Om_State_" + result.Value].Value;
        return Redirect(result.Value == OrangeMoneyState.Paid ? "/abonnement" : $"/abonnement/orange-money/{paymentId}");
    }

    /// <summary>Polled by the payment page (database only, never a call to Orange Money).</summary>
    [HttpGet("orange-money/{paymentId:guid}/etat")]
    public async Task<IActionResult> State(Guid paymentId, CancellationToken cancellationToken)
    {
        var state = await payments.StateAsync(User.GetUserId()!.Value, paymentId, cancellationToken);
        return state is null ? NotFound() : Json(new { state = state.Value.ToString() });
    }
}

/// <summary>
/// Orange Money's payment notification (HTTPS, « Authorization: Basic {shared key} »). The body is only a signal:
/// the payment is confirmed by asking Orange Money for its own state (amount, merchant, status), once.
/// 202 = received (also for an unknown reference), 401 = not Orange Money, 503 = Orange unreachable: retried.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("/paiements/orange-money")]
public sealed partial class OrangeMoneyNotificationController(
    OrangeMoneyPayments payments, IOrangeMoneyGateway gateway, ILogger<OrangeMoneyNotificationController> logger) : Controller
{
    [HttpPost("notification")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Notify(CancellationToken cancellationToken)
    {
        if (!gateway.IsTrustedCallback(Request.Headers.Authorization))
        {
            LogRefused(logger);
            return Unauthorized();
        }

        string? reference;
        try
        {
            using var json = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
            reference = json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("reference", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : null;
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            return Accepted();
        }

        try
        {
            await payments.ConfirmAsync(reference, cancellationToken);
        }
        catch (OrangeMoneyException ex)
        {
            LogUnreachable(logger, ex);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Accepted();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orange Money notification refused: wrong or missing key")]
    private static partial void LogRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orange Money notification received, Orange Money unreachable to confirm it")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);
}
