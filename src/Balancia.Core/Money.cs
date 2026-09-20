namespace Balancia.Core;

/// <summary>A signed CHF value with exact centime precision. Zero is valid for balances.</summary>
public readonly record struct Money(long Centimes)
{
    public decimal Francs => Centimes / 100m;

    public static Money FromFrancs(decimal francs)
    {
        var centimes = checked(francs * 100m);
        if (decimal.Truncate(centimes) != centimes)
        {
            throw new ArgumentException("Amounts must use whole centimes.", nameof(francs));
        }

        return new Money(checked((long)centimes));
    }

    public static Money operator +(Money left, Money right) =>
        new(checked(left.Centimes + right.Centimes));

    public static Money operator -(Money left, Money right) =>
        new(checked(left.Centimes - right.Centimes));
}
