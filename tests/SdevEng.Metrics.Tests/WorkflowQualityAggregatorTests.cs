namespace SdevEng.Metrics.Tests;

public sealed class WorkflowQualityAggregatorTests
{
    [Fact]
    public void CountsFirstPassAndEligibleUnitsWithoutPromotingRetrySuccess()
    {
        var aggregate = WorkflowQualityAggregator.Aggregate([Document(
            Validation("u1-first", "unit-1", 1, WorkflowObservationResult.Passed),
            Validation("u2-first", "unit-2", 1, WorkflowObservationResult.Failed),
            Validation("u2-retry", "unit-2", 2, WorkflowObservationResult.Passed),
            Validation("u3-first", "unit-3", 1, WorkflowObservationResult.Passed))]);

        Assert.Equal(2, aggregate.FirstPassSuccessCount);
        Assert.Equal(3, aggregate.EligibleWorkUnitCount);
        Assert.Equal(0, aggregate.RepairCommitCount);
        Assert.Equal(3, aggregate.ByWorkUnit.Count);
        Assert.Equal(0, aggregate.ByWorkUnit.Single(item => item.WorkUnitId == "unit-2").FirstPassSuccessCount);
        Assert.Equal(2, aggregate.ByRepositoryStage.Single().FirstPassSuccessCount);
    }

    [Fact]
    public void CountsOnlyExplicitRepairObservationsAndDeduplicatesRepeatedCopies()
    {
        var validation = Validation("u1", "unit-1", 1, WorkflowObservationResult.Passed);
        var repair = new WorkflowQualityObservation("repair-observation", WorkflowObservationKind.RepairCommit,
            "simplexidev/sdeveng", Sha('a'), "stage-1", "unit-1", RepairCommitSha: Sha('b'), RepairObservationId: "repair-1");
        var document = Document(validation, repair);

        var aggregate = WorkflowQualityAggregator.Aggregate([document, document]);

        Assert.Equal(1, aggregate.EligibleWorkUnitCount);
        Assert.Equal(1, aggregate.FirstPassSuccessCount);
        Assert.Equal(1, aggregate.RepairCommitCount);
        Assert.Equal(1, aggregate.ByWorkUnit.Single().RepairCommitCount);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(aggregate),
            System.Text.Json.JsonSerializer.Serialize(WorkflowQualityAggregator.Aggregate([document, document])));
    }

    [Fact]
    public void EmptyObservationsProduceZeroEligibleUnits()
    {
        var aggregate = WorkflowQualityAggregator.Aggregate([Document()]);

        Assert.Equal(0, aggregate.FirstPassSuccessCount);
        Assert.Equal(0, aggregate.EligibleWorkUnitCount);
        Assert.Equal(0, aggregate.RepairCommitCount);
        Assert.Empty(aggregate.ByRepositoryStage);
        Assert.Empty(aggregate.ByWorkUnit);
    }

    [Fact]
    public void RejectsConflictingCopiesWithSameObservationIdentity()
    {
        var passed = Document(Validation("same-id", "unit-1", 1, WorkflowObservationResult.Passed));
        var failed = Document(Validation("same-id", "unit-1", 1, WorkflowObservationResult.Failed));

        Assert.Throws<InvalidOperationException>(() => WorkflowQualityAggregator.Aggregate([passed, failed]));
    }

    [Fact]
    public void CountsHostedRerunsByAttemptAndClassWithoutDuplicateInflation()
    {
        var initial = Hosted("h1", "run-1", 1, WorkflowCiClass.Advisory);
        var rerun = Hosted("h2", "run-1", 2, WorkflowCiClass.Advisory);
        var finalRerun = Final("h3", "run-2", 3, Sha('d'), "10:00:00", "10:00:05");

        var aggregate = WorkflowQualityAggregator.Aggregate([Document(initial, rerun, finalRerun), Document(initial, rerun)]);

        Assert.Equal(2, aggregate.WorkflowRerunCount);
        Assert.Equal(1, aggregate.AdvisoryWorkflowRerunCount);
        Assert.Equal(1, aggregate.FinalWorkflowRerunCount);
    }

    [Fact]
    public void ReportsAdvisoryLatencyPerExactWorkUnitCommitAndKeepsRerunsAsSeparateSamples()
    {
        var initial = Hosted("h1", "run-1", 1, WorkflowCiClass.Advisory) with { CommitSha = Sha('a') };
        var rerun = Hosted("h2", "run-1", 2, WorkflowCiClass.Advisory) with
        {
            CommitSha = Sha('b'),
            DurationMilliseconds = 8000,
            StartedAt = "2026-09-30T10:00:00Z",
            CompletedAt = "2026-09-30T10:00:08Z"
        };

        var aggregate = WorkflowQualityAggregator.Aggregate([Document(initial, rerun), Document(initial)]);

        Assert.Collection(aggregate.AdvisoryWorkUnitCiLatency,
            first => { Assert.Equal(Sha('a'), first.CommitSha); Assert.Equal(5000, first.DurationMilliseconds); Assert.Equal(1, first.AttemptOrdinal); },
            second => { Assert.Equal(Sha('b'), second.CommitSha); Assert.Equal(8000, second.DurationMilliseconds); Assert.Equal(2, second.AttemptOrdinal); });
        Assert.Empty(aggregate.FinalPrRequiredCheckLatency);
    }

    [Fact]
    public void ReportsFinalRequiredCheckLatencyThroughTheLastCheckCompletionForEachHeadAndAttempt()
    {
        var checkOne = Final("final-1", "check-1", 1, Sha('d'), "11:00:00", "11:00:10");
        var checkTwo = Final("final-2", "check-2", 1, Sha('d'), "11:00:02", "11:00:20");
        var rerun = Final("final-rerun", "check-1", 2, Sha('d'), "11:01:00", "11:01:30");
        var otherHead = Final("other-head", "check-other-head", 1, Sha('e'), "11:00:00", "11:00:03");

        var aggregate = WorkflowQualityAggregator.Aggregate([Document(checkOne, checkTwo, rerun, otherHead), Document(checkOne)]);

        Assert.Collection(aggregate.FinalPrRequiredCheckLatency,
            first => { Assert.Equal(Sha('d'), first.CommitSha); Assert.Equal(20000, first.DurationMilliseconds); Assert.Equal(1, first.AttemptOrdinal); },
            second => { Assert.Equal(Sha('d'), second.CommitSha); Assert.Equal(30000, second.DurationMilliseconds); Assert.Equal(2, second.AttemptOrdinal); },
            third => { Assert.Equal(Sha('e'), third.CommitSha); Assert.Equal(3000, third.DurationMilliseconds); });
        Assert.Empty(aggregate.AdvisoryWorkUnitCiLatency);
    }

    [Fact]
    public void RejectsMissingTimingEvidenceInsteadOfSynthesizingLatency()
    {
        var incomplete = Hosted("incomplete", "run-1", 1, WorkflowCiClass.Advisory) with { CompletedAt = null, DurationMilliseconds = null };

        Assert.Throws<System.Text.Json.JsonException>(() => WorkflowQualityAggregator.Aggregate([Document(incomplete)]));
    }

    [Fact]
    public void KeepsAdvisoryAndFinalTimingPopulationsInDistinctResults()
    {
        var aggregate = WorkflowQualityAggregator.Aggregate([Document(
            Hosted("advisory", "advisory-run", 1, WorkflowCiClass.Advisory),
            Final("final", "required-check", 1, Sha('d'), "11:00:00", "11:00:03"))]);

        Assert.Single(aggregate.AdvisoryWorkUnitCiLatency);
        Assert.Single(aggregate.FinalPrRequiredCheckLatency);
        Assert.Equal(5000, aggregate.AdvisoryWorkUnitCiLatency.Single().DurationMilliseconds);
        Assert.Equal(3000, aggregate.FinalPrRequiredCheckLatency.Single().DurationMilliseconds);
        Assert.Equal(("stage-1", "unit-1", null),
            (aggregate.AdvisoryWorkUnitCiLatency.Single().StageId, aggregate.AdvisoryWorkUnitCiLatency.Single().WorkUnitId, aggregate.AdvisoryWorkUnitCiLatency.Single().PullRequestId));
        Assert.Equal((null, null, "42"),
            (aggregate.FinalPrRequiredCheckLatency.Single().StageId, aggregate.FinalPrRequiredCheckLatency.Single().WorkUnitId, aggregate.FinalPrRequiredCheckLatency.Single().PullRequestId));
    }

    [Fact]
    public void CountsSameCommitComparisonDisagreementsAndExcludesMissingSidesAndOtherCommits()
    {
        var agreeing = Comparison("agree", Sha('a'), WorkflowObservationResult.Passed, WorkflowObservationResult.Passed);
        var disagreeing = Comparison("disagree", Sha('a'), WorkflowObservationResult.Passed, WorkflowObservationResult.Failed);
        var differentCommit = Comparison("different-commit", Sha('b'), WorkflowObservationResult.Passed, WorkflowObservationResult.Failed);

        var aggregate = WorkflowQualityAggregator.Aggregate([Document(agreeing, disagreeing, differentCommit)]);

        Assert.Equal(3, aggregate.LocalCiComparisonCount);
        Assert.Equal(2, aggregate.LocalCiDisagreementCount);
    }

    private static WorkflowQualityDocument Document(params WorkflowQualityObservation[] observations) =>
        new("1.0.0", observations);

    private static WorkflowQualityObservation Validation(string id, string unit, int ordinal, WorkflowObservationResult result) =>
        new(id, WorkflowObservationKind.WorkUnitValidation, "simplexidev/sdeveng", Sha('a'), "stage-1", unit,
            ordinal, result);

    private static WorkflowQualityObservation Hosted(string id, string run, int ordinal, WorkflowCiClass ciClass) =>
        new(id, WorkflowObservationKind.HostedWorkflow, "simplexidev/sdeveng", Sha('a'), AttemptOrdinal: ordinal,
            StageId: "stage-1", WorkUnitId: "unit-1", Result: WorkflowObservationResult.Passed, Provider: "github-actions", ProviderRunId: run, CiClass: ciClass,
            StartedAt: "2026-09-30T10:00:00Z", CompletedAt: "2026-09-30T10:00:05Z", DurationMilliseconds: 5000);

    private static WorkflowQualityObservation Final(string id, string run, int ordinal, string sha, string start, string end) =>
        new(id, WorkflowObservationKind.FinalPrRequiredChecks, "simplexidev/sdeveng", sha,
            AttemptOrdinal: ordinal, Result: WorkflowObservationResult.Passed, Provider: "github-actions", ProviderRunId: run,
            CiClass: WorkflowCiClass.Final, PullRequestId: "42", StartedAt: $"2026-09-30T{start}Z",
            CompletedAt: $"2026-09-30T{end}Z", DurationMilliseconds: (long)(TimeSpan.Parse(end) - TimeSpan.Parse(start)).TotalMilliseconds);

    private static WorkflowQualityObservation Comparison(string id, string sha, WorkflowObservationResult local, WorkflowObservationResult hosted) =>
        new(id, WorkflowObservationKind.VerificationComparison, "simplexidev/sdeveng", sha, LocalResult: local, HostedResult: hosted);

    private static string Sha(char character) => new(character, 40);
}
