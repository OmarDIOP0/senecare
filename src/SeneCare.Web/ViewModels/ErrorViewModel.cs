namespace SeneCare.Web.ViewModels;

public sealed record ErrorViewModel(string? RequestId)
{
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
