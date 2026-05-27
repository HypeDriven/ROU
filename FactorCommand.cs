using System.Diagnostics;
using System.Numerics;

namespace LargePrimeCli;

public static class FactorCommand
{
    private static readonly BigInteger AutoLargePrimeCacheLimit = 10_000_000_000L;

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
        bool largePrimeCacheAutoSkipped = options.UseLargePrimeCache
            && !options.ForceLargePrimeCache
            && largePrimeLimit > AutoLargePrimeCacheLimit;
        string? memoryMappedLargePrimeCachePath = options.UseLargePrimeCache
            && !largePrimeCacheAutoSkipped
            && File.Exists(options.LargePrimesFile)
                ? options.LargePrimesFile
                : null;
        BigInteger effectiveLargePrimeLimit = options.ForceLargePrimeCache
            ? largePrimeLimit
            : BigInteger.Min(largePrimeLimit, AutoLargePrimeCacheLimit);
        BigInteger[] largePrimes = [];

        long[] rootSchedule = PMinusOneRootScheduleCache.LoadOrCreate(
            options.RootScheduleFile,
            options.PMinusOneBound,
            options.Quiet);

        if (!options.Quiet)
        {
            Console.Error.WriteLine($"Factoring: {options.Number}");
            Console.Error.WriteLine($"Small prime input: {smallSource} ({smallPrimes.Length} primes).");
            if (!options.UseLargePrimeCache)
            {
                Console.Error.WriteLine("Large prime input: skipped by --no-large-prime-cache.");
            }
            else if (largePrimeCacheAutoSkipped)
            {
                Console.Error.WriteLine($"Large prime input: auto-skipped because sqrt(n) = {largePrimeLimit} exceeds safe startup limit {AutoLargePrimeCacheLimit}. Use --force-large-prime-cache to scan it anyway.");
            }
            else
            {
                Console.Error.WriteLine($"Large prime input: {(memoryMappedLargePrimeCachePath is null ? "cache file not found" : memoryMappedLargePrimeCachePath)}, memory-mapped and streamed up to <= {effectiveLargePrimeLimit}.");
            }
            Console.Error.WriteLine($"Pollard p-1/root-collision stage-1 bound: {options.PMinusOneBound}");
            Console.Error.WriteLine($"Pollard p-1/root-collision stage-2 bound: {options.PMinusOneStage2Bound}");
            Console.Error.WriteLine($"Root schedule input: {options.RootScheduleFile} ({rootSchedule.Length} prime powers).");
            Console.Error.WriteLine(options.UseBailliePsw
                ? "Primality check: Baillie-PSW probable-prime test."
                : $"Primality check: {options.MillerRabinRounds} randomized Miller-Rabin probable-prime round(s).");
            Console.Error.WriteLine($"Parallel factor workers: {options.Workers}.");
            if (options.Seed is int seed)
                Console.Error.WriteLine($"Deterministic test/benchmark RNG seed: {seed}.");
        }

        IRandomSource random = options.Seed is int deterministicSeed
            ? new DeterministicRandomSource(deterministicSeed)
            : new CryptographicRandomSource();

        IPrimalityTest primalityTest = options.UseBailliePsw
            ? new BailliePswPrimalityTest()
            : new MillerRabinPrimalityTest(random);

        var trialDivider = new TrialDivider(
            smallPrimes,
            largePrimes,
            memoryMappedLargePrimeCachePath,
            effectiveLargePrimeLimit);
        var pipeline = new FactorizationPipeline(
            trialDivider,
            primalityTest,
            new PMinusOneSplitter(rootSchedule, options.PMinusOneBound, options.PMinusOneStage2Bound, random),
            new RhoSplitter(random),
            options.Workers,
            options.MillerRabinRounds);

        List<BigInteger> factors = pipeline.Factor(options.Number, options.Quiet, cancellationToken);

        if (options.Prove)
        {
            var prover = new PocklingtonPrimalityProver(random);
            foreach (BigInteger factor in factors.Distinct())
            {
                if (!prover.TryProve(factor, cancellationToken, out PocklingtonProof proof))
                {
                    Console.Error.WriteLine($"Could not prove primality of factor {factor} with Pocklington. ECPP fallback is not implemented.");
                    return 4;
                }

                if (!options.Quiet)
                    Console.Error.WriteLine($"Pocklington proof: factor {factor}, {proof.Steps.Count} proof step(s).");
            }
        }

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
