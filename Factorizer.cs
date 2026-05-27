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
    bool ForceLargePrimeCache,
    int Workers,
    int MillerRabinRounds,
    bool UseBailliePsw,
    bool Prove,
    int? Seed,
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
        bool forceLargePrimeCache = false;
        int workers = Environment.ProcessorCount;
        int millerRabinRounds = 32;
        bool useBailliePsw = false;
        bool prove = false;
        int? seed = null;
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

                case "--force-large-prime-cache":
                    useLargePrimeCache = true;
                    forceLargePrimeCache = true;
                    break;

                case "--workers":
                case "-w":
                    if (++i >= args.Length || !int.TryParse(args[i], out workers)) return null;
                    break;

                case "--miller-rabin-rounds":
                    if (++i >= args.Length || !int.TryParse(args[i], out millerRabinRounds)) return null;
                    break;

                case "--baillie-psw":
                    useBailliePsw = true;
                    break;

                case "--prove":
                    prove = true;
                    break;

                case "--seed":
                    if (++i >= args.Length || !int.TryParse(args[i], out int parsedSeed)) return null;
                    seed = parsedSeed;
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
                forceLargePrimeCache,
                workers,
                millerRabinRounds,
                useBailliePsw,
                prove,
                seed,
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

        if (millerRabinRounds < 1)
        {
            Console.Error.WriteLine("--miller-rabin-rounds must be at least 1.");
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
            forceLargePrimeCache,
            workers,
            millerRabinRounds,
            useBailliePsw,
            prove,
            seed,
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
      --force-large-prime-cache     Scan large-prime cache even for huge inputs
      --miller-rabin-rounds <n>     Randomized Miller-Rabin rounds for probable-prime checks (default: 32)
      --baillie-psw                 Use Baillie-PSW probable-prime checks instead of randomized Miller-Rabin
      --prove                       Prove returned prime factors with recursive Pocklington certificates where possible
  -w, --workers <n>                 Parallel factor workers (default: CPU core count)
  -q, --quiet                       Only print factors to stdout
      --seed <n>                    Test/benchmark mode only: deterministic RNG seed
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

        var builder = new StringBuilder();
        builder.AppendLine($"# format={FormatMarker}");
        builder.AppendLine($"# bound={bound}");
        builder.AppendLine($"# count={powers.Length}");
        builder.AppendLine($"# sha256={hash}");
        builder.AppendLine("# Pollard p-1 reusable prime-power/root schedule");
        foreach (long power in powers)
            builder.AppendLine(power.ToString());

        WriteAtomic(path, builder.ToString());

        if (!quiet)
            Console.Error.WriteLine($"Created P-1 root schedule cache: {path} ({powers.Length} prime powers). SHA-256={hash}");

        return powers;
    }

    private static void WriteAtomic(string path, string content)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, content, Encoding.UTF8);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }

            throw;
        }
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

