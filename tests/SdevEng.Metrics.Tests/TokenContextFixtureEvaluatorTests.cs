using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SdevEng;

namespace SdevEng.Metrics.Tests;

public sealed class TokenContextFixtureEvaluatorTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "TokenFixtures");

    [Fact]
    public async Task NormalEntryEvaluatesPinnedGoldensAndEmitsValidMeasurementsWithoutInference()
    {
        var inference = new ThrowingExecutor();
        var results = TokenContextFixtureEvaluator.Evaluate(Root, inference);
        Assert.Equal(0, inference.Calls);
        Assert.Equal(3, results.Count);
        var schema = JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "Schemas", "rendered-input-token-measurement.schema.json"));
        foreach (var result in results)
        {
            result.Validate();
            Assert.True(result.FixtureOnly);
            Assert.Equal("fixture-byte-v1", result.Method);
            Assert.Equal(result.Tokens, result.Attribution!.TotalTokens);
            Assert.True(schema.Evaluate(JsonSerializer.SerializeToNode(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))).IsValid);
        }
        foreach (var (file, contract) in new[] { ("tokenizer.json", "tokenizer-manifest"), ("template.json", "chat-template-manifest") })
        {
            var metadataSchema = JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "Schemas", contract + ".schema.json"));
            Assert.True(metadataSchema.Evaluate(JsonNode.Parse(File.ReadAllText(Path.Combine(Root, file)))).IsValid);
        }
        var promptSchema = JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "Schemas", "prompt-manifest.schema.json"));
        foreach (var file in Directory.GetFiles(Root, "*.prompt.json"))
            Assert.True(promptSchema.Evaluate(JsonNode.Parse(File.ReadAllText(file))).IsValid);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await MetricsCli.RunAsync(["evaluate-token-fixtures", Root], output, error));
        Assert.Equal("", error.ToString());
        var replay = JsonSerializer.Deserialize<RenderedInputTokenMeasurement[]>(output.ToString(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(3, replay.Length);
        foreach (var result in replay) result.Validate();
    }

    [Fact]
    public void GoldenAndAssetTamperingAreRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "token-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var file in Directory.GetFiles(Root)) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
            var path = Path.Combine(root, "empty.golden.json");
            var golden = JsonNode.Parse(File.ReadAllText(path))!;
            golden["tokens"] = 0;
            File.WriteAllText(path, golden.ToJsonString());
            Assert.Throws<InvalidDataException>(() => TokenContextFixtureEvaluator.Evaluate(root, new ThrowingExecutor()));
            File.Copy(Path.Combine(Root, "empty.golden.json"), path, true);
            golden = JsonNode.Parse(File.ReadAllText(path))!;
            golden["attribution"]!["components"]![0]!["tokens"] = 0;
            File.WriteAllText(path, golden.ToJsonString());
            Assert.Throws<InvalidDataException>(() => TokenContextFixtureEvaluator.Evaluate(root, new ThrowingExecutor()));
            File.Copy(Path.Combine(Root, "empty.golden.json"), path, true);
            File.WriteAllBytes(Path.Combine(root, "vocab.bin"), [0]);
            Assert.Throws<InvalidDataException>(() => TokenContextFixtureEvaluator.Evaluate(root, new ThrowingExecutor()));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ThrowingExecutor : IEvaluationExecutor
    {
        public int Calls { get; private set; }
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Inference forbidden.");
        }
    }
}
