using System.Numerics;

namespace LargePrimeCli;

public static class PrimeUtilities
{
    public static int[] GenerateSmallPrimes(int max)
    {
        if (max < 2)
            return [];

        bool[] composite = new bool[max + 1];
        var primes = new List<int>();

        for (int n = 2; n <= max; n++)
        {
            if (composite[n])
                continue;

            primes.Add(n);

            if ((long)n * n > max)
                continue;

            for (int multiple = n * n; multiple <= max; multiple += n)
                composite[multiple] = true;
        }

        return primes.ToArray();
    }

    public static int[] LoadSmallPrimes(string path)
    {
        if (!File.Exists(path))
            return [];

        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(int.Parse)
            .Where(p => p >= 2)
            .Distinct()
            .OrderBy(p => p)
            .ToArray();
    }

    public static BigInteger[] LoadLargePrimes(string path)
    {
        if (!File.Exists(path))
            return [];

        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(BigInteger.Parse)
            .Where(p => p > int.MaxValue)
            .Distinct()
            .OrderBy(p => p)
            .ToArray();
    }

    public static int IntegerSquareRoot(long n)
    {
        if (n < 0)
            throw new ArgumentOutOfRangeException(nameof(n));

        long root = (long)Math.Sqrt(n);
        while ((root + 1) <= n / (root + 1)) root++;
        while (root > n / root) root--;
        return checked((int)root);
    }
}
