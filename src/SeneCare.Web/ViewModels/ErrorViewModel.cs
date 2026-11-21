namespace SeneCare.Web.ViewModels;

/// <param name="RequestId">Reference shown to the user.</param>
/// <param name="Detail">Test site only (Staging): the cause, secrets masked. Never in Production.</param>
public sealed record ErrorViewModel(string? RequestId, string? Detail = null)
{
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
