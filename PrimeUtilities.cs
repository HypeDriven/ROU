using System.IO.MemoryMappedFiles;
using System.Numerics;

namespace LargePrimeCli;

public static class PrimeUtilities
{
    public static int[] GenerateSmallPrimes(int max)
    {
        if (max < 2)
            return [];

        if (max >= int.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(max), $"Prime sieve limit {max:N0} is too large for an in-memory .NET array. Use a smaller --max/--pminus1-bound or a segmented/external cache approach.");

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

    public static IEnumerable<int> EnumeratePrimesSegmented(int minExclusive, int max, int segmentSize = 1_000_000)
    {
        if (max < 2 || minExclusive >= max)
            yield break;

        if (segmentSize < 1024)
            throw new ArgumentOutOfRangeException(nameof(segmentSize), "Segment size must be at least 1024.");

        int root = IntegerSquareRoot(max);
        int[] basePrimes = GenerateSmallPrimes(root);
        long startValue = Math.Max(2L, (long)minExclusive + 1L);

        for (long low = startValue; low <= max; low += segmentSize)
        {
            long high = Math.Min(max, low + segmentSize - 1L);
            int length = checked((int)(high - low + 1L));
            bool[] composite = new bool[length];

            foreach (int prime in basePrimes)
            {
                long p = prime;
                long pSquared = p * p;
                if (pSquared > high)
                    break;

                long start = Math.Max(pSquared, ((low + p - 1L) / p) * p);
                for (long multiple = start; multiple <= high; multiple += p)
                    composite[multiple - low] = true;
            }

            for (int offset = 0; offset < length; offset++)
            {
                if (!composite[offset])
                    yield return checked((int)(low + offset));
            }
        }
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

    public static int[] LoadSmallPrimes(string path, int maxPrime)
    {
        if (!File.Exists(path))
            return [];

        var primes = new List<int>();
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            int prime = int.Parse(line);
            if (prime > maxPrime)
                break;

            if (prime >= 2)
                primes.Add(prime);
        }

        return primes.Distinct().OrderBy(p => p).ToArray();
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

    public static BigInteger[] LoadLargePrimes(string path, BigInteger maxPrime)
    {
        return EnumerateLargePrimesMemoryMapped(path, maxPrime)
            .Distinct()
            .OrderBy(p => p)
            .ToArray();
    }

    public static IEnumerable<BigInteger> EnumerateLargePrimesMemoryMapped(string path, BigInteger maxPrime)
    {
        if (!File.Exists(path))
            yield break;

        using MemoryMappedFile mappedFile = MemoryMappedFile.CreateFromFile(
            path,
            FileMode.Open,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read);

        using MemoryMappedViewStream stream = mappedFile.CreateViewStream(
            offset: 0,
            size: 0,
            MemoryMappedFileAccess.Read);

        using var reader = new StreamReader(
            stream,
            System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024 * 1024,
            leaveOpen: false);

        while (reader.ReadLine() is { } rawLine)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            BigInteger prime = BigInteger.Parse(line);
            if (prime > maxPrime)
                break;

            if (prime > int.MaxValue)
                yield return prime;
        }
    }

    public static int IntegerSquareRoot(long n)
    {
        if (n < 0)
            throw new ArgumentOutOfRangeException(nameof(n));

        long root = (long)Math.Sqrt(n);
        while ((root + 1) <= n / (root + 1)) root++;
        while (root > n / root) root--;

        if (root > int.MaxValue - 1L)
            throw new ArgumentOutOfRangeException(nameof(n), $"sqrt({n:N0}) is {root:N0}, which is too large for the current in-memory base-prime sieve.");

        return (int)root;
    }
}
