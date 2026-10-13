using SeneCare.Domain.Common;
using SeneCare.Domain.Users;

namespace SeneCare.Domain.Tests;

public class PhoneNumberTests
{
    [Fact]
    public void Placeholder_of_an_erased_person_can_be_stored_but_never_typed_at_sign_in()
    {
        var placeholder = PhoneNumber.Anonymized(42);

        Assert.True(placeholder.IsAnonymized);
        Assert.Equal("+221330000042", placeholder.E164);
        Assert.Equal(placeholder, PhoneNumber.FromStorage(placeholder.E164));
        Assert.False(PhoneNumber.TryParse(placeholder.E164, out _));
        Assert.False(PhoneNumber.Parse("77 123 45 67").IsAnonymized);
    }

    [Fact]
    public void Anonymizing_a_user_removes_identity_consents_and_access()
    {
        var user = User.Register(PhoneNumber.Parse("77 123 45 67"), TestData.Now);
        user.UpdateProfile("Awa Ndiaye", "awa@example.sn", Language.French);
        user.GiveConsent(ConsentType.PersonalData, "v1", TestData.Now);
        user.GrantRole(PlatformRole.Customer, TestData.Now);

        user.Anonymize(PhoneNumber.Anonymized(7), TestData.Now);

        Assert.Null(user.FullName);
        Assert.Null(user.Email);
        Assert.True(user.PhoneNumber.IsAnonymized);
        Assert.False(user.IsActive);
        Assert.Empty(user.Roles);
        Assert.DoesNotContain(user.Consents, c => c.IsActive);
        Assert.Throws<DomainException>(() => user.Anonymize(PhoneNumber.Parse("78 000 00 00"), TestData.Now));
    }

    [Theory]
    [InlineData("77 123 45 67")]
    [InlineData("771234567")]
    [InlineData("+221 77 123 45 67")]
    [InlineData("+221771234567")]
    [InlineData("00221771234567")]
    [InlineData("221 77-123-45-67")]
    public void Accepts_common_senegalese_formats(string input)
    {
        var phone = PhoneNumber.Parse(input);

        Assert.Equal("+221771234567", phone.E164);
        Assert.Equal("77 123 45 67", phone.Formatted);
    }

    [Theory]
    [InlineData("70")]
    [InlineData("75")]
    [InlineData("76")]
    [InlineData("78")]
    public void Accepts_all_mobile_prefixes(string prefix) =>
        Assert.True(PhoneNumber.TryParse(prefix + "1234567", out _));

    [Theory]
    [InlineData("")]
    [InlineData("33 821 00 00")] // landline: cannot receive an OTP SMS
    [InlineData("77 123 45 6")]
    [InlineData("77 123 45 678")]
    [InlineData("+33 6 12 34 56 78")]
    [InlineData("+77 123 45 67")]
    [InlineData("abc")]
    public void Rejects_invalid_numbers(string input)
    {
        Assert.False(PhoneNumber.TryParse(input, out _));
        Assert.Throws<DomainException>(() => PhoneNumber.Parse(input));
    }

    [Fact]
    public void Equal_numbers_are_equal_whatever_the_input_format() =>
        Assert.Equal(PhoneNumber.Parse("77 123 45 67"), PhoneNumber.Parse("+221771234567"));
}

public class MoneyTests
{
    [Fact]
    public void Cannot_be_negative() => Assert.Throws<DomainException>(() => Money.FromFcfa(-1));

    [Fact]
    public void Adds_and_multiplies_whole_amounts()
    {
        var total = Money.FromFcfa(10_000) + (Money.FromFcfa(8_500) * 3);

        Assert.Equal(35_500, total.Amount);
    }

    [Fact]
    public void Subtraction_below_zero_is_refused() =>
        Assert.Throws<DomainException>(() => Money.FromFcfa(100) - Money.FromFcfa(200));
}

public class DateRangeTests
{
    [Fact]
    public void Of_months_covers_whole_calendar_months()
    {
        var quarter = DateRange.OfMonths(new DateOnly(2026, 1, 1), 3);

        Assert.Equal(new DateOnly(2026, 3, 31), quarter.End);
        Assert.Equal(new DateRange(new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)), quarter.Next(3));
    }

    [Fact]
    public void End_before_start_is_refused() =>
        Assert.Throws<DomainException>(() => new DateRange(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1)));
}

public class UserTests
{
    [Fact]
    public void Consent_can_be_given_and_withdrawn()
    {
        var user = User.Register(PhoneNumber.Parse("77 123 45 67"), TestData.Now);

        user.GiveConsent(ConsentType.PersonalData, "2026-10", TestData.Now);
        Assert.True(user.HasActiveConsent(ConsentType.PersonalData));

        user.WithdrawConsent(ConsentType.PersonalData, TestData.Now.AddDays(1));
        Assert.False(user.HasActiveConsent(ConsentType.PersonalData));
        Assert.Single(user.Consents);
    }

    [Fact]
    public void Tours_are_completed_once_and_can_be_reset()
    {
        var user = User.Register(PhoneNumber.Parse("77 123 45 67"), TestData.Now);

        user.CompleteTour("client");
        user.CompleteTour("client");
        Assert.Single(user.CompletedTours);

        user.ResetTour("client");
        Assert.False(user.HasCompletedTour("client"));
    }

    [Fact]
    public void Roles_are_not_duplicated()
    {
        var user = User.Register(PhoneNumber.Parse("78 000 00 00"), TestData.Now);

        user.GrantRole(PlatformRole.Customer, TestData.Now);
        user.GrantRole(PlatformRole.Customer, TestData.Now);

        Assert.Single(user.Roles);
    }
}
