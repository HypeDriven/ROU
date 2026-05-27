using System.Numerics;

namespace LargePrimeCli;

public static class RegressionTests
{
    public static int Run(CancellationToken cancellationToken = default)
    {
        TestWheel(cancellationToken);
        TestPrimality();
        TestRhoSemiprimes(cancellationToken);
        TestRhoRepeatedFactors(cancellationToken);
        TestFactorization(cancellationToken);
        TestScheduleCache();
        TestPocklington(cancellationToken);
        TestConcurrency(cancellationToken);

        Console.Error.WriteLine("Regression tests passed.");
        return 0;
    }

    private static void TestWheel(CancellationToken cancellationToken)
    {
        var wheel = new WheelCandidateGenerator();
        int[] expectedResidues = [1, 7, 11, 13, 17, 19, 23, 29];
        int[] residues = wheel.Generate(31, cancellationToken)
            .Take(8)
            .Select(n => (int)(n % 30))
            .ToArray();
        AssertSequence(expectedResidues, residues, "B={2,3,5} wheel residues modulo 30");

        foreach (BigInteger candidate in wheel.Generate(7, cancellationToken).Take(200))
            Assert(BigInteger.GreatestCommonDivisor(candidate, 30) == 1, $"wheel candidate {candidate} is not coprime to 30");

        BigInteger[] wrap = wheel.Generate(23, cancellationToken).Take(5).ToArray();
        AssertSequence([23, 29, 31, 37, 41], wrap, "wheel gap sequence wrap");
    }

    private static void TestPrimality()
    {
        IPrimalityTest mr = new MillerRabinPrimalityTest(new DeterministicRandomSource(123));
        IPrimalityTest bpsw = new BailliePswPrimalityTest();

        int[] smallPrimes = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 97, 101];
        int[] smallComposites = [0, 1, 4, 6, 8, 9, 10, 12, 21, 25, 27, 91, 100];
        int[] carmichael = [561, 1105, 1729, 2465, 2821, 6601];

        foreach (int p in smallPrimes)
        {
            Assert(mr.IsProbablePrime(p, 16), $"MR rejected small prime {p}");
            Assert(bpsw.IsProbablePrime(p), $"BPSW rejected small prime {p}");
        }

        foreach (int n in smallComposites.Concat(carmichael))
        {
            Assert(!mr.IsProbablePrime(n, 16), $"MR accepted composite {n}");
            Assert(!bpsw.IsProbablePrime(n), $"BPSW accepted composite {n}");
        }

        BigInteger[] strongPseudoprimesToBase2 = [2047, 3277, 4033, 4681, 8321];
        foreach (BigInteger n in strongPseudoprimesToBase2)
        {
            Assert(MillerRabinPrimalityTest.MillerRabinBase2(n), $"{n} should be a base-2 strong pseudoprime");
            Assert(!bpsw.IsProbablePrime(n), $"BPSW accepted base-2 strong pseudoprime {n}");
        }

        ulong[] deterministic64Primes = [18_446_744_073_709_551_557UL, 18_446_744_073_709_551_533UL];
        ulong[] deterministic64Composites = [18_446_744_073_709_551_615UL, 18_446_744_073_709_551_555UL];
        foreach (ulong p in deterministic64Primes)
            Assert(IsPrime64Deterministic(p), $"deterministic 64-bit path rejected {p}");
        foreach (ulong n in deterministic64Composites)
            Assert(!IsPrime64Deterministic(n), $"deterministic 64-bit path accepted {n}");
    }

    private static void TestRhoSemiprimes(CancellationToken cancellationToken)
    {
        (BigInteger P, BigInteger Q)[] cases =
        [
            (101, 113),
            (10_007, 10_009),
            (1_000_003, 1_000_033),
            (4_294_967_311, 4_294_967_357),
            (32_416_190_071, 32_416_190_087)
        ];

        foreach ((BigInteger p, BigInteger q) in cases)
            AssertRhoSplits(p * q, cancellationToken);
    }

    private static void TestRhoRepeatedFactors(CancellationToken cancellationToken)
    {
        BigInteger[] cases =
        [
            101 * 101,
            10_007 * 10_007,
            (BigInteger)1_000_003 * 1_000_003,
            (BigInteger)1_000_003 * 1_000_003 * 1_000_033
        ];

        foreach (BigInteger n in cases)
            AssertRhoSplits(n, cancellationToken);
    }

    private static void TestFactorization(CancellationToken cancellationToken)
    {
        AssertFactors(8051, [83, 97], workers: 1, cancellationToken);
        AssertFactors(101, [101], workers: 1, cancellationToken);
        AssertFactors(BigInteger.Pow(2, 12), Enumerable.Repeat((BigInteger)2, 12), workers: 1, cancellationToken);
        AssertFactors(BigInteger.Pow(3, 8), Enumerable.Repeat((BigInteger)3, 8), workers: 1, cancellationToken);
        AssertFactors((BigInteger)1_000_003 * 1_000_003, [1_000_003, 1_000_003], workers: 1, cancellationToken);
        AssertFactors(7 * 1_000_003, [7, 1_000_003], workers: 1, cancellationToken);

        AssertFactors(61 * 97, [61, 97], workers: 1, cancellationToken, pMinusOneStage1Bound: 5, pMinusOneStage2Bound: 5);
        AssertFactors(23 * 47, [23, 47], workers: 1, cancellationToken, pMinusOneStage1Bound: 5, pMinusOneStage2Bound: 11);

        var random = new Random(42);
        int[] primes = [101, 103, 107, 109, 113, 127, 131, 137, 139, 149, 151, 157];
        for (int i = 0; i < 20; i++)
        {
            BigInteger input = BigInteger.One;
            for (int j = 0; j < 4; j++)
                input *= primes[random.Next(primes.Length)];

            List<BigInteger> factors = RunPipeline(input, workers: 1, cancellationToken);
            Assert(factors.Aggregate(BigInteger.One, (acc, factor) => acc * factor) == input, $"factor product mismatch for {input}");
        }
    }

    private static void TestScheduleCache()
    {
        string directory = Path.Combine(Path.GetTempPath(), "LargePrimeCli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string missing = Path.Combine(directory, "missing.txt");
            long[] built = PMinusOneRootScheduleCache.LoadOrCreate(missing, 11, quiet: true);
            Assert(built.Length > 0 && File.Exists(missing), "missing schedule cache was not built");

            string staleBound = Path.Combine(directory, "stale-bound.txt");
            WriteSchedule(staleBound, bound: 5, count: 3, hash: ScheduleHash([2, 3, 5]), [2, 3, 5]);
            PMinusOneRootScheduleCache.LoadOrCreate(staleBound, 7, quiet: true);
            Assert(File.ReadAllText(staleBound).Contains("# bound=7"), "stale schedule bound was not rejected and rebuilt");

            string badCount = Path.Combine(directory, "bad-count.txt");
            WriteSchedule(badCount, bound: 7, count: 99, hash: ScheduleHash([4, 3, 5, 7]), [4, 3, 5, 7]);
            PMinusOneRootScheduleCache.LoadOrCreate(badCount, 7, quiet: true);
            Assert(File.ReadAllText(badCount).Contains("# count=4"), "bad schedule count was not rejected and rebuilt");

            string badHash = Path.Combine(directory, "bad-hash.txt");
            WriteSchedule(badHash, bound: 7, count: 4, hash: "BAD", [4, 3, 5, 7]);
            PMinusOneRootScheduleCache.LoadOrCreate(badHash, 7, quiet: true);
            Assert(!File.ReadAllText(badHash).Contains("# sha256=BAD"), "bad schedule SHA-256 was not rejected and rebuilt");

            string atomic = Path.Combine(directory, "atomic.txt");
            File.WriteAllText(Path.Combine(directory, ".atomic.txt.interrupted.tmp"), "partial write");
            PMinusOneRootScheduleCache.LoadOrCreate(atomic, 13, quiet: true);
            long[] loaded = PMinusOneRootScheduleCache.LoadOrCreate(atomic, 13, quiet: true);
            Assert(loaded.Length > 0 && File.ReadAllText(atomic).Contains("# bound=13"), "atomic schedule cache write did not produce a reusable valid cache");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestPocklington(CancellationToken cancellationToken)
    {
        var prover = new PocklingtonPrimalityProver(new DeterministicRandomSource(2468));
        Assert(prover.TryProve(101, cancellationToken, out PocklingtonProof proof101), "failed to prove 101 with Pocklington");
        Assert(proof101.Steps.Count > 0, "Pocklington proof for 101 had no steps");
        Assert(prover.TryProve(1_000_003, cancellationToken, out _), "failed to prove 1000003 with Pocklington");
        Assert(!prover.TryProve(8051, cancellationToken, out _), "Pocklington proved composite 8051");
    }

    private static void TestConcurrency(CancellationToken cancellationToken)
    {
        BigInteger input = (BigInteger)83 * 97 * 101 * 103;
        List<BigInteger>? expected = null;
        foreach (int workers in new[] { 1, 2, 4, 8 })
        {
            List<BigInteger> actual = RunPipeline(input, workers, cancellationToken);
            expected ??= actual;
            AssertSequence(expected, actual, $"worker={workers} factor multiset");
        }
    }

    private static void AssertRhoSplits(BigInteger n, CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;

        for (int seed = 1; seed <= 8; seed++)
        {
            try
            {
                BigInteger divisor = new RhoSplitter(new DeterministicRandomSource(seed)).Split(n, cancellationToken);
                if (divisor > 1 && divisor < n && n % divisor == 0)
                    return;

                lastFailure = new InvalidOperationException($"rho returned invalid divisor {divisor} for {n} with seed {seed}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ex;
            }
        }

        throw new InvalidOperationException($"rho failed to split {n} with deterministic seeds 1..8", lastFailure);
    }

    private static void AssertFactors(
        BigInteger input,
        IEnumerable<BigInteger> expected,
        int workers,
        CancellationToken cancellationToken,
        int pMinusOneStage1Bound = 100,
        int pMinusOneStage2Bound = 100)
    {
        List<BigInteger> actual = RunPipeline(input, workers, cancellationToken, pMinusOneStage1Bound, pMinusOneStage2Bound);
        AssertSequence(expected.OrderBy(x => x).ToArray(), actual, $"factorization of {input}");
    }

    private static List<BigInteger> RunPipeline(
        BigInteger input,
        int workers,
        CancellationToken cancellationToken,
        int pMinusOneStage1Bound = 100,
        int pMinusOneStage2Bound = 100)
    {
        IRandomSource random = new DeterministicRandomSource(12345);
        var pipeline = new FactorizationPipeline(
            new TrialDivider([2, 3, 5, 7], [], null, 0),
            new MillerRabinPrimalityTest(random),
            new PMinusOneSplitter(PowersForBound(pMinusOneStage1Bound), pMinusOneStage1Bound, pMinusOneStage2Bound, random),
            new RhoSplitter(random),
            workers,
            millerRabinRounds: 16);

        TextWriter originalOut = Console.Out;
        using var capturedOut = new StringWriter();
        Console.SetOut(capturedOut);
        try
        {
            return pipeline.Factor(input, quiet: true, cancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static long[] PowersForBound(int bound)
    {
        return PrimeUtilities.GenerateSmallPrimes(bound)
            .Select(p =>
            {
                long power = p;
                while (power <= bound / p)
                    power *= p;
                return power;
            })
            .ToArray();
    }

    private static bool IsPrime64Deterministic(ulong n)
    {
        if (n < 2) return false;
        foreach (ulong p in new ulong[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 })
        {
            if (n == p) return true;
            if (n % p == 0) return false;
        }

        ulong d = n - 1;
        int s = 0;
        while ((d & 1) == 0)
        {
            d >>= 1;
            s++;
        }

        foreach (ulong a in new ulong[] { 2, 3, 5, 7, 11, 13, 17 })
        {
            if (a >= n) continue;
            BigInteger x = BigInteger.ModPow(a, d, n);
            if (x == 1 || x == n - 1) continue;

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

    private static void WriteSchedule(string path, int bound, int count, string hash, IEnumerable<long> powers)
    {
        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine("# format=LargePrimeCli.PMinusOneRootSchedule.v1");
        writer.WriteLine($"# bound={bound}");
        writer.WriteLine($"# count={count}");
        writer.WriteLine($"# sha256={hash}");
        foreach (long power in powers)
            writer.WriteLine(power);
    }

    private static string ScheduleHash(IEnumerable<long> powers)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        string payload = string.Join('\n', powers) + "\n";
        byte[] hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string name)
    {
        T[] expectedArray = expected.ToArray();
        T[] actualArray = actual.ToArray();
        if (!expectedArray.SequenceEqual(actualArray))
            throw new InvalidOperationException($"{name}: expected [{string.Join(", ", expectedArray)}], got [{string.Join(", ", actualArray)}]");
    }
}
