using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// One read-only, deterministic check against a TOM-derived model snapshot.
/// Rule metadata is stable; returned issues are enriched by the engine.
/// </summary>
public interface IModelQualityRule
{
    /// <summary>Stable rule identifier in the SM-001 style.</summary>
    string Code { get; }

    /// <summary>Human-readable rule name.</summary>
    string Title { get; }

    /// <summary>Severity of issues raised by this rule.</summary>
    DiagnosticSeverity Severity { get; }

    /// <summary>Return zero or more issues without modifying the model.</summary>
    IReadOnlyList<ModelQualityIssue> Evaluate(TomModelSnapshot model);
}
