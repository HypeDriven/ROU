using System.Numerics;
using System.Security.Cryptography;

namespace LargePrimeCli;

public sealed class LargePrimeSearcher
{
    private readonly int[] _smallKnownPrimes;
    private readonly BigInteger[] _largeKnownPrimes;

    public LargePrimeSearcher(
        IEnumerable<int> smallKnownPrimes,
        IEnumerable<BigInteger>? largeKnownPrimes = null)
    {
        _smallKnownPrimes = smallKnownPrimes
            .Where(p => p >= 2)
            .Distinct()
            .OrderBy(p => p)
            .ToArray();

        if (_smallKnownPrimes.Length == 0 || _smallKnownPrimes[0] != 2)
            throw new ArgumentException("smallKnownPrimes must include 2.");

        _largeKnownPrimes = (largeKnownPrimes ?? Enumerable.Empty<BigInteger>())
            .Where(p => p > int.MaxValue)
            .Distinct()
            .OrderBy(p => p)
            .ToArray();
    }

    public async Task<BigInteger> FindPrimeAsync(
        int bits,
        int millerRabinRounds = 64,
        CancellationToken cancellationToken = default)
    {
        if (bits < 16)
            throw new ArgumentOutOfRangeException(nameof(bits), "Bit length must be at least 16.");

        if (bits <= 62)
            return FindSmallPrime(bits);

        int qBits = (bits / 2) + 2;

        BigInteger q = await FindPrimeAsync(
            qBits,
            millerRabinRounds,
            cancellationToken);

        return await FindPocklingtonPrimeAsync(
            bits,
            q,
            millerRabinRounds,
            cancellationToken);
    }

    private async Task<BigInteger> FindPocklingtonPrimeAsync(
        int bits,
        BigInteger q,
        int millerRabinRounds,
        CancellationToken cancellationToken)
    {
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        CancellationToken token = linkedCts.Token;
        int workers = Environment.ProcessorCount;

        var tasks = new Task<BigInteger>[workers];

        for (int i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    BigInteger k = RandomKForBits(bits, q);
                    BigInteger n = 2 * k * q + 1;

                    if (BitLength(n) != bits)
                        continue;

                    if (!PassesKnownPrimeFilters(n))
                        continue;

                    if (!IsProbablePrime(n, millerRabinRounds))
                        continue;

                    if (!ProvePrimePocklington(n, q))
                        continue;

                    linkedCts.Cancel();
                    return n;
                }

                token.ThrowIfCancellationRequested();
                return BigInteger.Zero;
            }, token);
        }

        Task<BigInteger> completed = await Task.WhenAny(tasks);
        return await completed;
    }

    private static bool ProvePrimePocklington(BigInteger n, BigInteger q)
    {
        if (n < 2 || q < 2)
            return false;

        if ((n - 1) % q != 0)
            return false;

        if (q * q <= n)
            return false;

        for (int a = 2; a < 100; a++)
        {
            BigInteger A = a;

            if (BigInteger.ModPow(A, n - 1, n) != 1)
                continue;

            BigInteger test =
                BigInteger.ModPow(A, (n - 1) / q, n) - 1;

            BigInteger g = BigInteger.GreatestCommonDivisor(test, n);

            if (g == 1)
                return true;
        }

        return false;
    }

    private bool PassesKnownPrimeFilters(BigInteger n)
    {
        foreach (int p in _smallKnownPrimes)
        {
            if (n == p)
                return true;

            if (n % p == 0)
                return false;
        }

        foreach (BigInteger p in _largeKnownPrimes)
        {
            if (p * p > n)
                break;

            if (n == p)
                return true;

            if (n % p == 0)
                return false;
        }

        return true;
    }

    private static bool IsProbablePrime(BigInteger n, int rounds)
    {
        if (n < 2)
            return false;

        if (n == 2 || n == 3)
            return true;

        if (n.IsEven)
            return false;

        BigInteger d = n - 1;
        int s = 0;

        while (d.IsEven)
        {
            d >>= 1;
            s++;
        }

        int byteLength = n.ToByteArray(
            isUnsigned: true,
            isBigEndian: true).Length;

        for (int i = 0; i < rounds; i++)
        {
            BigInteger a =
                RandomBigIntegerBelow(n - 3, byteLength) + 2;

            BigInteger x = BigInteger.ModPow(a, d, n);

            if (x == 1 || x == n - 1)
                continue;

            bool passed = false;

            for (int r = 1; r < s; r++)
            {
                x = BigInteger.ModPow(x, 2, n);

                if (x == n - 1)
                {
                    passed = true;
                    break;
                }
            }

            if (!passed)
                return false;
        }

        return true;
    }

    private static BigInteger RandomKForBits(int bits, BigInteger q)
    {
        int kBits = bits - BitLength(q) - 1;

        if (kBits < 2)
            kBits = 2;

        while (true)
        {
            BigInteger k = RandomOddBigInteger(kBits);

            if (k < q / 2)
                return k;
        }
    }

    private static BigInteger RandomOddBigInteger(int bits)
    {
        int byteLength = (bits + 7) / 8;
        byte[] bytes = new byte[byteLength];

        RandomNumberGenerator.Fill(bytes);

        int excessBits = byteLength * 8 - bits;
        bytes[0] &= (byte)(0xFF >> excessBits);
        bytes[0] |= (byte)(1 << (7 - excessBits));
        bytes[^1] |= 1;

        return new BigInteger(
            bytes,
            isUnsigned: true,
            isBigEndian: true);
    }

    private static BigInteger RandomBigIntegerBelow(
        BigInteger maxExclusive,
        int byteLength)
    {
        byte[] bytes = new byte[byteLength];

        while (true)
        {
            RandomNumberGenerator.Fill(bytes);

            BigInteger value = new(
                bytes,
                isUnsigned: true,
                isBigEndian: true);

            if (value < maxExclusive)
                return value;
        }
    }

    private static int BitLength(BigInteger n)
    {
        if (n.Sign <= 0)
            return 0;

        byte[] bytes = n.ToByteArray(
            isUnsigned: true,
            isBigEndian: true);

        int bits = (bytes.Length - 1) * 8;
        byte top = bytes[0];

        while (top != 0)
        {
            bits++;
            top >>= 1;
        }

        return bits;
    }

    private static BigInteger FindSmallPrime(int bits)
    {
        while (true)
        {
            BigInteger n = RandomOddBigInteger(bits);

            if (IsDeterministicPrime64((ulong)n))
                return n;
        }
    }

    private static bool IsDeterministicPrime64(ulong n)
    {
        if (n < 2)
            return false;

        foreach (ulong p in new ulong[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 })
        {
            if (n == p)
                return true;

            if (n % p == 0)
                return false;
        }

        ulong d = n - 1;
        int s = 0;

        while ((d & 1) == 0)
        {
            d >>= 1;
            s++;
        }

        foreach (ulong a in new ulong[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 })
        {
            if (a >= n)
                continue;

            BigInteger x = BigInteger.ModPow(a, d, n);

            if (x == 1 || x == n - 1)
                continue;

            bool passed = false;

            for (int r = 1; r < s; r++)
            {
                x = BigInteger.ModPow(x, 2, n);

                if (x == n - 1)
                {
                    passed = true;
                    break;
                }
            }

            if (!passed)
                return false;
        }

        return true;
    }
}
