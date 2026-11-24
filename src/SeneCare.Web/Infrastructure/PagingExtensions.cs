using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Common;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Infrastructure;

/// <summary>Paged lists: the view gets the items, « Précédent / Suivant » comes from ViewData (Ui/_PagerSlot).</summary>
public static class PagingExtensions
{
    public const string Key = "Pager";

    public static IReadOnlyList<T> Paged<T>(this Controller controller, Page<T> page)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(page);
        controller.ViewData[Key] = new PagerModel(page.Number, page.HasNext);
        return page.Items;
    }
}
