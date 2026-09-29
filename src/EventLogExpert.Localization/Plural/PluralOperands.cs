// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Localization.Plural;

public readonly record struct PluralOperands(decimal N, UInt128 I, int V, int W, UInt128 F, UInt128 T, int C, int E)
{
    private const int DecimalScaleMask = 0x00FF0000;
    private const int DecimalScaleShift = 16;

    public static PluralOperands FromLong(long value)
    {
        UInt128 magnitude = value < 0 ?
            (UInt128)(ulong)-(value + 1) + 1 :
            (ulong)value;

        return new PluralOperands((decimal)magnitude, magnitude, 0, 0, 0, 0, 0, 0);
    }

    public static PluralOperands FromDecimal(decimal value)
    {
        decimal magnitude = value < 0 ? decimal.Negate(value) : value;
        int[] bits = decimal.GetBits(magnitude);
        int scale = (bits[3] & DecimalScaleMask) >> DecimalScaleShift;
        UInt128 unscaledValue = ((UInt128)(uint)bits[2] << 64) | ((UInt128)(uint)bits[1] << 32) | (uint)bits[0];
        UInt128 scaleFactor = Pow10(scale);
        UInt128 integerPart = scaleFactor == 1 ? unscaledValue : unscaledValue / scaleFactor;
        UInt128 fractionPart = scaleFactor == 1 ? 0 : unscaledValue % scaleFactor;

        int visibleFractionDigitCountWithoutTrailingZeros = scale;
        UInt128 fractionWithoutTrailingZeros = fractionPart;

        while (visibleFractionDigitCountWithoutTrailingZeros > 0 && fractionWithoutTrailingZeros % 10 == 0)
        {
            fractionWithoutTrailingZeros /= 10;
            visibleFractionDigitCountWithoutTrailingZeros--;
        }

        return new PluralOperands(
            magnitude,
            integerPart,
            scale,
            visibleFractionDigitCountWithoutTrailingZeros,
            fractionPart,
            fractionWithoutTrailingZeros,
            0,
            0);
    }

    public static PluralOperands FromComponents(decimal mantissa, int compactExponent)
    {
        PluralOperands operands = FromDecimal(mantissa);

        return operands with { C = compactExponent, E = compactExponent };
    }

    private static UInt128 Pow10(int exponent)
    {
        UInt128 result = 1;

        for (int index = 0; index < exponent; index++)
        {
            result *= 10;
        }

        return result;
    }
}
