using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Domain.Users;

namespace SeneCare.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.Property(u => u.PhoneNumber).IsRequired();
        builder.HasIndex(u => u.PhoneNumber).IsUnique();
        builder.Property(u => u.ReferralCode).HasMaxLength(User.ReferralCodeLength);
        builder.HasIndex(u => u.ReferralCode).IsUnique();
        builder.HasIndex(u => u.ReferredByUserId);
        builder.Property(u => u.FullName).HasMaxLength(User.NameMaxLength);
        builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength);
        builder.PrimitiveCollection(u => u.CompletedTours);

        builder.OwnsMany(u => u.Roles, b =>
        {
            b.ToTable("user_roles");
            b.WithOwner().HasForeignKey("user_id");
            b.HasIndex("user_id", nameof(UserRole.Role)).IsUnique();
        });

        builder.OwnsMany(u => u.Consents, b =>
        {
            b.ToTable("user_consents");
            b.WithOwner().HasForeignKey("user_id");
            b.Property(c => c.PolicyVersion).HasMaxLength(20);
            b.Ignore(c => c.IsActive);
        });
    }
}

internal sealed class CustomerAccountConfiguration : IEntityTypeConfiguration<CustomerAccount>
{
    public void Configure(EntityTypeBuilder<CustomerAccount> builder)
    {
        builder.ToTable("customer_accounts");
        builder.Property(a => a.DisplayName).HasMaxLength(CustomerAccount.NameMaxLength);
        builder.Property(a => a.Ninea).HasMaxLength(20);
        builder.Property(a => a.ChosenPlanCode).HasMaxLength(32);

        builder.HasMany(a => a.Members).WithOne().HasForeignKey("account_id").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Members).AutoInclude();
    }
}

internal sealed class AccountMemberConfiguration : IEntityTypeConfiguration<AccountMember>
{
    public void Configure(EntityTypeBuilder<AccountMember> builder)
    {
        builder.ToTable("account_members");
        builder.Property<Guid>("account_id");
        builder.PrimitiveCollection(m => m.PropertyScope);
        builder.Ignore(m => m.HasFullAccess);
        builder.HasIndex("account_id", nameof(AccountMember.UserId)).IsUnique();
        builder.HasIndex(m => m.UserId);
    }
}

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.ToTable("properties");
        builder.Property(p => p.Name).HasMaxLength(Property.NameMaxLength);
        builder.HasIndex(p => p.AccountId);

        builder.OwnsOne(p => p.Address, a =>
        {
            a.Property(x => x.NeighborhoodId).HasColumnName("neighborhood_id");
            a.Property(x => x.Landmark).HasColumnName("landmark").HasMaxLength(Address.LandmarkMaxLength);
            a.Property(x => x.Details).HasColumnName("address_details").HasMaxLength(Address.DetailsMaxLength);
            a.OwnsOne(x => x.Location, g =>
            {
                g.Property(p => p.Latitude).HasColumnName("latitude");
                g.Property(p => p.Longitude).HasColumnName("longitude");
            });
            a.HasIndex(x => x.NeighborhoodId);
        });
        builder.Navigation(p => p.Address).IsRequired();

        builder.OwnsMany(p => p.Photos, OwnedMedia.Configure<Property>("property_photos", "property_id"));
    }
}

/// <summary>
/// Shared mapping for collections of <see cref="MediaAttachment"/>. Owned collections use a generated
/// key (EF default): with a natural composite key, items added to an already-saved owner were
/// taken for existing rows (UPDATE of 0 rows). The natural key stays unique through an index.
/// </summary>
internal static class OwnedMedia
{
    public static Action<OwnedNavigationBuilder<TOwner, MediaAttachment>> Configure<TOwner>(string table, string ownerKey)
        where TOwner : class =>
        b =>
        {
            b.ToTable(table);
            b.WithOwner().HasForeignKey(ownerKey);
            b.HasIndex(ownerKey, nameof(MediaAttachment.MediaFileId)).IsUnique();
        };
}

internal sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.ToTable("media_files");
        builder.Property(m => m.StorageKey).HasMaxLength(300);
        builder.Property(m => m.ContentType).HasMaxLength(100);
        builder.HasIndex(m => m.StorageKey).IsUnique();
        builder.HasIndex(m => m.AccountId);
    }
}
