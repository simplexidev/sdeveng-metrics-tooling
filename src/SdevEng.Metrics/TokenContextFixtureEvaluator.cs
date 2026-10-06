using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SdevEng;
using SdevEng.Infrastructure;

namespace SdevEng.Metrics;

/// <summary>Public synthetic golden evaluation; uses the producer's counter without inference.</summary>
public static class TokenContextFixtureEvaluator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<RenderedInputTokenMeasurement> Evaluate(string fixtureRoot, IEvaluationExecutor inference)
    {
        ArgumentNullException.ThrowIfNull(inference);
        var tokenizers = new TokenizerRegistry(fixtureRoot);
        var adapter = tokenizers.RegisterFile(Path.Combine(fixtureRoot, "tokenizer.json"));
        if (!adapter.Manifest.FixtureOnly) throw new InvalidDataException("Only synthetic fixture tokenizers are permitted.");
        var templates = new ChatTemplateRegistry(tokenizers);
        var template = templates.RegisterFile(Path.Combine(fixtureRoot, "template.json"));
        if (!template.FixtureOnly) throw new InvalidDataException("Only synthetic fixture templates are permitted.");
        var counter = new RenderedInputTokenCounter(templates);
        var results = new List<RenderedInputTokenMeasurement>();
        foreach (var name in new[] { "empty", "unicode", "boundary" })
        {
            var prompt = Read<PromptManifest>(fixtureRoot, name + ".prompt.json");
            prompt.Validate();
            var text = Read<Dictionary<string, string>>(fixtureRoot, name + ".text.json");
            foreach (var component in prompt.Components)
            {
                var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text[component.ContentReference]))).ToLowerInvariant();
                if (digest != component.ContentHash) throw new InvalidDataException("Fixture content hash mismatch.");
            }
            var golden = Read<Golden>(fixtureRoot, name + ".golden.json");
            var rendering = templates.Render(template.Id, template.Revision, template.Checksum, prompt, text);
            var result = counter.CountAttributed(template.Id, template.Revision, template.Checksum, prompt, text);
            result.Validate();
            if (!rendering.Available || rendering.Text != golden.RenderedInput || result.MeasurementKind != "exact" ||
                result.Tokens != golden.Tokens || result.Utf8Bytes != golden.Utf8Bytes || result.RenderedInputDigest != golden.RenderedInputDigest)
                throw new InvalidDataException($"Token/context golden mismatch: {name}");
            results.Add(result);
        }
        return results;
    }

    private static T Read<T>(string root, string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(root, name)), Json)
        ?? throw new JsonException("Empty fixture.");

    private sealed record Golden(string RenderedInput, long Utf8Bytes, long Tokens, string RenderedInputDigest);

    public sealed class RejectInferenceExecutor : IEvaluationExecutor
    {
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Token/context fixtures must never invoke inference.");
    }
}
