using FluentAssertions;
using TrustLab.Domain.Models;
using TrustLab.Guardrails.CircuitBreaker;
using TrustLab.Guardrails.Grounding;
using TrustLab.Guardrails.Schema;
using Xunit;

namespace TrustLab.UnitTests.Guardrails;

public record TestStructuredModel(string Title, int ReliabilityScore, bool IsProductionReady);

public class GuardrailTests
{
    [Fact]
    public void JsonSchemaEnforcer_ShouldParseAndAutoRepairMalformedJson()
    {
        // Arrange
        var enforcer = new JsonSchemaEnforcer();
        string rawMarkdownWithBrokenJson = """
            Here is the response:
            ```json
            {
                'Title': 'Deterministic Guardrails',
                'ReliabilityScore': 100,
                'IsProductionReady': true,
            }
            ```
            """;

        // Act
        var result = enforcer.ValidateAndRepairJson<TestStructuredModel>(rawMarkdownWithBrokenJson);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Deterministic Guardrails");
        result.Value.ReliabilityScore.Should().Be(100);
        result.Value.IsProductionReady.Should().BeTrue();
    }

    [Fact]
    public async Task NgramGroundingGuard_ShouldPassGroundedResponse_AndRejectHallucination()
    {
        // Arrange
        var guard = new NgramGroundingGuard();
        var context = new List<Chunk>
        {
            Chunk.Create("c1", "d1", "TrustLab achieves zero hallucinations using strict context grounding and circuit breakers.", 0, 0, 85)
        };

        string groundedResponse = "TrustLab achieves zero hallucinations through strict context grounding and circuit breakers.";
        string hallucinatedResponse = "TrustLab uses PostgreSQL database and Redis caching to store user passwords.";

        // Act
        var passVerdict = await guard.VerifyGroundingAsync(groundedResponse, context);
        var rejectVerdict = await guard.VerifyGroundingAsync(hallucinatedResponse, context);

        // Assert
        passVerdict.IsValid.Should().BeTrue();
        passVerdict.FaithfulnessScore.Should().BeGreaterThanOrEqualTo(0.85f);

        rejectVerdict.IsValid.Should().BeFalse();
        rejectVerdict.PrimaryFailureReason.Should().Be(ValidationFailureReason.UngroundedClaim);
        rejectVerdict.Violations.Should().NotBeEmpty();
    }

    [Fact]
    public void DeterministicCircuitBreaker_ShouldTripWhenFailuresExceedThreshold()
    {
        // Arrange
        var breaker = new DeterministicCircuitBreaker(maxConsecutiveFailures: 2);
        var failedVerdict = GuardrailVerdict.Reject(
            ValidationFailureReason.UngroundedClaim,
            new[] { "Ungrounded statement" });

        // Act & Assert
        breaker.ShouldTrip(failedVerdict, consecutiveFailureCount: 1).Should().BeFalse();
        breaker.ShouldTrip(failedVerdict, consecutiveFailureCount: 2).Should().BeTrue();

        string fallbackMsg = breaker.GetSafeFallbackResponse("test query", ValidationFailureReason.UngroundedClaim);
        fallbackMsg.Should().Contain("unable to provide a verified answer");
    }
}
