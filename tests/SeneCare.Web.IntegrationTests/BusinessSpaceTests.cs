using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeneCare.Application.Development;
using SeneCare.Application.Subscriptions;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>Company space: offer, sites, sub-roles and approval circuit, diagnostics, exports.</summary>
[Collection(WebAppTestGroup.Name)]
public sealed class BusinessSpaceTests(WebAppFixture app)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [PostgresFact]
    public async Task Manager_chooses_an_offer_covering_every_site_and_sees_the_dashboard()
    {
        var company = await CompanyAsync();

        // No offer yet: the subscription page sends the manager to the offers.
        Assert.Equal("/entreprise/offre", (await company.Manager.GetAsync(new Uri("/abonnement", UriKind.Relative))).Location());
        var offers = WebUtility.HtmlDecode(await company.Manager.GetStringAsync(new Uri("/entreprise/offre", UriKind.Relative)));
        Assert.Contains("Essentiel", offers, StringComparison.Ordinal);
        Assert.Contains("Premium", offers, StringComparison.Ordinal);

        var chosen = await company.Manager.SubmitAsync("/entreprise/offre", "/entreprise/offre", new Dictionary<string, string> { ["plan"] = "PREMIUM" });
        Assert.Equal("/abonnement", chosen.Location());

        var page = Normalize(await company.Manager.GetStringAsync(new Uri("/abonnement", UriKind.Relative)));
        Assert.Contains("10 575 000 FCFA", page, StringComparison.Ordinal); // 75 000 + 3 × 3 500 000

        // A site added later joins the same subscription.
        var third = await AddSiteAsync(company.Manager, "Entrepôt Mbao");
        await using (var db = app.Database.CreateContext())
        {
            var subscription = await db.Subscriptions.SingleAsync(s => s.AccountId == company.AccountId);
            Assert.Equal(3, subscription.CoveredPropertyIds.Count);
            Assert.Contains(third, subscription.CoveredPropertyIds);
            Assert.Equal(3, subscription.BillingPeriodMonths);
        }

        var subscriptionId = await ActivateAsync(company);
        await using (var db = app.Database.CreateContext())
        {
            var diagnostic = await db.Diagnostics.SingleAsync(d => d.SubscriptionId == subscriptionId);
            Assert.Equal(DiagnosticStatus.Planned, diagnostic.Status);
        }

        var home = Normalize(await company.Manager.GetStringAsync(new Uri("/espace", UriKind.Relative)));
        Assert.Contains("Premium", home, StringComparison.Ordinal);
        Assert.Contains("< 2 h", home, StringComparison.Ordinal); // critical delay comes from the plan
        Assert.Contains("Prochain :", home, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Employee_requests_wait_for_the_site_manager_who_approves_or_refuses()
    {
        var company = await CompanyAsync();
        await company.Manager.SubmitAsync("/entreprise/offre", "/entreprise/offre", new Dictionary<string, string> { ["plan"] = "PROFESSIONNEL" });
        await ActivateAsync(company);
        var approval = await company.Manager.SubmitAsync("/entreprise/equipe", "/entreprise/equipe/validation", new Dictionary<string, string> { ["required"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, approval.StatusCode);

        var reporter = await MemberAsync(company, AccountRole.Reporter, company.SiteA);
        var siteManager = await MemberAsync(company, AccountRole.SiteManager, company.SiteA);

        // The employee only sees their site and never the money.
        Assert.Equal(HttpStatusCode.NotFound, (await reporter.GetAsync(new Uri($"/logements/{company.SiteB}", UriKind.Relative))).StatusCode);
        Assert.StartsWith("/acces-refuse", (await reporter.GetAsync(new Uri("/abonnement", UriKind.Relative))).Location(), StringComparison.Ordinal);
        Assert.StartsWith("/acces-refuse", (await reporter.GetAsync(new Uri("/entreprise/exports/factures.csv", UriKind.Relative))).Location(), StringComparison.Ordinal);

        var first = await ReportAsync(reporter, company.SiteA, "Climatiseur de la salle de réunion en panne");
        var second = await ReportAsync(reporter, company.SiteA, "Néon qui clignote dans le couloir");
        await using (var db = app.Database.CreateContext())
        {
            var request = await db.ServiceRequests.SingleAsync(r => r.Id == first);
            Assert.Equal(RequestStatus.AwaitingApproval, request.Status);
            Assert.Null(request.SlaDeadline);
        }

        // Another employee's request is not theirs to approve.
        var refused = await reporter.SubmitAsync("/espace", $"/entreprise/demandes/{first}/approuver", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        await using (var db = app.Database.CreateContext())
        {
            Assert.Equal(RequestStatus.AwaitingApproval, (await db.ServiceRequests.SingleAsync(r => r.Id == first)).Status);
        }

        var home = WebUtility.HtmlDecode(await siteManager.GetStringAsync(new Uri("/espace", UriKind.Relative)));
        Assert.Contains("À valider (2)", home, StringComparison.Ordinal);
        Assert.Contains("Climatiseur de la salle de réunion", home, StringComparison.Ordinal);

        await siteManager.SubmitAsync("/espace", $"/entreprise/demandes/{first}/approuver", new Dictionary<string, string>());
        await siteManager.SubmitAsync("/espace", $"/entreprise/demandes/{second}/refuser", new Dictionary<string, string> { ["reason"] = "Le gardien s'en occupe" });

        await using (var check = app.Database.CreateContext())
        {
            var approved = await check.ServiceRequests.SingleAsync(r => r.Id == first);
            Assert.Equal(RequestStatus.Submitted, approved.Status);
            // PROFESSIONNEL: high urgency answered within 12 h, counted from the approval.
            Assert.Equal(TimeSpan.FromHours(12), approved.SlaDeadline - approved.SubmittedAt);

            var rejected = await check.ServiceRequests.SingleAsync(r => r.Id == second);
            Assert.Equal(RequestStatus.Cancelled, rejected.Status);
            Assert.Contains(rejected.History, h => h.Comment == "Le gardien s'en occupe");
        }
    }

    [PostgresFact]
    public async Task Diagnostic_report_is_readable_as_pdf_and_anomalies_become_requests()
    {
        var company = await CompanyAsync();
        await company.Manager.SubmitAsync("/entreprise/offre", "/entreprise/offre", new Dictionary<string, string> { ["plan"] = "ESSENTIEL" });
        await ActivateAsync(company);

        Guid diagnosticId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<DiagnosticSimulator>().CompleteAsync(company.ManagerId);
            Assert.True(result.IsSuccess, result.Error?.Code);
            diagnosticId = result.Value;
        }

        var page = WebUtility.HtmlDecode(await company.Manager.GetStringAsync(new Uri($"/entreprise/diagnostics/{diagnosticId}", UriKind.Relative)));
        Assert.Contains("Fissure de 40 cm", page, StringComparison.Ordinal);
        Assert.Contains("Siège Plateau", page, StringComparison.Ordinal);

        var pdf = await company.Manager.GetAsync(new Uri($"/entreprise/diagnostics/{diagnosticId}/rapport.pdf", UriKind.Relative));
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..4], StringComparison.Ordinal);

        Guid pointId;
        await using (var db = app.Database.CreateContext())
        {
            var diagnostic = await db.Diagnostics.SingleAsync(d => d.Id == diagnosticId);
            pointId = diagnostic.ControlPoints.First(c => c.Result == ControlPointResult.Anomaly && c.PropertyId == company.SiteB).Id;
        }

        var converted = await company.Manager.SubmitAsync(
            $"/entreprise/diagnostics/{diagnosticId}", $"/entreprise/diagnostics/{diagnosticId}/points/{pointId}/demande", new Dictionary<string, string>());
        Assert.StartsWith("/interventions/", converted.Location(), StringComparison.Ordinal);
        var requestId = Guid.Parse(converted.Location().Split('/')[^1]);

        // Twice is idempotent: the same request.
        var again = await company.Manager.SubmitAsync(
            $"/entreprise/diagnostics/{diagnosticId}", $"/entreprise/diagnostics/{diagnosticId}/points/{pointId}/demande", new Dictionary<string, string>());
        Assert.Equal(converted.Location(), again.Location());

        await using var check = app.Database.CreateContext();
        var request = await check.ServiceRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(company.SiteB, request.PropertyId);
        Assert.Equal(Urgency.Normal, request.Urgency);
        Assert.Equal(RequestStatus.Submitted, request.Status);
        Assert.Equal(requestId, (await check.Diagnostics.SingleAsync(d => d.Id == diagnosticId)).ControlPoints.Single(c => c.Id == pointId).ConvertedRequestId);
    }

    [PostgresFact]
    public async Task Exports_are_excel_ready_and_limited_to_the_members_sites()
    {
        var company = await CompanyAsync();
        await company.Manager.SubmitAsync("/entreprise/offre", "/entreprise/offre", new Dictionary<string, string> { ["plan"] = "ESSENTIEL" });
        await ActivateAsync(company);
        await ReportAsync(company.Manager, company.SiteA, "=HYPERLINK(\"http://pirate\") fuite au plafond");
        await ReportAsync(company.Manager, company.SiteB, "Porte de l'entrepôt bloquée");

        var csv = await company.Manager.GetAsync(new Uri("/entreprise/exports/demandes.csv", UriKind.Relative));
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.StartsWith("N°;Date;Site;Métier;Urgence;Statut", text, StringComparison.Ordinal);
        Assert.Contains("Porte de l'entrepôt bloquée", text, StringComparison.Ordinal);
        Assert.Contains("'=HYPERLINK", text, StringComparison.Ordinal); // never run as a formula

        var reporter = await MemberAsync(company, AccountRole.Reporter, company.SiteA);
        var scoped = Encoding.UTF8.GetString(await reporter.GetByteArrayAsync(new Uri("/entreprise/exports/demandes.csv", UriKind.Relative)));
        Assert.DoesNotContain("Porte de l'entrepôt bloquée", scoped, StringComparison.Ordinal);

        var pdf = await company.Manager.GetAsync(new Uri("/entreprise/exports/demandes.pdf", UriKind.Relative));
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);

        var invoices = Encoding.UTF8.GetString(await company.Manager.GetByteArrayAsync(new Uri("/entreprise/exports/factures.csv", UriKind.Relative)));
        Assert.Contains("Facture;Échéance", invoices, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task A_trade_outside_the_offer_is_refused()
    {
        var company = await CompanyAsync();
        await company.Manager.SubmitAsync("/entreprise/offre", "/entreprise/offre", new Dictionary<string, string> { ["plan"] = "ESSENTIEL" });
        var subscriptionId = await ActivateAsync(company);
        await using (var db = app.Database.CreateContext())
        {
            // Admins configure each offer: here the plan of this subscription stops covering outdoor work.
            var planId = await db.Subscriptions.Where(s => s.Id == subscriptionId).Select(s => s.PlanId).SingleAsync();
            var plan = await db.Plans.SingleAsync(p => p.Id == planId);
            var domains = plan.IncludedDomains.Where(d => d != ServiceDomain.Outdoor).ToList();
            plan.SetIncludedDomains(domains);
            await db.SaveChangesAsync();
        }

        try
        {
            var response = await company.Manager.SubmitMultipartAsync(
                "/signaler", "/signaler",
                [
                    new("PropertyId", company.SiteA.ToString()),
                    new("Domain", "Outdoor"),
                    new("Description", "Tailler la haie devant l'entrée"),
                    new("Urgency", "Planned"),
                    new("ClientReference", Guid.NewGuid().ToString("N")),
                ],
                []);
            Assert.Contains("n'est pas couvert par votre offre", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        }
        finally
        {
            await using var db = app.Database.CreateContext();
            var plan = await db.Plans.SingleAsync(p => p.Code == "ESSENTIEL");
            plan.SetIncludedDomains(Enum.GetValues<ServiceDomain>());
            await db.SaveChangesAsync();
        }
    }

    private sealed record Company(HttpClient Manager, Guid ManagerId, Guid AccountId, Guid SiteA, Guid SiteB);

    private async Task<Company> CompanyAsync()
    {
        var client = app.CreateClient();
        var phone = TestData.NewPhone();
        await client.SignInAsync(app.Messages, phone);
        var registered = await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Ousmane Fall",
            ["Profile"] = "Business",
            ["CompanyName"] = "Teranga Logistique",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        Assert.Equal("/", registered.Location());

        var siteA = await AddSiteAsync(client, "Siège Plateau");
        var siteB = await AddSiteAsync(client, "Dépôt Rufisque");
        await using var db = app.Database.CreateContext();
        var user = await db.Users.SingleAsync(u => u.PhoneNumber == phone);
        var accountId = await db.Properties.Where(p => p.Id == siteA).Select(p => p.AccountId).SingleAsync();
        return new Company(client, user.Id, accountId, siteA, siteB);
    }

    private async Task<Guid> AddSiteAsync(HttpClient client, string name)
    {
        Guid neighborhoodId;
        await using (var db = app.Database.CreateContext())
        {
            neighborhoodId = await db.Neighborhoods.Where(n => n.Name == "Almadies").Select(n => n.Id).SingleAsync();
        }

        var response = await client.SubmitMultipartAsync("/logements/nouveau", "/logements/nouveau", new Dictionary<string, string>
        {
            ["Kind"] = "Office",
            ["Name"] = name,
            ["Landmark"] = "Face à la station Total",
            ["NeighborhoodId"] = neighborhoodId.ToString(),
        }, [("photos", "facade.png", Png)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Location().Split('/')[^1]);
    }

    private async Task<Guid> ActivateAsync(Company company)
    {
        Guid subscriptionId;
        await using (var db = app.Database.CreateContext())
        {
            subscriptionId = await db.Subscriptions.Where(s => s.AccountId == company.AccountId).Select(s => s.Id).SingleAsync();
        }

        await using var scope = app.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<SimulatedActivation>().ActivateAsync(company.ManagerId, subscriptionId);
        Assert.True(result.IsSuccess, result.Error?.Code);
        return subscriptionId;
    }

    /// <summary>A colleague already in the account (as after accepting an invitation), signed in.</summary>
    private async Task<HttpClient> MemberAsync(Company company, AccountRole role, Guid site)
    {
        var phone = TestData.NewPhone();
        await using (var db = app.Database.CreateContext())
        {
            var user = User.Register(phone, DateTimeOffset.UtcNow);
            db.Users.Add(user);
            var account = await db.CustomerAccounts.SingleAsync(a => a.Id == company.AccountId);
            account.AddMember(user.Id, role, [site], DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, phone);
        var registered = await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = role == AccountRole.Reporter ? "Aminata Sow" : "Cheikh Ba",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        return client;
    }

    private static async Task<Guid> ReportAsync(HttpClient client, Guid siteId, string description)
    {
        var response = await client.SubmitMultipartAsync(
            "/signaler", "/signaler",
            [
                new("PropertyId", siteId.ToString()),
                new("Domain", "Electricity"),
                new("Description", description),
                new("Urgency", "High"),
                new("ClientReference", Guid.NewGuid().ToString("N")),
            ],
            [("photos", "panne.png", Png)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Location().Split('/')[^1]);
    }

    private static string Normalize(string html) => WebUtility.HtmlDecode(html).Replace(' ', ' ').Replace(' ', ' ');
}
