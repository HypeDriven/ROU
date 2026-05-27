using System.Numerics;

namespace LargePrimeCli;

public sealed class RhoSplitter
{
    private const int BrentBatchSize = 128;

    private readonly IRandomSource _random;

    public RhoSplitter(IRandomSource random)
    {
        _random = random;
    }

    public BigInteger Split(BigInteger n, CancellationToken cancellationToken)
    {
        if (n.IsEven)
            return 2;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BigInteger y = NumberTheoryRandom.Below(_random, n - 1) + 1;
            BigInteger c = NumberTheoryRandom.Below(_random, n - 1) + 1;
            long m = BrentBatchSize;
            BigInteger g = BigInteger.One;
            long r = 1;
            BigInteger x = BigInteger.Zero;
            BigInteger ys = BigInteger.Zero;
            bool restart = false;

            while (g == BigInteger.One)
            {
                cancellationToken.ThrowIfCancellationRequested();

                x = y;
                for (long i = 0; i < r; i++)
                    y = Step(y, c, n);

                long k = 0;
                while (k < r && g == BigInteger.One)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ys = y;
                    BigInteger q = BigInteger.One;
                    long limit = Math.Min(m, r - k);
                    for (long i = 0; i < limit; i++)
                    {
                        y = Step(y, c, n);
                        q = (q * BigInteger.Abs(x - y)) % n;
                    }

                    g = BigInteger.GreatestCommonDivisor(q, n);
                    k += m;
                }

                if (r > long.MaxValue / 2)
                {
                    restart = true;
                    break;
                }

                r <<= 1;
            }

            if (restart)
                continue;

            if (g == n)
            {
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ys = Step(ys, c, n);
                    g = BigInteger.GreatestCommonDivisor(BigInteger.Abs(x - ys), n);
                }
                while (g == BigInteger.One);
            }

            if (g > 1 && g < n)
                return g;
        }
    }

    private static BigInteger Step(BigInteger x, BigInteger c, BigInteger n)
    {
        return ((x * x) + c) % n;
    }
}
