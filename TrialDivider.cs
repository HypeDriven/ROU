using System.Numerics;

namespace LargePrimeCli;

public sealed class TrialDivider
{
    private readonly int[] _smallPrimes;
    private readonly BigInteger[] _largePrimes;
    private readonly string? _memoryMappedLargePrimeCachePath;
    private readonly BigInteger _largePrimeLimit;

    public TrialDivider(
        int[] smallPrimes,
        BigInteger[] largePrimes,
        string? memoryMappedLargePrimeCachePath,
        BigInteger largePrimeLimit)
    {
        _smallPrimes = smallPrimes;
        _largePrimes = largePrimes;
        _memoryMappedLargePrimeCachePath = memoryMappedLargePrimeCachePath;
        _largePrimeLimit = largePrimeLimit;
    }

    public BigInteger Divide(BigInteger n, Action<BigInteger, string> addFactor, CancellationToken cancellationToken)
    {
        n = DivideBySmallPrimes(n, addFactor, cancellationToken);
        if (n == 1)
            return n;

        return DivideByLargePrimes(n, addFactor, cancellationToken);
    }

    private BigInteger DivideBySmallPrimes(BigInteger n, Action<BigInteger, string> addFactor, CancellationToken cancellationToken)
    {
        foreach (int p in _smallPrimes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BigInteger bp = p;

            if (bp > n / bp)
                break;

            if (n % bp != 0)
                continue;

            do
            {
                addFactor(bp, "Prime factor from small-prime input");
                n /= bp;
            }
            while (n % bp == 0);

            if (n == 1)
                break;
        }

        return n;
    }

    private BigInteger DivideByLargePrimes(BigInteger n, Action<BigInteger, string> addFactor, CancellationToken cancellationToken)
    {
        foreach (BigInteger p in EnumerateLargePrimeInputs())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (p > n / p)
                break;

            if (n % p != 0)
                continue;

            do
            {
                addFactor(p, "Prime factor from large-prime input");
                n /= p;
            }
            while (n % p == 0);

            if (n == 1)
                break;
        }

        return n;
    }

    private IEnumerable<BigInteger> EnumerateLargePrimeInputs()
    {
        foreach (BigInteger prime in _largePrimes)
            yield return prime;

        if (!string.IsNullOrWhiteSpace(_memoryMappedLargePrimeCachePath))
        {
            foreach (BigInteger prime in PrimeUtilities.EnumerateLargePrimesMemoryMapped(
                _memoryMappedLargePrimeCachePath,
                _largePrimeLimit))
            {
                yield return prime;
            }
        }
    }
}
