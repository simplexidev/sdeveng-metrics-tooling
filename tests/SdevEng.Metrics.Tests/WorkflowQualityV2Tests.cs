using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace SdevEng.Metrics.Tests;

public sealed class WorkflowQualityV2Tests
{
    [Fact]
    public void CommitScopedLocalEvidenceNeedsNoInventedRunOrWorkUnit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sdeveng-quality-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = new string('a', 40);
            File.WriteAllText(Path.Combine(directory, "local.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = "2.0.0",
                localValidations = new[] { new { repository = "simplexidev/sdeveng", commitSha = first,
                    attemptOrdinal = 1, check = "build", result = "passed" } },
                repairCommits = new[] { new { repository = "simplexidev/sdeveng", beforeCommitSha = first,
                    repairCommitSha = new string('b', 40) } }
            }));
            var schema = JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "Schemas", "workflow-quality-observations-v2.schema.json"));
            Assert.True(schema.Evaluate(JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "local.json")))).IsValid);
            var summary = WorkflowQualityV2.AggregateDirectory(directory);
            Assert.Equal(1, summary.EligibleLocalScopeCount);
            Assert.Equal(1, summary.FirstPassLocalSuccessCount);
            Assert.Equal(1, summary.RepairCommitCount);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ImportsExactCommitCiAndPrivateLocalEvidenceWithoutStageOrInventedWorkUnit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sdeveng-quality-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sha = new string('a', 40);
            File.WriteAllText(Path.Combine(directory, "local.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = "2.0.0",
                localValidations = new[] { new { repository = "simplexidev/sdeveng", runId = "run-1", commitSha = sha, attemptOrdinal = 1, check = "tests", result = "passed" } },
                repairCommits = new[] { new { repository = "simplexidev/sdeveng", runId = "run-1", beforeCommitSha = sha, repairCommitSha = new string('b', 40) } }
            }));
            void Hosted(string file, int attempt, string tier, string status) => File.WriteAllText(Path.Combine(directory, file), JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                repository = "simplexidev/sdeveng",
                commitSha = sha,
                validationTier = tier,
                runner = "ubuntu-latest",
                providerRunId = "101",
                attemptOrdinal = attempt,
                pullRequestId = tier == "final-pr" ? "42" : null,
                startedAt = "2026-09-30T10:00:00Z",
                completedAt = "2026-09-30T10:00:05Z",
                durationMilliseconds = 5000,
                checks = new[] { new { schemaVersion = 2, source = "hosted", check = "tests", status,
                    exitCode = status == "passed" ? 0 : 1, artifact = "github-actions:101/tests", environmentIdentity = "github:ubuntu-latest:101" } },
                notObserved = Array.Empty<string>()
            }));
            Hosted("advisory-1.json", 1, "advisory-commit", "failed");
            Hosted("advisory-2.json", 2, "advisory-commit", "failed");
            Hosted("final.json", 1, "final-pr", "passed");

            var summary = WorkflowQualityV2.AggregateDirectory(directory);
            Assert.Equal(1, summary.FirstPassLocalSuccessCount);
            Assert.Equal(1, summary.EligibleLocalScopeCount);
            Assert.Equal(1, summary.RepairCommitCount);
            Assert.Equal(1, summary.AdvisoryWorkflowRerunCount);
            Assert.Equal(0, summary.FinalWorkflowRerunCount);
            Assert.Equal(3, summary.LocalHostedComparisonCount);
            Assert.Equal(2, summary.LocalHostedDisagreementCount);
            Assert.Equal(new long[] { 5000, 5000 }, summary.AdvisoryLatencyMilliseconds);
            Assert.Equal(new long[] { 5000 }, summary.FinalPrLatencyMilliseconds);

            var output = Path.Combine(directory, "aggregate.json");
            Assert.Equal(0, await MetricsCli.RunAsync(["aggregate-workflow-quality", directory, output], TextWriter.Null, TextWriter.Null));
            Assert.True(File.Exists(output));
            Assert.DoesNotContain(sha, File.ReadAllText(output), StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RejectsFalseTimingAndDoesNotInferUnobservedLocalSuccess()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sdeveng-quality-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                repository = "simplexidev/sdeveng",
                commitSha = new string('a', 40),
                validationTier = "advisory-commit",
                providerRunId = "101",
                attemptOrdinal = 1,
                startedAt = "2026-09-30T10:00:00Z",
                completedAt = "2026-09-30T10:00:05Z",
                durationMilliseconds = 9000,
                checks = Array.Empty<object>()
            }));
            Assert.Throws<InvalidDataException>(() => WorkflowQualityV2.AggregateDirectory(directory));
            File.Delete(Path.Combine(directory, "result.json"));
            Assert.Equal(0, WorkflowQualityV2.AggregateDirectory(directory).EligibleLocalScopeCount);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void MatrixRerunCountsOnceAndLatencySpansObservedRunnerWindow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sdeveng-quality-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var (runner, end) in new[] { ("ubuntu-latest", "10:00:05"), ("windows-latest", "10:00:09") })
            {
                File.WriteAllText(Path.Combine(directory, runner + ".json"), JsonSerializer.Serialize(new
                {
                    schemaVersion = 2,
                    repository = "simplexidev/sdeveng",
                    commitSha = new string('a', 40),
                    validationTier = "advisory-commit",
                    runner,
                    providerRunId = "101",
                    attemptOrdinal = 2,
                    startedAt = "2026-09-30T10:00:00Z",
                    completedAt = $"2026-09-30T{end}Z",
                    durationMilliseconds = end == "10:00:05" ? 5000 : 9000,
                    checks = new[] { new { schemaVersion = 2, source = "hosted", check = "tests", status = "passed", exitCode = 0, artifact = "github:101/tests", environmentIdentity = "github:" + runner + ":101" } },
                    notObserved = Array.Empty<string>()
                }));
            }
            var summary = WorkflowQualityV2.AggregateDirectory(directory);
            Assert.Equal(1, summary.AdvisoryWorkflowRerunCount);
            Assert.Equal(new long[] { 9000 }, summary.AdvisoryLatencyMilliseconds);
        }
        finally { Directory.Delete(directory, true); }
    }
}
