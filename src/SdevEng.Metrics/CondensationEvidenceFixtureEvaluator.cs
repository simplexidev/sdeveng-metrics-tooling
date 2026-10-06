using System.Text.Json;
using System.Text.Json.Nodes;
using SdevEng;
using SdevEng.Infrastructure;

namespace SdevEng.Metrics;

/// <summary>Runs public synthetic condensation and evidence-selection goldens using the producer measurers.</summary>
public static class CondensationEvidenceFixtureEvaluator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static (RequestVariantTokenMeasurement Condensation, EvidenceEfficiencyMeasurement Evidence) Evaluate(string fixtureRoot)
    {
        var tokenizers = new TokenizerRegistry(fixtureRoot);
        var tokenizer = tokenizers.RegisterFile(Path.Combine(fixtureRoot, "tokenizer.json"));
        if (!tokenizer.Manifest.FixtureOnly) throw new InvalidDataException("Only synthetic fixture tokenizers are permitted.");

        var condensationInput = Read<RequestVariantTokenInput>(fixtureRoot, "condensation.input.json");
        var condensation = new RequestVariantTokenMeasurer(tokenizers).Measure(tokenizer.Manifest.Id, condensationInput);
        Compare(condensation, fixtureRoot, "condensation.golden.json", "condensation");

        var evidenceInput = Read<EvidenceInput>(fixtureRoot, "evidence.input.json");
        var candidates = evidenceInput.Candidates.Select(item => new EvidenceEfficiencyItem(item.EvidenceId, item.SourceRevision, item.Text,
            null, null, item.FileLocationKey, item.SymbolLocationKey)).ToArray();
        var evidence = new EvidenceEfficiencyMeasurer(tokenizers).Measure(tokenizer.Manifest.Id, candidates,
            evidenceInput.SelectedEvidenceIds, evidenceInput.Role, evidenceInput.RoleInputBudgetTokens, evidenceInput.RenderedInputTokens);
        Compare(evidence, fixtureRoot, "evidence.golden.json", "evidence selection");
        return (condensation, evidence);
    }

    private static void Compare<T>(T actual, string root, string goldenFile, string label)
    {
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(root, goldenFile)));
        if (!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(actual, Json), expected))
            throw new InvalidDataException($"{label} fixture golden mismatch.");
    }

    private static T Read<T>(string root, string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(root, name)), Json)
        ?? throw new JsonException("Empty fixture.");

    private sealed record EvidenceInput(string Role, long RoleInputBudgetTokens, long RenderedInputTokens,
        IReadOnlyList<string> SelectedEvidenceIds, IReadOnlyList<EvidenceInputItem> Candidates);
    private sealed record EvidenceInputItem(string EvidenceId, string SourceRevision, string Text, string? FileLocationKey, string? SymbolLocationKey);
}
