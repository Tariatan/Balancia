namespace Balancia.Core;

/// <summary>A signed currency value with exact centime precision. Zero is valid for balances.</summary>
public readonly record struct Money(long Centimes) : IComparable<Money>
{
    public static readonly Money Zero = new(0);

    public decimal Francs => Centimes / 100m;

    public int CompareTo(Money other) => Centimes.CompareTo(other.Centimes);

    public static Money FromFrancs(decimal francs)
    {
        var centimes = francs * 100m;
        return decimal.Truncate(centimes) == centimes
            ? new Money((long)centimes)
            : throw new ArgumentException("Amounts must use whole centimes.", nameof(francs));
    }

    public static Money operator +(Money left, Money right) =>
        new(checked(left.Centimes + right.Centimes));

    public static Money operator -(Money left, Money right) =>
        new(checked(left.Centimes - right.Centimes));

    public static Money operator -(Money value) =>
        new(checked(-value.Centimes));

    public Money Abs() => new(Math.Abs(Centimes));

    public static bool operator <(Money left, Money right) => left.Centimes < right.Centimes;
    public static bool operator >(Money left, Money right) => left.Centimes > right.Centimes;
    public static bool operator <=(Money left, Money right) => left.Centimes <= right.Centimes;
    public static bool operator >=(Money left, Money right) => left.Centimes >= right.Centimes;
}
