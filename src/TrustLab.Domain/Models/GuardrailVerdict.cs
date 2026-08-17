namespace TrustLab.Domain.Models;

public enum ValidationFailureReason
{
    None = 0,
    SchemaViolation = 1,
    UngroundedClaim = 2,
    EntityHallucination = 3,
    Contradiction = 4,
    ContextDeficit = 5,
    ConfidenceBelowThreshold = 6,
    CircuitBreakerTripped = 7
}

public sealed record GuardrailVerdict(
    bool IsValid,
    float FaithfulnessScore,
    IReadOnlyList<string> Violations,
    ValidationFailureReason PrimaryFailureReason = ValidationFailureReason.None,
    string? SanitizedOutput = null,
    IReadOnlyDictionary<string, object>? Telemetry = null)
{
    public static GuardrailVerdict Pass(float faithfulnessScore = 1.0f, string? sanitizedOutput = null, IReadOnlyDictionary<string, object>? telemetry = null) =>
        new(true, faithfulnessScore, Array.Empty<string>(), ValidationFailureReason.None, sanitizedOutput, telemetry);

    public static GuardrailVerdict Reject(ValidationFailureReason reason, IReadOnlyList<string> violations, float faithfulnessScore = 0.0f, IReadOnlyDictionary<string, object>? telemetry = null) =>
        new(false, faithfulnessScore, violations, reason, null, telemetry);
}
