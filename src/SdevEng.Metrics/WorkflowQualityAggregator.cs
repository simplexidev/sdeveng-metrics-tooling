namespace SdevEng.Metrics;

public sealed record WorkflowQualityAggregate(
    int FirstPassSuccessCount,
    int EligibleWorkUnitCount,
    int RepairCommitCount,
    IReadOnlyList<WorkflowQualityDimensionCount> ByRepositoryStage,
    IReadOnlyList<WorkflowQualityWorkUnitCount> ByWorkUnit);

public sealed record WorkflowQualityDimensionCount(string Repository, string? StageId, int FirstPassSuccessCount, int EligibleWorkUnitCount, int RepairCommitCount);

public sealed record WorkflowQualityWorkUnitCount(string Repository, string StageId, string WorkUnitId, int FirstPassSuccessCount, int EligibleWorkUnitCount, int RepairCommitCount);

public static class WorkflowQualityAggregator
{
    public static WorkflowQualityAggregate Aggregate(IEnumerable<WorkflowQualityDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var observations = documents.SelectMany(document =>
        {
            WorkflowQualityObservationParser.Validate(document);
            return document.Observations;
        }).GroupBy(item => item.ObservationId, StringComparer.Ordinal).Select(group =>
        {
            var first = group.First();
            if (group.Any(item => item != first))
                throw new InvalidOperationException($"Conflicting observations share observationId '{first.ObservationId}'.");
            return first;
        }).ToArray();

        var workUnits = observations.Where(item => item.Kind == WorkflowObservationKind.WorkUnitValidation)
            .GroupBy(item => (Repository: item.Repository, StageId: item.StageId!, WorkUnitId: item.WorkUnitId!), WorkUnitKeyComparer.Instance)
            .Select(group => new
            {
                group.Key.Repository,
                StageId = group.Key.StageId,
                WorkUnitId = group.Key.WorkUnitId,
                FirstPassSuccess = group.Any(item => item.AttemptOrdinal == 1 && item.Result == WorkflowObservationResult.Passed)
                    ? 1 : 0
            }).ToArray();
        var repairs = observations.Where(item => item.Kind == WorkflowObservationKind.RepairCommit)
            .GroupBy(item => (Repository: item.Repository, StageId: item.StageId!, WorkUnitId: item.WorkUnitId!), WorkUnitKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.Count(), WorkUnitKeyComparer.Instance);

        var byWorkUnit = workUnits.Select(unit => new WorkflowQualityWorkUnitCount(
            unit.Repository, unit.StageId, unit.WorkUnitId, unit.FirstPassSuccess, 1,
            repairs.GetValueOrDefault((unit.Repository, unit.StageId, unit.WorkUnitId)))).ToArray();
        var allKeys = workUnits.Select(unit => (unit.Repository, unit.StageId))
            .Concat(repairs.Keys.Select(key => (key.Repository, key.StageId)))
            .Distinct(RepositoryStageKeyComparer.Instance).ToArray();
        var byRepositoryStage = allKeys.Select(key =>
        {
            var units = byWorkUnit.Where(unit => unit.Repository == key.Repository && unit.StageId == key.StageId).ToArray();
            var repairCount = repairs.Where(pair => pair.Key.Repository == key.Repository && pair.Key.StageId == key.StageId).Sum(pair => pair.Value);
            return new WorkflowQualityDimensionCount(key.Repository, key.StageId,
                units.Sum(unit => unit.FirstPassSuccessCount), units.Length, repairCount);
        }).OrderBy(item => item.Repository, StringComparer.Ordinal).ThenBy(item => item.StageId, StringComparer.Ordinal).ToArray();

        return new WorkflowQualityAggregate(byWorkUnit.Sum(item => item.FirstPassSuccessCount), byWorkUnit.Length,
            byWorkUnit.Sum(item => item.RepairCommitCount), byRepositoryStage, byWorkUnit
                .OrderBy(item => item.Repository, StringComparer.Ordinal).ThenBy(item => item.StageId, StringComparer.Ordinal)
                .ThenBy(item => item.WorkUnitId, StringComparer.Ordinal).ToArray());
    }

    private sealed class WorkUnitKeyComparer : IEqualityComparer<(string Repository, string StageId, string WorkUnitId)>
    {
        public static readonly WorkUnitKeyComparer Instance = new();
        public bool Equals((string Repository, string StageId, string WorkUnitId) x, (string Repository, string StageId, string WorkUnitId) y) =>
            StringComparer.Ordinal.Equals(x.Repository, y.Repository) && StringComparer.Ordinal.Equals(x.StageId, y.StageId) && StringComparer.Ordinal.Equals(x.WorkUnitId, y.WorkUnitId);
        public int GetHashCode((string Repository, string StageId, string WorkUnitId) value) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.Repository), StringComparer.Ordinal.GetHashCode(value.StageId), StringComparer.Ordinal.GetHashCode(value.WorkUnitId));
    }

    private sealed class RepositoryStageKeyComparer : IEqualityComparer<(string Repository, string StageId)>
    {
        public static readonly RepositoryStageKeyComparer Instance = new();
        public bool Equals((string Repository, string StageId) x, (string Repository, string StageId) y) =>
            StringComparer.Ordinal.Equals(x.Repository, y.Repository) && StringComparer.Ordinal.Equals(x.StageId, y.StageId);
        public int GetHashCode((string Repository, string StageId) value) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.Repository), StringComparer.Ordinal.GetHashCode(value.StageId));
    }
}
