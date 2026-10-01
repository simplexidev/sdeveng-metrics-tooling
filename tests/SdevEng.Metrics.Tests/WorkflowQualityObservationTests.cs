using System.Text.Json;

namespace SdevEng.Metrics.Tests;

public sealed class WorkflowQualityObservationTests
{
    [Fact]
    public void ParsesFixtureCoveringEveryObservationKindAndComparisonOutcome()
    {
        var json = File.ReadAllText(Fixture("workflow-quality-valid-v1.json"));
        var parsed = WorkflowQualityObservationParser.Parse(json);
        Assert.Equal(6, parsed.Observations.Count);
        Assert.Equal(Enum.GetValues<WorkflowObservationKind>().ToHashSet(), parsed.Observations.Select(x => x.Kind).ToHashSet());
        Assert.Equal(1, parsed.Observations.Single(x => x.Kind == WorkflowObservationKind.WorkUnitValidation).AttemptOrdinal);
        Assert.Equal(2, parsed.Observations.Single(x => x.Kind == WorkflowObservationKind.FinalPrRequiredChecks).AttemptOrdinal);
        var advisory = parsed.Observations.Single(x => x.Kind == WorkflowObservationKind.HostedWorkflow);
        Assert.Equal(("3.5", "unit-1"), (advisory.StageId, advisory.WorkUnitId));
        var finalChecks = parsed.Observations.Single(x => x.Kind == WorkflowObservationKind.FinalPrRequiredChecks);
        Assert.Equal(("42", "github-actions", "run-101"), (finalChecks.PullRequestId, finalChecks.Provider, finalChecks.ProviderRunId));
        Assert.Contains(parsed.Observations, x => x.Kind == WorkflowObservationKind.VerificationComparison && x.LocalResult == x.HostedResult);
        Assert.Contains(parsed.Observations, x => x.Kind == WorkflowObservationKind.VerificationComparison && x.LocalResult != x.HostedResult);
    }

    [Fact]
    public void RejectsDuplicateProviderEventIdentity()
    {
        var json = File.ReadAllText(Fixture("workflow-quality-valid-v1.json"));
        var duplicated = json.Replace("\"observationId\": \"hosted-u1-attempt-1\"", "\"observationId\": \"local-u1-attempt-1\"");
        Assert.Throws<JsonException>(() => WorkflowQualityObservationParser.Parse(duplicated));
    }

    [Theory]
    [InlineData("\"commitSha\": \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", "\"commitSha\": \"abcdef1\"")]
    [InlineData("\"durationMilliseconds\": 5000", "\"durationMilliseconds\": 5001")]
    [InlineData("\"result\": \"passed\"", "\"result\": \"unknown\"")]
    [InlineData("\"completedAt\": \"2026-09-30T10:00:05Z\"", "\"completedAt\": \"2026-09-30T09:59:59Z\"")]
    public void RejectsInvalidShaEnumAndTiming(string oldValue, string newValue)
    {
        var json = File.ReadAllText(Fixture("workflow-quality-valid-v1.json"));
        Assert.Throws<JsonException>(() => WorkflowQualityObservationParser.Parse(json.Replace(oldValue, newValue, StringComparison.Ordinal)));
    }

    [Fact]
    public void RejectsAmbiguousFieldsThatDoNotApplyToKind()
    {
        var json = File.ReadAllText(Fixture("workflow-quality-valid-v1.json"));
        var ambiguous = json.Replace("\"workUnitId\": \"unit-1\", \"attemptOrdinal\": 1", "\"workUnitId\": \"unit-1\", \"attemptOrdinal\": 1, \"ciClass\": \"final\"");
        Assert.Throws<JsonException>(() => WorkflowQualityObservationParser.Parse(ambiguous));
    }

    [Fact]
    public void RejectsAdvisoryWorkflowWithoutWorkUnitBinding()
    {
        var json = File.ReadAllText(Fixture("workflow-quality-valid-v1.json"))
            .Replace(", \"stageId\": \"3.5\", \"workUnitId\": \"unit-1\"", string.Empty, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => WorkflowQualityObservationParser.Parse(json));
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
