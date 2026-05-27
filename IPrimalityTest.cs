using System.Numerics;

namespace LargePrimeCli;

public interface IPrimalityTest
{
    bool IsProbablePrime(BigInteger n, int rounds = 32);
}

public sealed class MillerRabinPrimalityTest : IPrimalityTest
{
    private readonly IRandomSource _random;

    public MillerRabinPrimalityTest(IRandomSource random)
    {
        _random = random;
    }

    public bool IsProbablePrime(BigInteger n, int rounds = 32)
    {
        if (n < 2) return false;
        if (n == 2 || n == 3) return true;
        if (n.IsEven) return false;

        BigInteger d = n - 1;
        int s = 0;
        while (d.IsEven)
        {
            d >>= 1;
            s++;
        }

        int byteLength = n.ToByteArray(isUnsigned: true, isBigEndian: true).Length;

        for (int i = 0; i < rounds; i++)
        {
            BigInteger a = NumberTheoryRandom.Below(_random, n - 3, byteLength) + 2;
            if (!MillerRabinRound(n, d, s, a))
                return false;
        }

        return true;
    }

    internal static bool MillerRabinBase2(BigInteger n)
    {
        if (n < 2) return false;
        if (n == 2 || n == 3) return true;
        if (n.IsEven) return false;

        BigInteger d = n - 1;
        int s = 0;
        while (d.IsEven)
        {
            d >>= 1;
            s++;
        }

        return MillerRabinRound(n, d, s, 2);
    }

    private static bool MillerRabinRound(BigInteger n, BigInteger d, int s, BigInteger a)
    {
        a %= n;
        if (a < 2)
            return true;

        BigInteger x = BigInteger.ModPow(a, d, n);

        if (x == 1 || x == n - 1)
            return true;

        for (int r = 1; r < s; r++)
        {
            x = (x * x) % n;
            if (x == n - 1)
                return true;
        }

        return false;
    }
}

public sealed class BailliePswPrimalityTest : IPrimalityTest
{
    public bool IsProbablePrime(BigInteger n, int rounds = 32)
    {
        if (n < 2) return false;
        foreach (int p in new[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 })
        {
            if (n == p) return true;
            if (n % p == 0) return false;
        }
        if (n.IsEven) return false;
        if (IsPerfectSquare(n)) return false;

        return MillerRabinPrimalityTest.MillerRabinBase2(n)
            && IsStrongLucasSelfridgeProbablePrime(n);
    }

    private static bool IsStrongLucasSelfridgeProbablePrime(BigInteger n)
    {
        long d = 5;
        int sign = 1;

        while (true)
        {
            int jacobi = Jacobi(d, n);
            if (jacobi == -1)
                break;
            if (jacobi == 0)
                return false;

            sign = -sign;
            d = sign > 0 ? -d + 2 : -d - 2;
        }

        BigInteger p = BigInteger.One;
        BigInteger q = (1 - d) / 4;
        BigInteger discriminant = d;

        BigInteger m = n + 1;
        int s = 0;
        while (m.IsEven)
        {
            m >>= 1;
            s++;
        }

        (BigInteger u, BigInteger v, BigInteger qk) = LucasSequence(n, p, q, discriminant, m);
        if (u.IsZero || v.IsZero)
            return true;

        for (int r = 1; r < s; r++)
        {
            v = Mod(v * v - 2 * qk, n);
            qk = Mod(qk * qk, n);
            if (v.IsZero)
                return true;
        }

        return false;
    }

    private static (BigInteger U, BigInteger V, BigInteger Qk) LucasSequence(
        BigInteger modulus,
        BigInteger p,
        BigInteger q,
        BigInteger discriminant,
        BigInteger k)
    {
        BigInteger u = BigInteger.Zero;
        BigInteger v = 2;
        BigInteger qk = BigInteger.One;

        string bits = k.ToString("B");
        foreach (char bit in bits)
        {
            u = Mod(u * v, modulus);
            v = Mod(v * v - 2 * qk, modulus);
            qk = Mod(qk * qk, modulus);

            if (bit == '1')
            {
                BigInteger nextU = p * u + v;
                BigInteger nextV = discriminant * u + p * v;

                if (!nextU.IsEven)
                    nextU += modulus;
                if (!nextV.IsEven)
                    nextV += modulus;

                u = Mod(nextU / 2, modulus);
                v = Mod(nextV / 2, modulus);
                qk = Mod(qk * q, modulus);
            }
        }

        return (u, v, qk);
    }

    private static int Jacobi(BigInteger a, BigInteger n)
    {
        if (n <= 0 || n.IsEven)
            throw new ArgumentOutOfRangeException(nameof(n));

        a = Mod(a, n);
        int result = 1;

        while (a != 0)
        {
            while (a.IsEven)
            {
                a >>= 1;
                BigInteger nMod8 = n % 8;
                if (nMod8 == 3 || nMod8 == 5)
                    result = -result;
            }

            (a, n) = (n, a);
            if (a % 4 == 3 && n % 4 == 3)
                result = -result;
            a %= n;
        }

        return n == 1 ? result : 0;
    }

    private static bool IsPerfectSquare(BigInteger n)
    {
        if (n.Sign < 0)
            return false;

        BigInteger root = IntegerSqrt(n);
        return root * root == n;
    }

    private static BigInteger IntegerSqrt(BigInteger n)
    {
        if (n < 2)
            return n;

        BigInteger x = BigInteger.One << ((BitLength(n) + 1) / 2);

        while (true)
        {
            BigInteger y = (x + n / x) >> 1;
            if (y >= x)
                return x;
            x = y;
        }
    }

    private static int BitLength(BigInteger n)
    {
        byte[] bytes = n.ToByteArray(isUnsigned: true, isBigEndian: true);
        int bits = (bytes.Length - 1) * 8;
        byte top = bytes[0];

        while (top != 0)
        {
            bits++;
            top >>= 1;
        }

        return bits;
    }

    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        BigInteger result = value % modulus;
        return result.Sign < 0 ? result + modulus : result;
    }
}
