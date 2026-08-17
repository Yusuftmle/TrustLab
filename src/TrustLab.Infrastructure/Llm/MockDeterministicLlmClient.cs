using TrustLab.Application.Interfaces;

namespace TrustLab.Infrastructure.Llm;

public sealed class MockDeterministicLlmClient : ILlmClient
{
    private readonly Func<string, string?, int, string> _responseGenerator;
    private int _callCount = 0;

    public int CallCount => _callCount;

    public MockDeterministicLlmClient(Func<string, string?, int, string> responseGenerator)
    {
        _responseGenerator = responseGenerator ?? throw new ArgumentNullException(nameof(responseGenerator));
    }

    public static MockDeterministicLlmClient CreateGrounded(string groundedResponse)
    {
        return new MockDeterministicLlmClient((prompt, sys, count) => groundedResponse);
    }

    public static MockDeterministicLlmClient CreateSelfCorrecting(string initialHallucination, string correctedResponse)
    {
        return new MockDeterministicLlmClient((prompt, sys, count) =>
        {
            if (count == 1)
            {
                return initialHallucination;
            }

            return correctedResponse;
        });
    }

    public static MockDeterministicLlmClient CreatePersistentHallucinator(string persistentHallucination)
    {
        return new MockDeterministicLlmClient((prompt, sys, count) => persistentHallucination);
    }

    public Task<string> GenerateResponseAsync(
        string prompt,
        string? systemInstruction = null,
        float temperature = 0,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        string response = _responseGenerator(prompt, systemInstruction, _callCount);
        return Task.FromResult(response);
    }
}
