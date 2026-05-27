using System.Security.Cryptography;

namespace LargePrimeCli;

public interface IRandomSource
{
    void FillBytes(Span<byte> bytes);
}

public sealed class CryptographicRandomSource : IRandomSource
{
    public void FillBytes(Span<byte> bytes) => RandomNumberGenerator.Fill(bytes);
}

public sealed class DeterministicRandomSource : IRandomSource
{
    private readonly Random _random;
    private readonly object _sync = new();

    public DeterministicRandomSource(int seed)
    {
        _random = new Random(seed);
    }

    public void FillBytes(Span<byte> bytes)
    {
        lock (_sync)
            _random.NextBytes(bytes);
    }
}
