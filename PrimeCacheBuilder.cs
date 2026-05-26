using System.Diagnostics;

namespace LargePrimeCli;

public sealed record PrimeCacheOptions(
    long Max,
    string SmallOutputPath,
    string LargeOutputPath,
    int SegmentSize,
    bool Quiet,
    bool ShowHelp,
    bool EmitPrimes)
{
    public static PrimeCacheOptions? Parse(string[] args)
    {
        long max = 0;
        string smallOutputPath = Path.Combine(".prime-cache", "small-primes.txt");
        string largeOutputPath = Path.Combine(".prime-cache", "large-primes.txt");
        int segmentSize = 1_000_000;
        bool quiet = false;
        bool help = false;
        bool emitPrimes = true;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    help = true;
                    break;

                case "-n":
                case "--max":
                    if (++i >= args.Length || !long.TryParse(args[i], out max)) return null;
                    break;

                case "--small-out":
                    if (++i >= args.Length) return null;
                    smallOutputPath = args[i];
                    break;

                case "--large-out":
                    if (++i >= args.Length) return null;
                    largeOutputPath = args[i];
                    break;

                case "--segment-size":
                    if (++i >= args.Length || !int.TryParse(args[i], out segmentSize)) return null;
                    break;

                case "--no-emit-primes":
                    emitPrimes = false;
                    break;

                case "-q":
                case "--quiet":
                    quiet = true;
                    break;

                default:
                    Console.Error.WriteLine($"Unknown cache argument: {args[i]}");
                    return null;
            }
        }

        if (help)
            return new PrimeCacheOptions(max, smallOutputPath, largeOutputPath, segmentSize, quiet, help, emitPrimes);

        if (max < 2)
        {
            Console.Error.WriteLine("cache --max must be at least 2.");
            return null;
        }

        if (segmentSize < 1024)
        {
            Console.Error.WriteLine("cache --segment-size must be at least 1024.");
            return null;
        }

        return new PrimeCacheOptions(max, smallOutputPath, largeOutputPath, segmentSize, quiet, help, emitPrimes);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
LargePrimeCli cache - create cached prime inputs by enumerating primes from 1 to N

Usage:
  dotnet run -- cache --max <N> [options]

Options:
  -n, --max <N>              Generate every prime <= N
      --small-out <path>     Output file for int-sized primes (default: .prime-cache/small-primes.txt)
      --large-out <path>     Output file for primes > int.MaxValue (default: .prime-cache/large-primes.txt)
      --segment-size <n>     Segmented sieve block size (default: 1000000)
      --no-emit-primes       Do not print each discovered verified prime to stdout
  -q, --quiet                Reduce progress output; primes still print unless --no-emit-primes is used
  -h, --help                 Show this help

Examples:
  dotnet run -- cache --max 1000000
  dotnet run -- cache --max 5000000000 --segment-size 2000000
""");
    }
}

public static class PrimeCacheBuilder
{
    public static void Build(PrimeCacheOptions options, CancellationToken cancellationToken = default)
    {
        string? smallDirectory = Path.GetDirectoryName(options.SmallOutputPath);
        if (!string.IsNullOrWhiteSpace(smallDirectory))
            Directory.CreateDirectory(smallDirectory);

        string? largeDirectory = Path.GetDirectoryName(options.LargeOutputPath);
        if (!string.IsNullOrWhiteSpace(largeDirectory))
            Directory.CreateDirectory(largeDirectory);

        int root = PrimeUtilities.IntegerSquareRoot(options.Max);
        int[] basePrimes = PrimeUtilities.GenerateSmallPrimes(root);

        using var smallWriter = new StreamWriter(options.SmallOutputPath, append: false);
        using var largeWriter = new StreamWriter(options.LargeOutputPath, append: false);

        smallWriter.WriteLine($"# primes <= int.MaxValue generated up to {options.Max:0}");
        largeWriter.WriteLine($"# primes > int.MaxValue generated up to {options.Max:0}");

        long smallCount = 0;
        long largeCount = 0;
        long processedThrough = 1;
        Stopwatch totalTime = Stopwatch.StartNew();
        TimeSpan statsInterval = TimeSpan.FromMinutes(5);
        TimeSpan lastStatsAt = TimeSpan.Zero;

        try
        {
            for (long low = 2; low <= options.Max; low += options.SegmentSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long high = Math.Min(options.Max, low + options.SegmentSize - 1L);
                int length = checked((int)(high - low + 1));
                bool[] composite = new bool[length];

                foreach (int prime in basePrimes)
                {
                    long p = prime;
                    long pSquared = p * p;
                    if (pSquared > high)
                        break;

                    long start = Math.Max(pSquared, ((low + p - 1) / p) * p);
                    for (long multiple = start; multiple <= high; multiple += p)
                        composite[multiple - low] = true;
                }

                for (int offset = 0; offset < length; offset++)
                {
                    long value = low + offset;
                    if (value < 2 || composite[offset])
                        continue;

                    if (value <= int.MaxValue)
                    {
                        smallWriter.WriteLine(value);
                        if (options.EmitPrimes)
                            Console.WriteLine(value);
                        smallCount++;
                    }
                    else
                    {
                        largeWriter.WriteLine(value);
                        if (options.EmitPrimes)
                            Console.WriteLine(value);
                        largeCount++;
                    }
                }

                processedThrough = high;

                if (options.EmitPrimes)
                {
                    if (!options.Quiet)
                        Console.Error.WriteLine($"Processed through {high}; small={smallCount}, large={largeCount}");
                }
                else if (totalTime.Elapsed - lastStatsAt >= statsInterval)
                {
                    WriteCacheStats(totalTime.Elapsed, processedThrough, options.Max, smallCount, largeCount);
                    lastStatsAt = totalTime.Elapsed;
                }
            }
        }
        finally
        {
            totalTime.Stop();
            Console.Error.WriteLine($"Cache builder exited after {totalTime.Elapsed}. Processed through {processedThrough} of {options.Max}.");
            Console.Error.WriteLine($"Cached primes: small={smallCount}, large={largeCount}.");
            Console.Error.WriteLine($"Average speed: {CalculatePrimesPerHour(totalTime.Elapsed, smallCount + largeCount):N0} primes/hour.");
            Console.Error.WriteLine($"Memory usage: managed={FormatBytes(GC.GetTotalMemory(forceFullCollection: false))}, working-set={FormatBytes(Process.GetCurrentProcess().WorkingSet64)}.");
            Console.Error.WriteLine($"Small cache: {options.SmallOutputPath}");
            Console.Error.WriteLine($"Large cache: {options.LargeOutputPath}");
        }
    }

    private static void WriteCacheStats(TimeSpan elapsed, long processedThrough, long max, long smallCount, long largeCount)
    {
        long totalPrimes = smallCount + largeCount;
        Console.Error.WriteLine(
            $"Stats after {elapsed}: processed through {processedThrough} of {max}; " +
            $"primes={totalPrimes}; speed={CalculatePrimesPerHour(elapsed, totalPrimes):N0} primes/hour; " +
            $"memory managed={FormatBytes(GC.GetTotalMemory(forceFullCollection: false))}, working-set={FormatBytes(Process.GetCurrentProcess().WorkingSet64)}");
    }

    private static double CalculatePrimesPerHour(TimeSpan elapsed, long primes)
    {
        return elapsed.TotalHours <= 0 ? 0 : primes / elapsed.TotalHours;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:N1} {units[unit]}";
    }
}
