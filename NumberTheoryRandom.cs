using System.Numerics;

namespace LargePrimeCli;

internal static class NumberTheoryRandom
{
    public static BigInteger Below(IRandomSource random, BigInteger maxExclusive)
    {
        int byteLength = maxExclusive.ToByteArray(isUnsigned: true, isBigEndian: true).Length;
        return Below(random, maxExclusive, byteLength);
    }

    public static BigInteger Below(IRandomSource random, BigInteger maxExclusive, int byteLength)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));

        byte[] bytes = new byte[byteLength];

        while (true)
        {
            random.FillBytes(bytes);
            BigInteger value = new(bytes, isUnsigned: true, isBigEndian: true);
            if (value < maxExclusive)
                return value;
        }
    }
}
