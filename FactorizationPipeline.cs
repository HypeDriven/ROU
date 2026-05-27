using System.Numerics;

namespace LargePrimeCli;

public sealed class FactorizationPipeline
{
    private readonly TrialDivider _trialDivider;
    private readonly IPrimalityTest _primalityTest;
    private readonly PMinusOneSplitter _pMinusOneSplitter;
    private readonly RhoSplitter _rhoSplitter;
    private readonly int _workers;
    private readonly int _millerRabinRounds;

    public FactorizationPipeline(
        TrialDivider trialDivider,
        IPrimalityTest primalityTest,
        PMinusOneSplitter pMinusOneSplitter,
        RhoSplitter rhoSplitter,
        int workers,
        int millerRabinRounds)
    {
        _trialDivider = trialDivider;
        _primalityTest = primalityTest;
        _pMinusOneSplitter = pMinusOneSplitter;
        _rhoSplitter = rhoSplitter;
        _workers = Math.Max(1, workers);
        _millerRabinRounds = Math.Max(1, millerRabinRounds);
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

        void AddFactor(BigInteger factor, string evidence)
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
                    ProcessComposite(value, AddFactor, Enqueue, cancellationToken);
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

        try
        {
            Task.WaitAll(tasks, cancellationToken);
        }
        catch (AggregateException ex) when (ex.Flatten().InnerExceptions.OfType<FactorizationFailedException>().FirstOrDefault() is { } failure)
        {
            throw failure;
        }

        lock (factorLock)
            factors.Sort();

        return factors;
    }

    private void ProcessComposite(
        BigInteger n,
        Action<BigInteger, string> addFactor,
        Action<BigInteger> enqueue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (n == 1)
            return;

        n = _trialDivider.Divide(n, addFactor, cancellationToken);
        if (n == 1)
            return;

        if (_primalityTest.IsProbablePrime(n, _millerRabinRounds))
        {
            addFactor(n, "Probable prime factor");
            return;
        }

        const string methodsAttempted = "trial division, probable-prime test, Pollard p-1 stage 1/2, Pollard rho";
        BigInteger divisor = _pMinusOneSplitter.Split(n, cancellationToken);
        string lastMethodAttempted = "Pollard p-1 stage 1/2";
        if (divisor <= 1 || divisor >= n)
        {
            divisor = _rhoSplitter.Split(n, cancellationToken);
            lastMethodAttempted = "Pollard rho";
        }

        if (divisor <= 1 || divisor >= n)
        {
            throw new FactorizationFailedException(
                n,
                methodsAttempted,
                lastMethodAttempted,
                "Try increasing --pminus1-bound and/or --pminus1-stage2-bound; try --baillie-psw or more --miller-rabin-rounds for probable-prime screening; reduce --workers if the machine is oversubscribed, or increase --workers if CPU capacity is available.");
        }

        enqueue(divisor);
        enqueue(n / divisor);
    }
}
