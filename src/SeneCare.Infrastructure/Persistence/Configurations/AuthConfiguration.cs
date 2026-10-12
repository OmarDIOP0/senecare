using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Users;
using SeneCare.Infrastructure.Identity;

namespace SeneCare.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("identity_users");
        // Same Id as the domain user: an identity record cannot exist without it.
        builder.HasOne<User>().WithOne().HasForeignKey<ApplicationUser>(u => u.Id).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class IdentityUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("identity_user_claims");
}

internal sealed class IdentityUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("identity_user_logins");
}

internal sealed class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("identity_user_tokens");
}

internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges");
        builder.Property(c => c.CodeHash).HasMaxLength(64);
        builder.Property(c => c.IpAddress).HasMaxLength(45);
        // Rate limits look up recent codes per number and per IP.
        builder.HasIndex(c => new { c.PhoneNumber, c.CreatedAt });
        builder.HasIndex(c => new { c.IpAddress, c.CreatedAt });
    }
}

internal sealed class AccountInvitationConfiguration : IEntityTypeConfiguration<AccountInvitation>
{
    public void Configure(EntityTypeBuilder<AccountInvitation> builder)
    {
        builder.ToTable("account_invitations");
        builder.Property(i => i.TokenHash).HasMaxLength(64);
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.AccountId, i.PhoneNumber, i.Status });
        builder.HasOne<CustomerAccount>().WithMany().HasForeignKey(i => i.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.PrimitiveCollection(i => i.PropertyScope);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.Property(t => t.CreatedByIp).HasMaxLength(45);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
