namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// A rule-specific observation. Location should identify a model object,
/// for example Sales[ProductId] or the model name for model-wide rules.
/// </summary>
public sealed record ModelQualityIssue(
    string Location,
    string Message,
    string WhyItMatters,
    string HowToFix);
