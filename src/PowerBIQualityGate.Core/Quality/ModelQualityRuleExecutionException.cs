namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// Signals an invalid result or failure from a registered rule. A rule error
/// is not silently converted into a clean quality audit.
/// </summary>
public sealed class ModelQualityRuleExecutionException : Exception
{
    public string RuleCode { get; }

    public ModelQualityRuleExecutionException(string ruleCode, Exception innerException)
        : base($"Quality rule {ruleCode} could not be evaluated.", innerException)
    {
        RuleCode = ruleCode;
    }
}
