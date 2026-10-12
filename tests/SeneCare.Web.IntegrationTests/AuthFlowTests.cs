using System.Net;
using Microsoft.EntityFrameworkCore;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

[Collection(WebAppTestGroup.Name)]
public class AuthFlowTests(WebAppFixture app)
{
    [PostgresFact]
    public async Task Household_customer_signs_up_with_phone_otp_and_consent()
    {
        var client = app.CreateClient();
        var phone = TestData.NewPhone();

        Assert.Equal("/inscription", await client.SignInAsync(app.Messages, phone));

        var registered = await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Awa Ndiaye",
            ["Profile"] = nameof(AccountKind.Household),
            ["AcceptsPrivacyPolicy"] = "true",
            ["AcceptsWhatsApp"] = "true",
        });
        Assert.Equal("/", registered.Location());

        var account = await client.GetStringAsync(new Uri("/compte", UriKind.Relative));
        Assert.Contains("Awa Ndiaye", account, StringComparison.Ordinal);
        Assert.Contains(phone.Formatted, account, StringComparison.Ordinal);

        await using var db = app.Database.CreateContext();
        var user = await db.Users.SingleAsync(u => u.PhoneNumber == phone);
        Assert.True(user.HasActiveConsent(ConsentType.PersonalData));
        Assert.True(user.HasActiveConsent(ConsentType.WhatsAppMessages));
        Assert.True(await db.CustomerAccounts.AnyAsync(a => a.Kind == AccountKind.Household && a.Members.Any(m => m.UserId == user.Id)));
    }

    [PostgresFact]
    public async Task Sms_contains_a_6_digit_code_and_stored_hash_is_not_the_code()
    {
        var client = app.CreateClient();
        var phone = TestData.NewPhone();
        await client.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = "+221 " + phone.Formatted });

        var code = app.Messages.LastCodeFor(phone);
        Assert.Matches("^[0-9]{6}$", code);

        await using var db = app.Database.CreateContext();
        var challenge = await db.OtpChallenges.Where(c => c.PhoneNumber == phone).SingleAsync();
        Assert.DoesNotContain(code, challenge.CodeHash, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(5), challenge.ExpiresAt - challenge.CreatedAt);
    }

    [PostgresFact]
    public async Task Invalid_phone_number_is_rejected_with_a_french_message()
    {
        var client = app.CreateClient();

        var response = await client.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = "33 821 00 00" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("n'est pas un mobile sénégalais valide", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Wrong_codes_are_limited_to_five_attempts()
    {
        var client = app.CreateClient();
        var phone = TestData.NewPhone();
        await client.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted });
        var good = app.Messages.LastCodeFor(phone);
        var wrong = good == "000000" ? "111111" : "000000";

        for (var i = 0; i < 4; i++)
        {
            var attempt = await client.SubmitAsync("/connexion/code", "/connexion/code", new Dictionary<string, string> { ["Code"] = wrong });
            Assert.Contains("Code incorrect", WebUtility.HtmlDecode(await attempt.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        }

        var fifth = await client.SubmitAsync("/connexion/code", "/connexion/code", new Dictionary<string, string> { ["Code"] = wrong });
        Assert.Contains("Trop d'essais", WebUtility.HtmlDecode(await fifth.Content.ReadAsStringAsync()), StringComparison.Ordinal);

        // Even the right code is refused now.
        var late = await client.SubmitAsync("/connexion/code", "/connexion/code", new Dictionary<string, string> { ["Code"] = good });
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
    }

    [PostgresFact]
    public async Task A_new_code_cannot_be_requested_within_a_minute()
    {
        var client = app.CreateClient();
        var phone = TestData.NewPhone();
        await client.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted });

        var again = await client.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted });

        Assert.Contains("Patientez une minute", WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Single(app.Messages.Messages, m => m.To == phone);
    }

    [PostgresFact]
    public async Task Registration_requires_consent_and_unregistered_users_are_sent_back_to_it()
    {
        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, TestData.NewPhone());

        var compte = await client.GetAsync(new Uri("/compte", UriKind.Relative));
        Assert.Equal("/inscription", compte.Location());

        var refused = await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Moussa Fall",
            ["Profile"] = nameof(AccountKind.Household),
        });
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("accepter la politique de confidentialité", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Household_customer_cannot_open_the_business_team_page()
    {
        var client = await RegisteredClientAsync(AccountKind.Household, "Ibrahima Sow");

        var team = await client.GetAsync(new Uri("/entreprise/equipe", UriKind.Relative));

        Assert.StartsWith("/acces-refuse", team.Location(), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Business_manager_invites_an_employee_who_joins_with_otp()
    {
        var manager = await RegisteredClientAsync(AccountKind.Business, "Fatou Diop", "Teranga Services");
        var employeePhone = TestData.NewPhone();

        var invited = await manager.SubmitAsync("/entreprise/equipe", "/entreprise/equipe", new Dictionary<string, string>
        {
            ["Invite.Phone"] = employeePhone.Formatted,
            ["Invite.Role"] = nameof(AccountRole.Reporter),
        });
        Assert.Equal("/entreprise/equipe", invited.Location());

        // The employee opens the link received by WhatsApp.
        var link = app.Messages.LastLinkFor(employeePhone);
        var employee = app.CreateClient();
        var invitationPage = await employee.GetStringAsync(new Uri(link.PathAndQuery, UriKind.Relative));
        Assert.Contains("Teranga Services vous invite", WebUtility.HtmlDecode(invitationPage), StringComparison.Ordinal);

        var accept = await employee.SubmitAsync(link.PathAndQuery, link.PathAndQuery, new Dictionary<string, string>());
        Assert.StartsWith("/connexion", accept.Location(), StringComparison.Ordinal);

        Assert.Equal("/inscription", await employee.SignInAsync(app.Messages, employeePhone, accept.Location()));

        var joining = await employee.GetStringAsync(new Uri("/inscription", UriKind.Relative));
        Assert.Contains("Vous rejoignez Teranga Services", WebUtility.HtmlDecode(joining), StringComparison.Ordinal);
        Assert.DoesNotContain("Mon entreprise", WebUtility.HtmlDecode(joining), StringComparison.Ordinal);

        var registered = await employee.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Modou Gueye",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        Assert.Equal("/", registered.Location());

        var team = WebUtility.HtmlDecode(await manager.GetStringAsync(new Uri("/entreprise/equipe", UriKind.Relative)));
        Assert.Contains("Modou Gueye", team, StringComparison.Ordinal);
        Assert.Contains("Employé déclarant", team, StringComparison.Ordinal);

        // Employees are not managers.
        var forbidden = await employee.GetAsync(new Uri("/entreprise/equipe", UriKind.Relative));
        Assert.StartsWith("/acces-refuse", forbidden.Location(), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Signing_out_ends_the_session()
    {
        var client = await RegisteredClientAsync(AccountKind.Household, "Ndeye Faye");

        var signedOut = await client.SubmitAsync("/compte", "/deconnexion", new Dictionary<string, string>());
        Assert.Equal("/", signedOut.Location());

        var compte = await client.GetAsync(new Uri("/compte", UriKind.Relative));
        Assert.StartsWith("/connexion", compte.Location(), StringComparison.Ordinal);
    }

    private async Task<HttpClient> RegisteredClientAsync(AccountKind profile, string name, string? company = null)
    {
        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, TestData.NewPhone());
        var fields = new Dictionary<string, string>
        {
            ["FullName"] = name,
            ["Profile"] = profile.ToString(),
            ["AcceptsPrivacyPolicy"] = "true",
        };
        if (company is not null)
        {
            fields["CompanyName"] = company;
        }

        var registered = await client.SubmitAsync("/inscription", "/inscription", fields);
        Assert.Equal("/", registered.Location());
        return client;
    }
}
