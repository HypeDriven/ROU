using System.Diagnostics;
using System.Numerics;
using LargePrimeCli;

if (args.Length > 0 && string.Equals(args[0], "self-test", StringComparison.OrdinalIgnoreCase))
{
    using var testCts = CreateCancellationTokenSource();

    try
    {
        return RegressionTests.Run(testCts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Canceled.");
        return 130;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Regression test failure: {ex.Message}");
        return 1;
    }
}

if (args.Length > 0 && string.Equals(args[0], "factor", StringComparison.OrdinalIgnoreCase))
{
    FactorOptions? factorOptions = FactorOptions.Parse(args[1..]);
    if (factorOptions is null)
    {
        FactorOptions.PrintUsage();
        return 2;
    }

    if (factorOptions.ShowHelp)
    {
        FactorOptions.PrintUsage();
        return 0;
    }

    using var factorCts = CreateCancellationTokenSource();

    try
    {
        return FactorCommand.Run(factorOptions, factorCts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Canceled.");
        return 130;
    }
    catch (FactorizationFailedException ex)
    {
        Console.Error.WriteLine("Factorization failed.");
        Console.Error.WriteLine($"Residual composite: {ex.ResidualComposite}");
        Console.Error.WriteLine($"Methods attempted: {ex.MethodsAttempted}");
        Console.Error.WriteLine($"Last method attempted: {ex.LastMethodAttempted}");
        Console.Error.WriteLine($"Suggested changes: {ex.SuggestedActions}");
        return 3;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

if (args.Length > 0 && string.Equals(args[0], "cache", StringComparison.OrdinalIgnoreCase))
{
    PrimeCacheOptions? cacheOptions = PrimeCacheOptions.Parse(args[1..]);
    if (cacheOptions is null)
    {
        PrimeCacheOptions.PrintUsage();
        return 2;
    }

    if (cacheOptions.ShowHelp)
    {
        PrimeCacheOptions.PrintUsage();
        return 0;
    }

    using var cacheCts = CreateCancellationTokenSource();

    try
    {
        PrimeCacheBuilder.Build(cacheOptions, cacheCts.Token);
        return 0;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Canceled.");
        return 130;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

CliOptions? options = CliOptions.Parse(args);
if (options is null)
{
    CliOptions.PrintUsage();
    return 2;
}

if (options.ShowHelp)
{
    CliOptions.PrintUsage();
    return 0;
}

using var cts = CreateCancellationTokenSource();
Stopwatch appStopwatch = Stopwatch.StartNew();
BigInteger largestPrimeFound = BigInteger.Zero;
int primesFound = 0;

try
{
    int[] smallPrimes = LoadOrGenerateSmallPrimes(options, out string smallPrimeSource);
    BigInteger[] largePrimes = PrimeUtilities.LoadLargePrimes(options.LargePrimesFile);
    string largePrimeSource = File.Exists(options.LargePrimesFile)
        ? options.LargePrimesFile
        : "none; cache file not found";
    var searcher = new LargePrimeSearcher(smallPrimes, largePrimes);

    if (!options.Quiet)
    {
        Console.Error.WriteLine($"Searching for {options.Count} prime(s), {options.Bits} bits, {options.Rounds} Miller-Rabin rounds...");
        Console.Error.WriteLine($"Small prime input: {smallPrimeSource} ({smallPrimes.Length} primes).");
        Console.Error.WriteLine($"Large prime input: {largePrimeSource} ({largePrimes.Length} primes).");
        Console.Error.WriteLine("Press Ctrl+C to cancel.");
    }

    for (int i = 1; i <= options.Count; i++)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        BigInteger prime = await searcher.FindPrimeAsync(options.Bits, options.Rounds, cts.Token);
        stopwatch.Stop();

        Console.WriteLine(FormatPrime(prime, options.Format));
        primesFound++;
        if (prime > largestPrimeFound)
            largestPrimeFound = prime;

        if (!options.Quiet)
            Console.Error.WriteLine($"Found prime {i}/{options.Count} in {stopwatch.Elapsed}.");
    }

    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Canceled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
finally
{
    appStopwatch.Stop();
    if (primesFound > 0)
    {
        Console.Error.WriteLine($"App exited after {appStopwatch.Elapsed}. Generated {primesFound} prime(s). Largest generated prime has {BitLength(largestPrimeFound)} bits and value {largestPrimeFound}.");
    }
    else
    {
        Console.Error.WriteLine($"App exited after {appStopwatch.Elapsed}. Generated 0 primes.");
    }
}

static CancellationTokenSource CreateCancellationTokenSource()
{
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    return cts;
}

static int[] LoadOrGenerateSmallPrimes(CliOptions options, out string source)
{
    int[] cachedSmallPrimes = PrimeUtilities.LoadSmallPrimes(options.SmallPrimesFile);
    if (cachedSmallPrimes.Length > 0)
    {
        source = options.SmallPrimesFile;
        return cachedSmallPrimes;
    }

    source = $"generated in memory up to {options.SmallPrimeLimit}; cache file not found or empty at {options.SmallPrimesFile}";
    return PrimeUtilities.GenerateSmallPrimes(options.SmallPrimeLimit);
}

static string FormatPrime(BigInteger value, OutputFormat format) => format switch
{
    OutputFormat.Decimal => value.ToString(),
    OutputFormat.Hex => "0x" + value.ToString("X"),
    _ => value.ToString()
};

static int BitLength(BigInteger value)
{
    if (value.Sign <= 0)
        return 0;

    byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
    int bits = (bytes.Length - 1) * 8;
    byte top = bytes[0];

    while (top != 0)
    {
        bits++;
        top >>= 1;
    }

    return bits;
}

enum OutputFormat
{
    Decimal,
    Hex
}

sealed record CliOptions(
    int Bits,
    int Rounds,
    int SmallPrimeLimit,
    int Count,
    OutputFormat Format,
    bool Quiet,
    bool ShowHelp,
    string SmallPrimesFile,
    string LargePrimesFile)
{
    public static CliOptions? Parse(string[] args)
    {
        int bits = 128;
        int rounds = 64;
        int smallPrimeLimit = 10_000;
        int count = 1;
        OutputFormat format = OutputFormat.Decimal;
        bool quiet = false;
        bool help = false;
        string smallPrimesFile = Path.Combine(".prime-cache", "small-primes.txt");
        string largePrimesFile = Path.Combine(".prime-cache", "large-primes.txt");

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "-h":
                case "--help":
                    help = true;
                    break;

                case "-b":
                case "--bits":
                    if (!TryReadInt(args, ref i, out bits)) return null;
                    break;

                case "-r":
                case "--rounds":
                    if (!TryReadInt(args, ref i, out rounds)) return null;
                    break;

                case "-s":
                case "--small-prime-limit":
                    if (!TryReadInt(args, ref i, out smallPrimeLimit)) return null;
                    break;

                case "-c":
                case "--count":
                    if (!TryReadInt(args, ref i, out count)) return null;
                    break;

                case "--small-primes-file":
                    if (++i >= args.Length) return null;
                    smallPrimesFile = args[i];
                    break;

                case "--large-primes-file":
                    if (++i >= args.Length) return null;
                    largePrimesFile = args[i];
                    break;

                case "-f":
                case "--format":
                    if (++i >= args.Length) return null;
                    if (!Enum.TryParse(args[i], ignoreCase: true, out format)) return null;
                    break;

                case "-q":
                case "--quiet":
                    quiet = true;
                    break;

                default:
                    Console.Error.WriteLine($"Unknown argument: {arg}");
                    return null;
            }
        }

        if (bits < 16)
        {
            Console.Error.WriteLine("--bits must be at least 16.");
            return null;
        }

        if (rounds < 1)
        {
            Console.Error.WriteLine("--rounds must be at least 1.");
            return null;
        }

        if (smallPrimeLimit < 2)
        {
            Console.Error.WriteLine("--small-prime-limit must be at least 2.");
            return null;
        }

        if (count < 1)
        {
            Console.Error.WriteLine("--count must be at least 1.");
            return null;
        }

        return new CliOptions(bits, rounds, smallPrimeLimit, count, format, quiet, help, smallPrimesFile, largePrimesFile);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
LargePrimeCli - generate large probable primes and factor integers

Usage:
  dotnet run -- [options]
  dotnet run -- cache --max <N> [options]
  dotnet run -- factor <number> [options]
  dotnet run -- self-test

Options:
  -b, --bits <n>                 Prime bit length (default: 128, min: 16)
  -r, --rounds <n>               Randomized Miller-Rabin rounds for probable primes (default: 64)
  -s, --small-prime-limit <n>    Fallback small prime generation limit when no cache exists (default: 10000)
  -c, --count <n>                Number of primes to generate (default: 1)
      --small-primes-file <path> Small prime cache file (default: .prime-cache/small-primes.txt)
      --large-primes-file <path> Large prime cache file (default: .prime-cache/large-primes.txt)
  -f, --format <decimal|hex>     Output format (default: decimal)
  -q, --quiet                    Only print generated prime values to stdout
  -h, --help                     Show this help

Cache utility:
  dotnet run -- cache --max 1000000
  dotnet run -- cache --help

Factor utility:
  dotnet run -- factor 8051
  dotnet run -- factor --help

Regression tests:
  dotnet run -- self-test

Examples:
  dotnet run -- --bits 256
  dotnet run -- -b 512 -f hex -c 2
""");
    }

    private static bool TryReadInt(string[] args, ref int index, out int value)
    {
        value = 0;
        if (++index >= args.Length)
            return false;

        return int.TryParse(args[index], out value);
    }
}
