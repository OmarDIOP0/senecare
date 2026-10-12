using System.Globalization;

namespace SeneCare.Domain.Common;

/// <summary>Amount in West African CFA francs (XOF). Always a whole, non-negative number.</summary>
public readonly record struct Money : IComparable<Money>
{
    public const string Currency = "XOF";

    public Money(long amount)
    {
        if (amount < 0)
        {
            throw new DomainException("money.negative", "An amount cannot be negative.");
        }

        Amount = amount;
    }

    public long Amount { get; }

    public static Money Zero => new(0);

    public static Money FromFcfa(long amount) => new(amount);

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);

    public static Money operator *(Money left, int multiplier) => new(left.Amount * multiplier);

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    public static Money Add(Money left, Money right) => left + right;

    public static Money Subtract(Money left, Money right) => left - right;

    public static Money Multiply(Money left, int multiplier) => left * multiplier;

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public override string ToString() => Amount.ToString(CultureInfo.InvariantCulture) + " " + Currency;
}
