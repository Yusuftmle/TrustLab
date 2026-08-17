using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;

namespace TrustLab.Guardrails.CircuitBreaker;

public sealed class DeterministicCircuitBreaker : ICircuitBreaker
{
    private readonly int _maxConsecutiveFailures;

    public DeterministicCircuitBreaker(int maxConsecutiveFailures = 2)
    {
        _maxConsecutiveFailures = Math.Max(1, maxConsecutiveFailures);
    }

    public bool ShouldTrip(GuardrailVerdict verdict, int consecutiveFailureCount)
    {
        if (verdict.IsValid)
        {
            return false;
        }

        return consecutiveFailureCount >= _maxConsecutiveFailures ||
               verdict.PrimaryFailureReason == ValidationFailureReason.ContextDeficit;
    }

    public string GetSafeFallbackResponse(string query, ValidationFailureReason reason)
    {
        return reason switch
        {
            ValidationFailureReason.ContextDeficit =>
                "I cannot answer this query because the retrieved knowledge base lacks sufficient factual context to ground the response reliably.",

            ValidationFailureReason.UngroundedClaim or ValidationFailureReason.EntityHallucination =>
                "I am unable to provide a verified answer. Candidate responses failed factual grounding checks against verified source documents.",

            ValidationFailureReason.SchemaViolation =>
                "{\"status\": \"error\", \"message\": \"Deterministic guardrail rejected response due to schema non-compliance.\"}",

            _ => "Verification gate failed. Safe deterministic fallback dispatched to prevent hallucination."
        };
    }
}
