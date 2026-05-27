using System.Numerics;

namespace LargePrimeCli;

public sealed record PocklingtonProofStep(
    BigInteger N,
    BigInteger PrimeFactorOfNMinusOne,
    BigInteger Witness);

public sealed record PocklingtonProof(BigInteger N, IReadOnlyList<PocklingtonProofStep> Steps);

public sealed class PocklingtonPrimalityProver
{
    private readonly IPrimalityTest _probablePrimeTest;
    private readonly RhoSplitter _rhoSplitter;
    private readonly Dictionary<BigInteger, PocklingtonProof> _proofCache = new();

    public PocklingtonPrimalityProver(IRandomSource random)
    {
        _probablePrimeTest = new BailliePswPrimalityTest();
        _rhoSplitter = new RhoSplitter(random);
    }

    public bool TryProve(BigInteger n, CancellationToken cancellationToken, out PocklingtonProof proof)
    {
        if (TryProveInternal(n, cancellationToken, out proof))
            return true;

        proof = new PocklingtonProof(n, []);
        return false;
    }

    private bool TryProveInternal(BigInteger n, CancellationToken cancellationToken, out PocklingtonProof proof)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_proofCache.TryGetValue(n, out proof!))
            return true;

        if (n < 2)
        {
            proof = new PocklingtonProof(n, []);
            return false;
        }

        if (n == 2 || n == 3)
        {
            proof = new PocklingtonProof(n, []);
            _proofCache[n] = proof;
            return true;
        }

        if (n.IsEven || !_probablePrimeTest.IsProbablePrime(n))
        {
            proof = new PocklingtonProof(n, []);
            return false;
        }

        BigInteger remaining = n - 1;
        BigInteger provenProduct = BigInteger.One;
        var steps = new List<PocklingtonProofStep>();

        foreach (var group in FactorForProof(remaining, cancellationToken)
            .GroupBy(x => x)
            .OrderByDescending(g => g.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();

            BigInteger q = group.Key;
            int exponent = group.Count();

            if ((n - 1) % q != 0)
                continue;

            if (!TryProveInternal(q, cancellationToken, out PocklingtonProof qProof))
                continue;

            if (!TryFindWitness(n, q, out BigInteger witness))
                continue;

            steps.AddRange(qProof.Steps);
            steps.Add(new PocklingtonProofStep(n, q, witness));
            for (int i = 0; i < exponent; i++)
                provenProduct *= q;

            if (provenProduct > IntegerSqrt(n))
            {
                proof = new PocklingtonProof(n, steps);
                _proofCache[n] = proof;
                return true;
            }
        }

        proof = new PocklingtonProof(n, steps);
        return false;
    }

    private IEnumerable<BigInteger> FactorForProof(BigInteger n, CancellationToken cancellationToken)
    {
        var stack = new Stack<BigInteger>();
        stack.Push(n);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BigInteger value = stack.Pop();
            if (value == 1)
                continue;

            foreach (int p in PrimeUtilities.GenerateSmallPrimes(10_000))
            {
                if (value % p != 0)
                    continue;

                do
                {
                    yield return p;
                    value /= p;
                }
                while (value % p == 0);
            }

            if (value == 1)
                continue;

            if (_probablePrimeTest.IsProbablePrime(value))
            {
                yield return value;
                continue;
            }

            BigInteger divisor = _rhoSplitter.Split(value, cancellationToken);
            if (divisor <= 1 || divisor >= value || value % divisor != 0)
                yield break;

            stack.Push(divisor);
            stack.Push(value / divisor);
        }
    }

    private static bool TryFindWitness(BigInteger n, BigInteger q, out BigInteger witness)
    {
        for (int a = 2; a < 10_000; a++)
        {
            BigInteger candidate = a;
            if (BigInteger.ModPow(candidate, n - 1, n) != 1)
                continue;

            BigInteger test = BigInteger.ModPow(candidate, (n - 1) / q, n) - 1;
            if (BigInteger.GreatestCommonDivisor(test, n) == 1)
            {
                witness = candidate;
                return true;
            }
        }

        witness = BigInteger.Zero;
        return false;
    }

    private static BigInteger IntegerSqrt(BigInteger n)
    {
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
