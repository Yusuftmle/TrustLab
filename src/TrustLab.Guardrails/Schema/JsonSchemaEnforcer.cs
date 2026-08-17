using System.Text.Json;
using System.Text.RegularExpressions;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Common;

namespace TrustLab.Guardrails.Schema;

public sealed class JsonSchemaEnforcer : ISchemaValidator
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public Result<T> ValidateAndRepairJson<T>(string rawJsonOutput)
    {
        if (string.IsNullOrWhiteSpace(rawJsonOutput))
        {
            return Result<T>.Failure("Guardrail.EmptyOutput", "LLM output is null or empty.");
        }

        string cleaned = CleanMarkdownAndNoise(rawJsonOutput);

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(cleaned, DefaultOptions);
            if (parsed is null)
            {
                return Result<T>.Failure("Guardrail.NullParsedObject", "JSON deserialized to null object.");
            }

            return Result<T>.Success(parsed);
        }
        catch (JsonException ex)
        {
            // Attempt deterministic auto-repair for common LLM malformed JSON issues
            string repaired = AttemptDeterministicJsonRepair(cleaned);
            try
            {
                var repairedParsed = JsonSerializer.Deserialize<T>(repaired, DefaultOptions);
                if (repairedParsed is not null)
                {
                    return Result<T>.Success(repairedParsed);
                }
            }
            catch
            {
                // Fall through to failure
            }

            return Result<T>.Failure("Guardrail.SchemaViolation", $"Failed to parse JSON against target schema: {ex.Message}");
        }
    }

    public Result<string> ValidateRawJsonSchema(string rawJsonOutput, string expectedJsonSchema)
    {
        if (string.IsNullOrWhiteSpace(rawJsonOutput))
        {
            return Result<string>.Failure("Guardrail.EmptyOutput", "Output is empty.");
        }

        string cleaned = CleanMarkdownAndNoise(rawJsonOutput);
        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            return Result<string>.Success(cleaned);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure("Guardrail.InvalidJson", $"Invalid JSON format: {ex.Message}");
        }
    }

    public static string CleanMarkdownAndNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string trimmed = text.Trim();

        // Strip ```json ... ``` or ``` ... ``` code blocks
        var match = Regex.Match(trimmed, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            trimmed = match.Groups[1].Value.Trim();
        }

        return trimmed;
    }

    public static string AttemptDeterministicJsonRepair(string brokenJson)
    {
        if (string.IsNullOrWhiteSpace(brokenJson)) return "{}";

        string fixedJson = brokenJson.Trim();

        // 1. Convert single quotes to double quotes for keys/values
        fixedJson = Regex.Replace(fixedJson, @"(?<=[\{\[,:])\s*'([^']*)'\s*(?=[\}\],:])", "\"$1\"");

        // 2. Remove trailing commas before closing braces/brackets
        fixedJson = Regex.Replace(fixedJson, @",\s*(\]|\})", "$1");

        // 3. Balance missing closing brackets/braces
        int openBraces = fixedJson.Count(c => c == '{') - fixedJson.Count(c => c == '}');
        int openBrackets = fixedJson.Count(c => c == '[') - fixedJson.Count(c => c == ']');

        if (openBrackets > 0)
        {
            fixedJson += new string(']', openBrackets);
        }

        if (openBraces > 0)
        {
            fixedJson += new string('}', openBraces);
        }

        return fixedJson;
    }
}
