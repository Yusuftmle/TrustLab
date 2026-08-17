using System.Numerics.Tensors;
using System.Security.Cryptography;
using System.Text;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Common;

namespace TrustLab.Infrastructure.Embedding;

public sealed class DeterministicHashEmbedder : ITextEmbedder
{
    private readonly int _dimensions;

    public int Dimensions => _dimensions;

    public DeterministicHashEmbedder(int dimensions = 128)
    {
        _dimensions = dimensions;
    }

    public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var vector = new float[_dimensions];
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<ReadOnlyMemory<float>>(vector);
        }

        var tokens = Tokenizer.Tokenize(text);
        foreach (var token in tokens)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            for (int i = 0; i < _dimensions; i++)
            {
                // Project token hash into dimension slot
                int byteIdx = i % hash.Length;
                float sign = ((hash[byteIdx] & (1 << (i % 8))) != 0) ? 1.0f : -1.0f;
                vector[i] += sign * (1.0f + (hash[byteIdx] / 255.0f));
            }
        }

        // L2 Normalize the vector using SIMD TensorPrimitives
        float norm = MathF.Sqrt(TensorPrimitives.SumOfSquares<float>(vector));
        if (norm > 1e-6f)
        {
            TensorPrimitives.Divide(vector, norm, vector);
        }

        return Task.FromResult<ReadOnlyMemory<float>>(vector);
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var list = new List<ReadOnlyMemory<float>>(texts.Count);
        foreach (var text in texts)
        {
            list.Add(await GenerateEmbeddingAsync(text, cancellationToken));
        }

        return list;
    }
}
