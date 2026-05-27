using System.Numerics;

namespace LargePrimeCli;

public sealed class PMinusOneSplitter
{
    private static readonly int[] Bases =
    [
        2, 3, 5, 7, 11, 13, 17, 19,
        23, 29, 31, 37, 41, 43, 47
    ];

    private const int RandomBaseRetries = 4;
    private const int Stage2BatchSize = 64;
    private const int Stage2PrimeSegmentSize = 1_000_000;

    private readonly long[] _stage1Powers;
    private readonly int _stage1Bound;
    private readonly int _stage2Bound;
    private readonly IRandomSource _random;

    public PMinusOneSplitter(long[] stage1Powers, int stage1Bound, int stage2Bound, IRandomSource random)
    {
        _stage1Powers = stage1Powers;
        _stage1Bound = stage1Bound;
        _stage2Bound = stage2Bound;
        _random = random;
    }

    public BigInteger Split(BigInteger n, CancellationToken cancellationToken)
    {
        foreach (BigInteger seed in BaseSequence(n))
        {
            cancellationToken.ThrowIfCancellationRequested();

            BigInteger a = seed % n;
            if (a <= 1)
                continue;

            BigInteger g = BigInteger.GreatestCommonDivisor(a, n);
            if (g > 1 && g < n)
                return g;

            bool collapsedCompletely = false;

            foreach (long power in _stage1Powers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                a = BigInteger.ModPow(a, power, n);
                g = BigInteger.GreatestCommonDivisor(a - 1, n);

                if (g > 1 && g < n)
                    return g;

                if (g == n)
                {
                    collapsedCompletely = true;
                    break;
                }
            }

            if (collapsedCompletely)
                continue;

            g = Stage2(n, a, cancellationToken);
            if (g > 1 && g < n)
                return g;
        }

        return BigInteger.One;
    }

    private BigInteger Stage2(BigInteger n, BigInteger stage1Value, CancellationToken cancellationToken)
    {
        if (_stage2Bound <= _stage1Bound)
            return BigInteger.One;

        BigInteger product = BigInteger.One;
        var batchPrimes = new List<int>(Stage2BatchSize);

        foreach (int q in PrimeUtilities.EnumeratePrimesSegmented(_stage1Bound, _stage2Bound, Stage2PrimeSegmentSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            BigInteger term = BigInteger.ModPow(stage1Value, q, n) - 1;
            if (term.Sign < 0)
                term += n;

            product = (product * term) % n;
            batchPrimes.Add(q);

            if (batchPrimes.Count == Stage2BatchSize)
            {
                BigInteger g = CheckStage2Batch(n, stage1Value, product, batchPrimes, cancellationToken);
                if (g > 1 && g < n)
                    return g;

                batchPrimes.Clear();
                product = BigInteger.One;
            }
        }

        if (batchPrimes.Count > 0)
        {
            BigInteger g = CheckStage2Batch(n, stage1Value, product, batchPrimes, cancellationToken);
            if (g > 1 && g < n)
                return g;
        }

        return BigInteger.One;
    }

    private static BigInteger CheckStage2Batch(
        BigInteger n,
        BigInteger stage1Value,
        BigInteger product,
        IReadOnlyList<int> batchPrimes,
        CancellationToken cancellationToken)
    {
        BigInteger g = BigInteger.GreatestCommonDivisor(product, n);
        if (g > 1 && g < n)
            return g;

        if (g == n)
        {
            foreach (int q in batchPrimes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                BigInteger term = BigInteger.ModPow(stage1Value, q, n) - 1;
                BigInteger fallback = BigInteger.GreatestCommonDivisor(term, n);

                if (fallback > 1 && fallback < n)
                    return fallback;
            }
        }

        return BigInteger.One;
    }

    private IEnumerable<BigInteger> BaseSequence(BigInteger n)
    {
        foreach (int b in Bases)
            yield return b;

        for (int i = 0; i < RandomBaseRetries; i++)
        {
            if (n <= 5)
                yield break;

            yield return NumberTheoryRandom.Below(_random, n - 3) + 2;
        }
    }
}
