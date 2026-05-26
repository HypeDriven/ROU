using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;

namespace LargePrimeCli;

public sealed record FactorOptions(
    BigInteger Number,
    int PMinusOneBound,
    int SmallPrimeLimit,
    string SmallPrimesFile,
    string LargePrimesFile,
    string RootScheduleFile,
    bool UseLargePrimeCache,
    int Workers,
    bool Quiet,
    bool ShowHelp)
{
    public static FactorOptions? Parse(string[] args)
    {
        BigInteger number = BigInteger.Zero;
        bool hasNumber = false;
        int pMinusOneBound = 100_000;
        int smallPrimeLimit = 100_000;
        string smallPrimesFile = Path.Combine(".prime-cache", "small-primes.txt");
        string largePrimesFile = Path.Combine(".prime-cache", "large-primes.txt");
        string? rootScheduleFile = null;
        bool useLargePrimeCache = false;
        int workers = Environment.ProcessorCount;
        bool quiet = false;
        bool help = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    help = true;
                    break;

                case "--pminus1-bound":
                    if (++i >= args.Length || !int.TryParse(args[i], out pMinusOneBound)) return null;
                    break;

                case "-s":
                case "--small-prime-limit":
                    if (++i >= args.Length || !int.TryParse(args[i], out smallPrimeLimit)) return null;
                    break;

                case "--small-primes-file":
                    if (++i >= args.Length) return null;
                    smallPrimesFile = args[i];
                    break;

                case "--large-primes-file":
                    if (++i >= args.Length) return null;
                    largePrimesFile = args[i];
                    break;

                case "--root-schedule-file":
                case "--roots-cache-file":
                    if (++i >= args.Length) return null;
                    rootScheduleFile = args[i];
                    break;

                case "--use-large-prime-cache":
                    useLargePrimeCache = true;
                    break;

                case "--workers":
                case "-w":
                    if (++i >= args.Length || !int.TryParse(args[i], out workers)) return null;
                    break;

                case "-q":
                case "--quiet":
                    quiet = true;
                    break;

                default:
                    if (hasNumber)
                    {
                        Console.Error.WriteLine($"Unknown factor argument: {args[i]}");
                        return null;
                    }

                    if (!TryParseBigInteger(args[i], out number))
                    {
                        Console.Error.WriteLine($"Invalid number: {args[i]}");
                        return null;
                    }

                    hasNumber = true;
                    break;
            }
        }

        rootScheduleFile ??= Path.Combine(".prime-cache", $"pminus1-powers-{pMinusOneBound}.txt");

        if (help)
            return new FactorOptions(number, pMinusOneBound, smallPrimeLimit, smallPrimesFile, largePrimesFile, rootScheduleFile, useLargePrimeCache, workers, quiet, help);

        if (!hasNumber || number < 2)
        {
            Console.Error.WriteLine("factor requires an integer >= 2.");
            return null;
        }

        if (pMinusOneBound < 2 || smallPrimeLimit < 2)
        {
            Console.Error.WriteLine("Bounds must be at least 2.");
            return null;
        }

        if (workers < 1)
        {
            Console.Error.WriteLine("--workers must be at least 1.");
            return null;
        }

        return new FactorOptions(number, pMinusOneBound, smallPrimeLimit, smallPrimesFile, largePrimesFile, rootScheduleFile, useLargePrimeCache, workers, quiet, help);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
LargePrimeCli factor - factor an integer using cached primes, roots-of-unity/Pollard p-1, and Pollard rho

Usage:
  dotnet run -- factor <number> [options]

Options:
      --pminus1-bound <n>        Smoothness bound for Pollard p-1/root collision search (default: 100000)
  -s, --small-prime-limit <n>    Fallback trial-division prime limit when no cache exists (default: 100000)
      --small-primes-file <path> Small prime cache file (default: .prime-cache/small-primes.txt)
      --large-primes-file <path> Large prime cache file (default: .prime-cache/large-primes.txt)
      --root-schedule-file <path> Cache file for reusable Pollard p-1 prime-power/root schedule
      --use-large-prime-cache    Trial-divide by large-primes cache too; off by default for huge inputs
  -w, --workers <n>              Parallel Pollard rho workers (default: CPU core count)
  -q, --quiet                    Only print factors to stdout
  -h, --help                     Show this help

Examples:
  dotnet run -- factor 8051
  dotnet run -- factor 0x1F73 --pminus1-bound 1000000
""");
    }

    private static bool TryParseBigInteger(string text, out BigInteger value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return BigInteger.TryParse(text[2..], System.Globalization.NumberStyles.AllowHexSpecifier, null, out value);

        return BigInteger.TryParse(text, out value);
    }
}

public sealed class Factorizer
{
    private readonly int[] _smallPrimes;
    private readonly BigInteger[] _largePrimes;
    private readonly long[] _pMinusOnePowers;
    private readonly int _workers;

    public Factorizer(int[] smallPrimes, BigInteger[] largePrimes, long[] pMinusOnePowers, int workers)
    {
        _smallPrimes = smallPrimes;
        _largePrimes = largePrimes;
        _pMinusOnePowers = pMinusOnePowers;
        _workers = Math.Max(1, workers);
    }

    public List<BigInteger> Factor(BigInteger n, bool quiet, CancellationToken cancellationToken = default)
    {
        var factors = new List<BigInteger>();
        FactorRecursive(n, factors, quiet, cancellationToken);
        factors.Sort();
        return factors;
    }

    private void FactorRecursive(BigInteger n, List<BigInteger> factors, bool quiet, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (n == 1)
            return;

        if (IsProbablePrime(n, 32))
        {
            AddFactor(n, factors, quiet);
            return;
        }

        foreach (int p in _smallPrimes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BigInteger bp = p;
            if (bp * bp > n)
                break;

            while (n % bp == 0)
            {
                AddFactor(bp, factors, quiet);
                n /= bp;
            }
        }

        foreach (BigInteger p in _largePrimes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (p * p > n)
                break;

            while (n % p == 0)
            {
                AddFactor(p, factors, quiet);
                n /= p;
            }
        }

        if (n == 1)
            return;

        if (IsProbablePrime(n, 32))
        {
            AddFactor(n, factors, quiet);
            return;
        }

        BigInteger divisor = PollardPMinusOne(n, cancellationToken);
        if (divisor <= 1 || divisor >= n)
            divisor = PollardRhoParallel(n, _workers, cancellationToken);

        FactorRecursive(divisor, factors, quiet, cancellationToken);
        FactorRecursive(n / divisor, factors, quiet, cancellationToken);
    }

    private static void AddFactor(BigInteger factor, List<BigInteger> factors, bool quiet)
    {
        factors.Add(factor);
        Console.WriteLine(factor);
        Console.Out.Flush();

        if (!quiet)
            Console.Error.WriteLine($"Verified factor: {factor}");
    }

    private BigInteger PollardPMinusOne(BigInteger n, CancellationToken cancellationToken)
    {
        BigInteger a = 2;

        foreach (long power in _pMinusOnePowers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            a = BigInteger.ModPow(a, power, n);
            BigInteger g = BigInteger.GreatestCommonDivisor(a - 1, n);

            if (g > 1 && g < n)
                return g;
        }

        return BigInteger.One;
    }

    private static BigInteger PollardRhoParallel(BigInteger n, int workers, CancellationToken cancellationToken)
    {
        if (n.IsEven)
            return 2;

        if (workers == 1)
            return PollardRhoWorker(n, cancellationToken);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        object sync = new();
        BigInteger result = BigInteger.Zero;

        var tasks = new Task[workers];
        for (int i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    BigInteger divisor = PollardRhoWorker(n, linkedCts.Token);
                    lock (sync)
                    {
                        if (result == BigInteger.Zero)
                        {
                            result = divisor;
                            linkedCts.Cancel();
                        }
                    }
                }
                catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
                {
                }
            }, linkedCts.Token);
        }

        try
        {
            Task.WaitAll(tasks);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result > 1 && result < n ? result : PollardRhoWorker(n, cancellationToken);
    }

    private static BigInteger PollardRhoWorker(BigInteger n, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BigInteger c = RandomBigIntegerBelow(n - 1) + 1;
            BigInteger x = RandomBigIntegerBelow(n - 2) + 2;
            BigInteger y = x;
            BigInteger d = 1;

            while (d == 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                x = (BigInteger.ModPow(x, 2, n) + c) % n;
                y = (BigInteger.ModPow(y, 2, n) + c) % n;
                y = (BigInteger.ModPow(y, 2, n) + c) % n;
                d = BigInteger.GreatestCommonDivisor(BigInteger.Abs(x - y), n);
            }

            if (d != n)
                return d;
        }
    }

    private static bool IsProbablePrime(BigInteger n, int rounds)
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

        for (int i = 0; i < rounds; i++)
        {
            BigInteger a = RandomBigIntegerBelow(n - 3) + 2;
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

    private static BigInteger RandomBigIntegerBelow(BigInteger maxExclusive)
    {
        int byteLength = maxExclusive.ToByteArray(isUnsigned: true, isBigEndian: true).Length;
        byte[] bytes = new byte[byteLength];

        while (true)
        {
            RandomNumberGenerator.Fill(bytes);
            BigInteger value = new(bytes, isUnsigned: true, isBigEndian: true);
            if (value < maxExclusive)
                return value;
        }
    }
}

public static class PMinusOneRootScheduleCache
{
    public static long[] LoadOrCreate(string path, int bound, bool quiet)
    {
        long[] cached = Load(path, bound);
        if (cached.Length > 0)
        {
            if (!quiet)
                Console.Error.WriteLine($"P-1 root schedule cache: {path} ({cached.Length} prime powers).");
            return cached;
        }

        long[] powers = Build(bound);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using (var writer = new StreamWriter(path, append: false))
        {
            writer.WriteLine($"# Pollard p-1 reusable prime-power/root schedule; bound={bound}");
            foreach (long power in powers)
                writer.WriteLine(power);
        }

        if (!quiet)
            Console.Error.WriteLine($"Created P-1 root schedule cache: {path} ({powers.Length} prime powers).");

        return powers;
    }

    private static long[] Load(string path, int bound)
    {
        if (!File.Exists(path))
            return [];

        var powers = new List<long>();
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (!long.TryParse(line, out long power) || power < 2)
                return [];

            if (power <= bound)
                powers.Add(power);
        }

        return powers.Count == 0 ? [] : powers.ToArray();
    }

    private static long[] Build(int bound)
    {
        int[] primes = PrimeUtilities.GenerateSmallPrimes(bound);
        var powers = new long[primes.Length];

        for (int i = 0; i < primes.Length; i++)
        {
            long power = primes[i];
            while (power <= bound / primes[i])
                power *= primes[i];
            powers[i] = power;
        }

        return powers;
    }
}

public static class FactorCommand
{
    public static int Run(FactorOptions options, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        int[] smallPrimes = PrimeUtilities.LoadSmallPrimes(options.SmallPrimesFile, options.SmallPrimeLimit);
        string smallSource = $"{options.SmallPrimesFile}, limited to <= {options.SmallPrimeLimit}";
        if (smallPrimes.Length == 0)
        {
            smallPrimes = PrimeUtilities.GenerateSmallPrimes(options.SmallPrimeLimit);
            smallSource = $"generated in memory up to {options.SmallPrimeLimit}";
        }

        BigInteger[] largePrimes = options.UseLargePrimeCache
            ? PrimeUtilities.LoadLargePrimes(options.LargePrimesFile, options.Number / 2)
            : [];

        long[] rootSchedule = PMinusOneRootScheduleCache.LoadOrCreate(
            options.RootScheduleFile,
            options.PMinusOneBound,
            options.Quiet);

        if (!options.Quiet)
        {
            Console.Error.WriteLine($"Factoring: {options.Number}");
            Console.Error.WriteLine($"Small prime input: {smallSource} ({smallPrimes.Length} primes).");
            Console.Error.WriteLine(options.UseLargePrimeCache
                ? $"Large prime input: {options.LargePrimesFile} ({largePrimes.Length} primes)."
                : "Large prime input: skipped by default; use --use-large-prime-cache to enable.");
            Console.Error.WriteLine($"Pollard p-1/root-collision bound: {options.PMinusOneBound}");
            Console.Error.WriteLine($"Root schedule input: {options.RootScheduleFile} ({rootSchedule.Length} prime powers).");
            Console.Error.WriteLine($"Parallel Pollard rho workers: {options.Workers}.");
        }

        var factorizer = new Factorizer(smallPrimes, largePrimes, rootSchedule, options.Workers);
        List<BigInteger> factors = factorizer.Factor(options.Number, options.Quiet, cancellationToken);

        stopwatch.Stop();

        if (!options.Quiet)
            Console.Error.WriteLine($"Found {factors.Count} factor(s) in {stopwatch.Elapsed}.");

        return 0;
    }
}
