using Microsoft.AspNetCore.Mvc.ViewFeatures;
using SeneCare.Application.Admin;

namespace SeneCare.Web.Infrastructure;

/// <summary>
/// A temporary password handed from an action to the next page (TempData: one read, then gone). Shown by
/// Ui/_TemporaryPassword with « Envoyer par WhatsApp »; never logged, never stored elsewhere.
/// </summary>
public static class TemporaryPasswordPanel
{
    public const string PasswordKey = "staff.password";
    public const string PhoneKey = "staff.phone";
    public const string NameKey = "staff.name";

    public static void Remember(ITempDataDictionary tempData, StaffAdded added)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        ArgumentNullException.ThrowIfNull(added);
        if (added.TemporaryPassword is null)
        {
            return;
        }

        tempData[PasswordKey] = added.TemporaryPassword;
        tempData[PhoneKey] = added.Phone;
        tempData[NameKey] = added.Name ?? added.Phone;
    }
}
