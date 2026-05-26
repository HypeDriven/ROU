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
