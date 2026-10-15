using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Domain.Users;
using static SeneCare.Domain.Tests.TestData;

namespace SeneCare.Domain.Tests;

public class OtpChallengeTests
{
    private static readonly PhoneNumber Phone = PhoneNumber.Parse("77 123 45 67");

    [Fact]
    public void Expires_after_five_minutes()
    {
        var otp = OtpChallenge.Issue(Phone, "hash", OtpChannel.Sms, "1.2.3.4", Now);

        Assert.Equal(OtpVerificationResult.Expired, otp.Verify(true, Now.AddMinutes(5).AddSeconds(1)));
    }

    [Fact]
    public void Is_single_use()
    {
        var otp = OtpChallenge.Issue(Phone, "hash", OtpChannel.Sms, null, Now);

        Assert.Equal(OtpVerificationResult.Success, otp.Verify(true, Now.AddMinutes(1)));
        // Even the right code, a second later: single use (a double send is handled by the page).
        Assert.Equal(OtpVerificationResult.AlreadyUsed, otp.Verify(true, Now.AddMinutes(1).AddSeconds(1)));
    }

    [Fact]
    public void Locks_after_five_wrong_attempts_even_for_the_right_code()
    {
        var otp = OtpChallenge.Issue(Phone, "hash", OtpChannel.WhatsApp, null, Now);

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(OtpVerificationResult.InvalidCode, otp.Verify(false, Now));
        }

        Assert.Equal(OtpVerificationResult.TooManyAttempts, otp.Verify(false, Now));
        Assert.Equal(OtpVerificationResult.TooManyAttempts, otp.Verify(true, Now));
    }
}

public class AccountInvitationTests
{
    private static readonly PhoneNumber EmployeePhone = PhoneNumber.Parse("78 555 66 77");

    [Fact]
    public void Only_a_business_manager_can_invite()
    {
        Assert.Throws<DomainException>(() =>
            AccountInvitation.Create(Household(), UserId, EmployeePhone, AccountRole.Reporter, [], "hash", Now));
        Assert.Throws<DomainException>(() =>
            AccountInvitation.Create(Business(), Guid.CreateVersion7(), EmployeePhone, AccountRole.Reporter, [], "hash", Now));
    }

    [Fact]
    public void Accepting_adds_the_member_with_the_invited_role_and_scope()
    {
        var account = Business();
        var site = Guid.CreateVersion7();
        var invitation = AccountInvitation.Create(account, UserId, EmployeePhone, AccountRole.SiteManager, [site], "hash", Now);
        var employee = User.Register(EmployeePhone, Now);

        invitation.Accept(account, employee, Now.AddDays(1));

        var member = account.FindMember(employee.Id)!;
        Assert.Equal(AccountRole.SiteManager, member.Role);
        Assert.True(member.CanAccess(site));
        Assert.Equal(InvitationStatus.Accepted, invitation.Status);
    }

    [Fact]
    public void Cannot_be_accepted_from_another_number_or_after_seven_days()
    {
        var account = Business();
        var invitation = AccountInvitation.Create(account, UserId, EmployeePhone, AccountRole.Reporter, [], "hash", Now);

        var stranger = User.Register(PhoneNumber.Parse("76 000 11 22"), Now);
        Assert.Throws<DomainException>(() => invitation.Accept(account, stranger, Now));

        var employee = User.Register(EmployeePhone, Now);
        Assert.Throws<DomainException>(() => invitation.Accept(account, employee, Now.AddDays(8)));
    }
}
