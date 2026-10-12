using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SeneCare.Api.Endpoints;
using SeneCare.Application.Users;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public class AuthApiTests(ApiFixture api)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [PostgresFact]
    public async Task Mobile_sign_up_flow_issues_tokens_and_registers_the_customer()
    {
        var client = api.Factory.CreateClient();
        var phone = TestData.NewPhone();

        var tokens = await SignInAsync(client, phone);
        Assert.True(tokens.IsNewUser);
        Assert.False(tokens.IsRegistrationComplete);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var me = await client.GetFromJsonAsync<UserProfileDto>(new Uri("/api/v1/me", UriKind.Relative), Json);
        Assert.Equal(phone.Formatted, me!.Phone);
        Assert.False(me.IsRegistrationComplete);

        var register = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new RegisterRequest("Awa Ndiaye", AccountKind.Household, null, AcceptsPrivacyPolicy: true, AcceptsWhatsApp: false),
            Json);
        register.EnsureSuccessStatusCode();
        var registered = (await register.Content.ReadFromJsonAsync<TokenResponse>(Json))!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.AccessToken);
        me = await client.GetFromJsonAsync<UserProfileDto>(new Uri("/api/v1/me", UriKind.Relative), Json);
        Assert.True(me!.IsRegistrationComplete);
        Assert.Equal(AccountKind.Household, Assert.Single(me.Memberships).Kind);
    }

    [PostgresFact]
    public async Task Wrong_code_returns_a_problem_with_a_stable_code()
    {
        var client = api.Factory.CreateClient();
        var phone = TestData.NewPhone();
        var otp = await RequestOtpAsync(client, phone);
        var wrong = api.Messages.LastCodeFor(phone) == "000000" ? "111111" : "000000";

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/otp/verify", UriKind.Relative), new VerifyRequest(otp.ChallengeId, wrong), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("otp.invalid", problem.GetProperty("code").GetString());
    }

    [PostgresFact]
    public async Task Requesting_a_second_code_too_soon_returns_429()
    {
        var client = api.Factory.CreateClient();
        var phone = TestData.NewPhone();
        await RequestOtpAsync(client, phone);

        var again = await client.PostAsJsonAsync(new Uri("/api/v1/auth/otp", UriKind.Relative), new OtpRequest(phone.Formatted), Json);

        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
    }

    [PostgresFact]
    public async Task Refresh_token_rotates_and_reuse_revokes_the_whole_family()
    {
        var client = api.Factory.CreateClient();
        var first = await SignInAsync(client, TestData.NewPhone());

        var rotated = await RefreshAsync(client, first.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = (await rotated.Content.ReadFromJsonAsync<TokenResponse>(Json))!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        // Replaying the old token (stolen?) is refused and kills the newer one too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, first.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, second.RefreshToken)).StatusCode);
    }

    [PostgresFact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var client = api.Factory.CreateClient();
        var tokens = await SignInAsync(client, TestData.NewPhone());

        var logout = await client.PostAsJsonAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), new RefreshRequest(tokens.RefreshToken), Json);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, tokens.RefreshToken)).StatusCode);
    }

    private static async Task<OtpResponse> RequestOtpAsync(HttpClient client, PhoneNumber phone)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/otp", UriKind.Relative), new OtpRequest(phone.Formatted), Json);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OtpResponse>(Json))!;
    }

    private async Task<TokenResponse> SignInAsync(HttpClient client, PhoneNumber phone)
    {
        var otp = await RequestOtpAsync(client, phone);
        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/otp/verify", UriKind.Relative), new VerifyRequest(otp.ChallengeId, api.Messages.LastCodeFor(phone)), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Json))!;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), new RefreshRequest(refreshToken), Json);
}
