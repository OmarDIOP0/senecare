using SeneCare.Application.Users;

namespace SeneCare.Web.ViewModels;

public sealed class SpaceViewModel
{
    public string? FirstName { get; init; }

    /// <summary>Kind of space = tour key: client, entreprise, technicien, admin.</summary>
    public string Space { get; init; } = TourKeys.Client;

    public string? AccountName { get; init; }

    /// <summary>Current month in Dakar, for seasonal advice (hivernage = July–October).</summary>
    public int Month { get; init; }

    public bool IsRainySeason => Month is >= 7 and <= 10;

    /// <summary>Guided tour configuration (JSON), or null when already seen.</summary>
    public string? TourJson { get; init; }

    /// <summary>Customer data (household space only).</summary>
    public ClientHomeModel? Client { get; init; }
}
