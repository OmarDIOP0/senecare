using System.Net;
using Microsoft.EntityFrameworkCore;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>Villas have their own offer (user decision of 25/11/2026): the kind of home picks it, prices from the database.</summary>
[Collection(WebAppTestGroup.Name)]
public sealed class VillaPlanTests(WebAppFixture app)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [DatabaseFact]
    public async Task A_villa_takes_the_villa_offer_and_a_house_the_home_offer()
    {
        Guid neighborhood;
        await using (var db = app.Database.CreateContext())
        {
            neighborhood = await (
                    from n in db.Neighborhoods
                    join c in db.Communes on n.CommuneId equals c.Id
                    join d in db.Departments on c.DepartmentId equals d.Id
                    join r in db.Regions on d.RegionId equals r.Id
                    where r.Name == "Dakar" && r.IsServed && c.IsServed
                    select n.Id)
                .FirstAsync();
        }

        var customer = app.CreateClient();
        await customer.SignInAsync(app.Messages, TestData.NewPhone());
        await customer.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Awa Ndiaye", ["Profile"] = "Household", ["AcceptsPrivacyPolicy"] = "true",
        });

        var villaName = "Villa " + Guid.NewGuid().ToString("N")[..6];
        var houseName = "Maison " + Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(HttpStatusCode.Redirect, (await AddHomeAsync(customer, "Villa", villaName, neighborhood)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await AddHomeAsync(customer, "House", houseName, neighborhood)).StatusCode);

        await using (var db = app.Database.CreateContext())
        {
            async Task<(string Code, long Monthly)> OfferOfAsync(string name)
            {
                var property = await db.Properties.SingleAsync(p => p.Name == name);
                var subscription = (await db.Subscriptions.Where(s => s.AccountId == property.AccountId).ToListAsync())
                    .Single(s => s.Covers(property.Id));
                var plan = await db.Plans.SingleAsync(p => p.Id == subscription.PlanId);
                return (plan.Code, subscription.MonthlyPrice.Amount);
            }

            var villa = await OfferOfAsync(villaName);
            Assert.Equal("VILLA", villa.Code);
            Assert.Equal(25_000, villa.Monthly);

            var house = await OfferOfAsync(houseName);
            Assert.Equal("HOME", house.Code);
            Assert.Equal(8_500, house.Monthly);
        }
    }

    private static async Task<HttpResponseMessage> AddHomeAsync(HttpClient client, string kind, string name, Guid neighborhoodId) =>
        await client.SubmitMultipartAsync("/logements/nouveau", "/logements/nouveau", new Dictionary<string, string>
        {
            ["Kind"] = kind,
            ["Name"] = name,
            ["NeighborhoodId"] = neighborhoodId.ToString(),
            ["Latitude"] = "14.6928",
            ["Longitude"] = "-17.4467",
        }, [("photos", "facade.png", Png)]);
}
