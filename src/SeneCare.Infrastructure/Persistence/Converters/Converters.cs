using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SeneCare.Domain.Common;
using SeneCare.Domain.Users;

namespace SeneCare.Infrastructure.Persistence.Converters;

/// <summary>Stores <see cref="Money"/> as a bigint amount of FCFA.</summary>
internal sealed class MoneyConverter() : ValueConverter<Money, long>(m => m.Amount, v => new Money(v));

/// <summary>Stores <see cref="PhoneNumber"/> in E.164 form (+221XXXXXXXXX).</summary>
internal sealed class PhoneNumberConverter() : ValueConverter<PhoneNumber, string>(p => p.E164, v => PhoneNumber.Parse(v));
