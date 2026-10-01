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

    private static WorkflowQualityDocument Document(params WorkflowQualityObservation[] observations) =>
        new("1.0.0", observations);

    private static WorkflowQualityObservation Validation(string id, string unit, int ordinal, WorkflowObservationResult result) =>
        new(id, WorkflowObservationKind.WorkUnitValidation, "simplexidev/sdeveng", Sha('a'), "stage-1", unit,
            ordinal, result);

    private static string Sha(char character) => new(character, 40);
}
