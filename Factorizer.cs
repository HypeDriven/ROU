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

        if (help)
            return new FactorOptions(number, pMinusOneBound, smallPrimeLimit, smallPrimesFile, largePrimesFile, quiet, help);

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

        return new FactorOptions(number, pMinusOneBound, smallPrimeLimit, smallPrimesFile, largePrimesFile, quiet, help);
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
    private readonly int _pMinusOneBound;

    public Factorizer(int[] smallPrimes, BigInteger[] largePrimes, int pMinusOneBound)
    {
        _smallPrimes = smallPrimes;
        _largePrimes = largePrimes;
        _pMinusOneBound = pMinusOneBound;
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
            divisor = PollardRho(n, cancellationToken);

        FactorRecursive(divisor, factors, quiet, cancellationToken);
        FactorRecursive(n / divisor, factors, quiet, cancellationToken);
    }

    private static void AddFactor(BigInteger factor, List<BigInteger> factors, bool quiet)
    {
        factors.Add(factor);
        if (!quiet)
            Console.Error.WriteLine($"Verified factor: {factor}");
    }

    private BigInteger PollardPMinusOne(BigInteger n, CancellationToken cancellationToken)
    {
        BigInteger a = 2;
        int[] primes = _smallPrimes.Length > 0 && _smallPrimes[^1] >= _pMinusOneBound
            ? _smallPrimes.Where(p => p <= _pMinusOneBound).ToArray()
            : PrimeUtilities.GenerateSmallPrimes(_pMinusOneBound);

        foreach (int prime in primes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long power = prime;
            while (power <= _pMinusOneBound / prime)
                power *= prime;

            a = BigInteger.ModPow(a, power, n);
            BigInteger g = BigInteger.GreatestCommonDivisor(a - 1, n);

            if (g > 1 && g < n)
                return g;
        }

        return BigInteger.One;
    }

    private static BigInteger PollardRho(BigInteger n, CancellationToken cancellationToken)
    {
        if (n.IsEven)
            return 2;

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

        BigInteger largePrimeLimit = options.Number / 2;
        BigInteger[] largePrimes = PrimeUtilities.LoadLargePrimes(options.LargePrimesFile, largePrimeLimit);

        if (!options.Quiet)
        {
            Console.Error.WriteLine($"Factoring: {options.Number}");
            Console.Error.WriteLine($"Small prime input: {smallSource} ({smallPrimes.Length} primes).");
            Console.Error.WriteLine($"Large prime input: {options.LargePrimesFile} ({largePrimes.Length} primes).");
            Console.Error.WriteLine($"Pollard p-1/root-collision bound: {options.PMinusOneBound}");
        }

        var factorizer = new Factorizer(smallPrimes, largePrimes, options.PMinusOneBound);
        List<BigInteger> factors = factorizer.Factor(options.Number, options.Quiet, cancellationToken);

        foreach (BigInteger factor in factors)
            Console.WriteLine(factor);

        stopwatch.Stop();

        if (!options.Quiet)
            Console.Error.WriteLine($"Found {factors.Count} factor(s) in {stopwatch.Elapsed}.");

        return 0;
    }
}
