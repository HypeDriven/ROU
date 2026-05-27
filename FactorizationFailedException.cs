using System.Numerics;

namespace LargePrimeCli;

public sealed class FactorizationFailedException : Exception
{
    public FactorizationFailedException(
        BigInteger residualComposite,
        string methodsAttempted,
        string lastMethodAttempted,
        string suggestedActions)
        : base($"Failed to split residual composite {residualComposite} after {methodsAttempted}.")
    {
        ResidualComposite = residualComposite;
        MethodsAttempted = methodsAttempted;
        LastMethodAttempted = lastMethodAttempted;
        SuggestedActions = suggestedActions;
    }

    public BigInteger ResidualComposite { get; }

    public string MethodsAttempted { get; }

    public string LastMethodAttempted { get; }

    public string SuggestedActions { get; }
}
