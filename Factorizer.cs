using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace LargePrimeCli;

public sealed record FactorOptions(
    BigInteger Number,
    int PMinusOneBound,
    int PMinusOneStage2Bound,
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
        int? pMinusOneStage2Bound = null;
        int smallPrimeLimit = 100_000;
        string smallPrimesFile = Path.Combine(".prime-cache", "small-primes.txt");
        string largePrimesFile = Path.Combine(".prime-cache", "large-primes.txt");
        string? rootScheduleFile = null;
        bool useLargePrimeCache = true;
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

                case "--pminus1-stage2-bound":
                    if (++i >= args.Length || !int.TryParse(args[i], out int parsedStage2Bound)) return null;
                    pMinusOneStage2Bound = parsedStage2Bound;
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

                case "--no-large-prime-cache":
                    useLargePrimeCache = false;
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

        int effectiveStage2Bound = pMinusOneStage2Bound ?? DefaultPMinusOneStage2Bound(pMinusOneBound);
        rootScheduleFile ??= Path.Combine(".prime-cache", $"pminus1-powers-{pMinusOneBound}.txt");

        if (help)
        {
            return new FactorOptions(
                number,
                pMinusOneBound,
                effectiveStage2Bound,
                smallPrimeLimit,
                smallPrimesFile,
                largePrimesFile,
                rootScheduleFile,
                useLargePrimeCache,
                workers,
                quiet,
                help);
        }

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

        if (effectiveStage2Bound < pMinusOneBound)
        {
            Console.Error.WriteLine("--pminus1-stage2-bound must be greater than or equal to --pminus1-bound.");
            return null;
        }

        if (workers < 1)
        {
            Console.Error.WriteLine("--workers must be at least 1.");
            return null;
        }

        return new FactorOptions(
            number,
            pMinusOneBound,
            effectiveStage2Bound,
            smallPrimeLimit,
            smallPrimesFile,
            largePrimesFile,
            rootScheduleFile,
            useLargePrimeCache,
            workers,
            quiet,
            help);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
LargePrimeCli factor - factor an integer using cached primes, roots-of-unity/Pollard p-1, and Pollard rho

Usage:
  dotnet run -- factor <number> [options]

Options:
      --pminus1-bound <n>           Stage-1 smoothness bound for Pollard p-1/root collision search (default: 100000)
      --pminus1-stage2-bound <n>    Stage-2 bound for one-large-prime p-1 extension (default: 10 * stage-1 bound)
  -s, --small-prime-limit <n>       Fallback trial-division prime limit when no cache exists (default: 100000)
      --small-primes-file <path>    Small prime cache file (default: .prime-cache/small-primes.txt)
      --large-primes-file <path>    Large prime cache file (default: .prime-cache/large-primes.txt)
      --root-schedule-file <path>   Cache file for reusable Pollard p-1 prime-power/root schedule
      --use-large-prime-cache       Trial-divide by large-primes cache too; enabled by default
      --no-large-prime-cache        Skip large-prime cache trial division
  -w, --workers <n>                 Parallel factor workers and Pollard rho workers (default: CPU core count)
  -q, --quiet                       Only print factors to stdout
  -h, --help                        Show this help

Examples:
  dotnet run -- factor 8051
  dotnet run -- factor 0x1F73 --pminus1-bound 1000000 --pminus1-stage2-bound 10000000
""");
    }

    private static int DefaultPMinusOneStage2Bound(int pMinusOneBound)
    {
        long defaultBound = (long)pMinusOneBound * 10L;
        return defaultBound > int.MaxValue ? int.MaxValue : (int)defaultBound;
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
    private static readonly int[] PMinusOneBases =
    [
        2, 3, 5, 7, 11, 13, 17, 19,
        23, 29, 31, 37, 41, 43, 47
    ];

    private const int PMinusOneRandomBaseRetries = 4;
    private const int PMinusOneStage2BatchSize = 64;
    private const int RhoBrentBatchSize = 128;

    private readonly int[] _smallPrimes;
    private readonly BigInteger[] _largePrimes;
    private readonly long[] _pMinusOnePowers;
    private readonly int[] _pMinusOneStage2Primes;
    private readonly int _workers;

    public Factorizer(
        int[] smallPrimes,
        BigInteger[] largePrimes,
        long[] pMinusOnePowers,
        int pMinusOneStage1Bound,
        int pMinusOneStage2Bound,
        int workers)
    {
        _smallPrimes = smallPrimes;
        _largePrimes = largePrimes;
        _pMinusOnePowers = pMinusOnePowers;
        _pMinusOneStage2Primes = pMinusOneStage2Bound > pMinusOneStage1Bound
            ? PrimeUtilities.GenerateSmallPrimes(pMinusOneStage2Bound)
                .Where(p => p > pMinusOneStage1Bound)
                .ToArray()
            : [];
        _workers = Math.Max(1, workers);
    }

    public List<BigInteger> Factor(BigInteger n, bool quiet, CancellationToken cancellationToken = default)
    {
        var factors = new List<BigInteger>();
        object factorLock = new();
        var queue = new System.Collections.Concurrent.ConcurrentQueue<BigInteger>();
        using var workAvailable = new SemaphoreSlim(0);
        long pending = 0;

        void Enqueue(BigInteger value)
        {
            if (value <= 1)
                return;

            Interlocked.Increment(ref pending);
            queue.Enqueue(value);
            workAvailable.Release();
        }

        void CompleteOne()
        {
            if (Interlocked.Decrement(ref pending) == 0)
            {
                for (int i = 0; i < _workers; i++)
                    workAvailable.Release();
            }
        }

        void Worker()
        {
            while (true)
            {
                workAvailable.Wait(cancellationToken);

                if (Volatile.Read(ref pending) == 0 && queue.IsEmpty)
                    return;

                if (!queue.TryDequeue(out BigInteger value))
                    continue;

                try
                {
                    ProcessComposite(value, factors, factorLock, quiet, Enqueue, cancellationToken);
                }
                finally
                {
                    CompleteOne();
                }
            }
        }

        Enqueue(n);

        Task[] tasks = new Task[_workers];
        for (int i = 0; i < tasks.Length; i++)
            tasks[i] = Task.Run(Worker, cancellationToken);

        Task.WaitAll(tasks, cancellationToken);

        lock (factorLock)
            factors.Sort();

        return factors;
    }

    private void ProcessComposite(
        BigInteger n,
        List<BigInteger> factors,
        object factorLock,
        bool quiet,
        Action<BigInteger> enqueue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (n == 1)
            return;

        n = TrialDivideSmallPrimes(n, factors, factorLock, quiet, cancellationToken);
        if (n == 1)
            return;

        n = TrialDivideLargePrimes(n, factors, factorLock, quiet, cancellationToken);
        if (n == 1)
            return;

        if (IsProbablePrime(n, 32))
        {
            AddFactor(n, factors, factorLock, quiet, "Probable prime factor");
            return;
        }

        BigInteger divisor = PollardPMinusOne(n, cancellationToken);
        if (divisor <= 1 || divisor >= n)
            divisor = PollardRhoParallel(n, _workers, cancellationToken);

        if (divisor <= 1 || divisor >= n)
            throw new InvalidOperationException($"Failed to split composite: {n}");

        enqueue(divisor);
        enqueue(n / divisor);
    }

    private BigInteger TrialDivideSmallPrimes(
        BigInteger n,
        List<BigInteger> factors,
        object factorLock,
        bool quiet,
        CancellationToken cancellationToken)
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
                AddFactor(bp, factors, factorLock, quiet, "Prime factor from small-prime input");
                n /= bp;
            }
            while (n % bp == 0);

            if (n == 1)
                break;
        }

        return n;
    }

    private BigInteger TrialDivideLargePrimes(
        BigInteger n,
        List<BigInteger> factors,
        object factorLock,
        bool quiet,
        CancellationToken cancellationToken)
    {
        foreach (BigInteger p in _largePrimes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (p > n / p)
                break;

            if (n % p != 0)
                continue;

            do
            {
                AddFactor(p, factors, factorLock, quiet, "Prime factor from large-prime input");
                n /= p;
            }
            while (n % p == 0);

            if (n == 1)
                break;
        }

        return n;
    }

    private static void AddFactor(
        BigInteger factor,
        List<BigInteger> factors,
        object factorLock,
        bool quiet,
        string evidence)
    {
        lock (factorLock)
        {
            factors.Add(factor);
            Console.WriteLine(factor);
            Console.Out.Flush();
        }

        if (!quiet)
            Console.Error.WriteLine($"{evidence}: {factor}");
    }

    private BigInteger PollardPMinusOne(BigInteger n, CancellationToken cancellationToken)
    {
        foreach (BigInteger seed in PMinusOneBaseSequence(n))
        {
            cancellationToken.ThrowIfCancellationRequested();

            BigInteger a = seed % n;
            if (a <= 1)
                continue;

            BigInteger g = BigInteger.GreatestCommonDivisor(a, n);
            if (g > 1 && g < n)
                return g;

            bool collapsedCompletely = false;

            foreach (long power in _pMinusOnePowers)
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

            g = PollardPMinusOneStage2(n, a, cancellationToken);
            if (g > 1 && g < n)
                return g;
        }

        return BigInteger.One;
    }

    private BigInteger PollardPMinusOneStage2(BigInteger n, BigInteger stage1Value, CancellationToken cancellationToken)
    {
        if (_pMinusOneStage2Primes.Length == 0)
            return BigInteger.One;

        BigInteger product = BigInteger.One;
        int batchStart = 0;
        int batchCount = 0;

        for (int i = 0; i < _pMinusOneStage2Primes.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int q = _pMinusOneStage2Primes[i];
            BigInteger term = BigInteger.ModPow(stage1Value, q, n) - 1;
            if (term.Sign < 0)
                term += n;

            product = (product * term) % n;
            batchCount++;

            if (batchCount == PMinusOneStage2BatchSize)
            {
                BigInteger g = BigInteger.GreatestCommonDivisor(product, n);
                if (g > 1 && g < n)
                    return g;

                if (g == n)
                {
                    BigInteger fallback = PollardPMinusOneStage2IndividualSearch(
                        n,
                        stage1Value,
                        batchStart,
                        i,
                        cancellationToken);

                    if (fallback > 1 && fallback < n)
                        return fallback;
                }

                batchStart = i + 1;
                batchCount = 0;
                product = BigInteger.One;
            }
        }

        if (batchCount > 0)
        {
            BigInteger g = BigInteger.GreatestCommonDivisor(product, n);
            if (g > 1 && g < n)
                return g;

            if (g == n)
            {
                BigInteger fallback = PollardPMinusOneStage2IndividualSearch(
                    n,
                    stage1Value,
                    batchStart,
                    _pMinusOneStage2Primes.Length - 1,
                    cancellationToken);

                if (fallback > 1 && fallback < n)
                    return fallback;
            }
        }

        return BigInteger.One;
    }

    private BigInteger PollardPMinusOneStage2IndividualSearch(
        BigInteger n,
        BigInteger stage1Value,
        int startIndex,
        int endIndex,
        CancellationToken cancellationToken)
    {
        for (int i = startIndex; i <= endIndex; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int q = _pMinusOneStage2Primes[i];
            BigInteger term = BigInteger.ModPow(stage1Value, q, n) - 1;
            BigInteger g = BigInteger.GreatestCommonDivisor(term, n);

            if (g > 1 && g < n)
                return g;
        }

        return BigInteger.One;
    }

    private static IEnumerable<BigInteger> PMinusOneBaseSequence(BigInteger n)
    {
        foreach (int b in PMinusOneBases)
            yield return b;

        for (int i = 0; i < PMinusOneRandomBaseRetries; i++)
        {
            if (n <= 5)
                yield break;

            yield return RandomBigIntegerBelow(n - 3) + 2;
        }
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

            BigInteger y = RandomBigIntegerBelow(n - 1) + 1;
            BigInteger c = RandomBigIntegerBelow(n - 1) + 1;
            long m = RhoBrentBatchSize;
            BigInteger g = BigInteger.One;
            long r = 1;
            BigInteger q = BigInteger.One;
            BigInteger x = BigInteger.Zero;
            BigInteger ys = BigInteger.Zero;
            bool restart = false;

            while (g == BigInteger.One)
            {
                cancellationToken.ThrowIfCancellationRequested();

                x = y;
                for (long i = 0; i < r; i++)
                    y = RhoStep(y, c, n);

                long k = 0;
                while (k < r && g == BigInteger.One)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ys = y;
                    long limit = Math.Min(m, r - k);
                    for (long i = 0; i < limit; i++)
                    {
                        y = RhoStep(y, c, n);
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
                    ys = RhoStep(ys, c, n);
                    g = BigInteger.GreatestCommonDivisor(BigInteger.Abs(x - ys), n);
                }
                while (g == BigInteger.One);
            }

            if (g > 1 && g < n)
                return g;
        }
    }

    private static BigInteger RhoStep(BigInteger x, BigInteger c, BigInteger n)
    {
        return ((x * x) + c) % n;
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

        int byteLength = n.ToByteArray(isUnsigned: true, isBigEndian: true).Length;

        for (int i = 0; i < rounds; i++)
        {
            BigInteger a = RandomBigIntegerBelow(n - 3, byteLength) + 2;
            BigInteger x = BigInteger.ModPow(a, d, n);

            if (x == 1 || x == n - 1)
                continue;

            bool passed = false;
            for (int r = 1; r < s; r++)
            {
                x = (x * x) % n;
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
        return RandomBigIntegerBelow(maxExclusive, byteLength);
    }

    private static BigInteger RandomBigIntegerBelow(BigInteger maxExclusive, int byteLength)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));

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
    private const string FormatMarker = "LargePrimeCli.PMinusOneRootSchedule.v1";

    public static long[] LoadOrCreate(string path, int bound, bool quiet)
    {
        long[] cached = Load(path, bound, out string? rejectionReason);
        if (cached.Length > 0)
        {
            if (!quiet)
                Console.Error.WriteLine($"P-1 root schedule cache: {path} ({cached.Length} prime powers). Metadata verified.");
            return cached;
        }

        if (!quiet && File.Exists(path) && !string.IsNullOrWhiteSpace(rejectionReason))
            Console.Error.WriteLine($"Ignoring stale or invalid P-1 root schedule cache: {path}. Reason: {rejectionReason}");

        long[] powers = Build(bound);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string hash = ComputeScheduleHash(powers);

        using (var writer = new StreamWriter(path, append: false))
        {
            writer.WriteLine($"# format={FormatMarker}");
            writer.WriteLine($"# bound={bound}");
            writer.WriteLine($"# count={powers.Length}");
            writer.WriteLine($"# sha256={hash}");
            writer.WriteLine("# Pollard p-1 reusable prime-power/root schedule");
            foreach (long power in powers)
                writer.WriteLine(power);
        }

        if (!quiet)
            Console.Error.WriteLine($"Created P-1 root schedule cache: {path} ({powers.Length} prime powers). SHA-256={hash}");

        return powers;
    }

    private static long[] Load(string path, int bound, out string? rejectionReason)
    {
        rejectionReason = null;

        if (!File.Exists(path))
        {
            rejectionReason = "file does not exist";
            return [];
        }

        string? format = null;
        int? metadataBound = null;
        int? metadataCount = null;
        string? metadataHash = null;
        var powers = new List<long>();

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('#'))
            {
                ReadOnlySpan<char> metadata = line.AsSpan(1).Trim();
                int equalsIndex = metadata.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                string key = metadata[..equalsIndex].Trim().ToString();
                string value = metadata[(equalsIndex + 1)..].Trim().ToString();

                switch (key)
                {
                    case "format":
                        format = value;
                        break;
                    case "bound":
                        if (int.TryParse(value, out int parsedBound))
                            metadataBound = parsedBound;
                        break;
                    case "count":
                        if (int.TryParse(value, out int parsedCount))
                            metadataCount = parsedCount;
                        break;
                    case "sha256":
                        metadataHash = value;
                        break;
                }

                continue;
            }

            if (!long.TryParse(line, out long power) || power < 2)
            {
                rejectionReason = $"invalid schedule entry: {line}";
                return [];
            }

            if (power > bound)
            {
                rejectionReason = $"schedule entry exceeds requested bound: {power} > {bound}";
                return [];
            }

            powers.Add(power);
        }

        if (format != FormatMarker)
        {
            rejectionReason = "missing or unsupported format metadata";
            return [];
        }

        if (metadataBound != bound)
        {
            rejectionReason = $"bound metadata mismatch: file={metadataBound?.ToString() ?? "missing"}, requested={bound}";
            return [];
        }

        if (metadataCount != powers.Count)
        {
            rejectionReason = $"count metadata mismatch: file={metadataCount?.ToString() ?? "missing"}, actual={powers.Count}";
            return [];
        }

        if (powers.Count == 0)
        {
            rejectionReason = "schedule contains no powers";
            return [];
        }

        string actualHash = ComputeScheduleHash(powers.ToArray());
        if (!string.Equals(metadataHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = "SHA-256 metadata mismatch";
            return [];
        }

        return powers.ToArray();
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

    private static string ComputeScheduleHash(long[] powers)
    {
        using SHA256 sha256 = SHA256.Create();
        string payload = string.Join('\n', powers) + "\n";
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
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

        BigInteger largePrimeLimit = IntegerSqrt(options.Number);
        BigInteger[] largePrimes = options.UseLargePrimeCache
            ? PrimeUtilities.LoadLargePrimes(options.LargePrimesFile, largePrimeLimit)
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
                ? $"Large prime input: {options.LargePrimesFile}, limited to <= sqrt(n) = {largePrimeLimit} ({largePrimes.Length} primes)."
                : "Large prime input: skipped by --no-large-prime-cache.");
            Console.Error.WriteLine($"Pollard p-1/root-collision stage-1 bound: {options.PMinusOneBound}");
            Console.Error.WriteLine($"Pollard p-1/root-collision stage-2 bound: {options.PMinusOneStage2Bound}");
            Console.Error.WriteLine($"Root schedule input: {options.RootScheduleFile} ({rootSchedule.Length} prime powers).");
            Console.Error.WriteLine($"Parallel factor/Pollard rho workers: {options.Workers}.");
        }

        var factorizer = new Factorizer(
            smallPrimes,
            largePrimes,
            rootSchedule,
            options.PMinusOneBound,
            options.PMinusOneStage2Bound,
            options.Workers);

        List<BigInteger> factors = factorizer.Factor(options.Number, options.Quiet, cancellationToken);

        stopwatch.Stop();

        if (!options.Quiet)
            Console.Error.WriteLine($"Found {factors.Count} factor(s) in {stopwatch.Elapsed}.");

        return 0;
    }

    private static BigInteger IntegerSqrt(BigInteger n)
    {
        if (n.Sign < 0)
            throw new ArgumentOutOfRangeException(nameof(n));

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
        if (n.Sign <= 0)
            return 0;

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
}
