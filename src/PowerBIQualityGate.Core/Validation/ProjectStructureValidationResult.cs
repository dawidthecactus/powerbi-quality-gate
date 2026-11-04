namespace PowerBIQualityGate.Core.Validation;

public enum ReportDefinitionFormat
{
    Unknown,
    Pbir,
    PbirLegacy
}

public enum SemanticModelDefinitionFormat
{
    Unknown,
    Tmdl,
    Tmsl,
    External
}

public sealed record ProjectStructureValidationResult(
    string? ReportDirectory,
    ReportDefinitionFormat ReportFormat,
    string? SemanticModelDirectory,
    SemanticModelDefinitionFormat SemanticModelFormat,
    IReadOnlyList<ProjectDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}
