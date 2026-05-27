using System.Numerics;

namespace LargePrimeCli;

public sealed class WheelCandidateGenerator
{
    private static readonly int[] Residues = [1, 7, 11, 13, 17, 19, 23, 29];
    private static readonly int[] Deltas = [6, 4, 2, 4, 2, 4, 6, 2];

    public IEnumerable<BigInteger> Generate(BigInteger start = default, CancellationToken cancellationToken = default)
    {
        if (start <= 2)
            yield return 2;
        if (start <= 3)
            yield return 3;
        if (start <= 5)
            yield return 5;

        BigInteger candidate = start <= 7 ? 7 : start;
        while (BigInteger.GreatestCommonDivisor(candidate, 30) != 1)
            candidate++;

        int residue = (int)(candidate % 30);
        if (residue < 0)
            residue += 30;
        int deltaIndex = Array.IndexOf(Residues, residue);
        if (deltaIndex < 0)
            throw new InvalidOperationException("Wheel candidate residue was not coprime to 30.");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return candidate;
            candidate += Deltas[deltaIndex];
            deltaIndex = (deltaIndex + 1) % Deltas.Length;
        }
    }
}
