using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SdevEng.Metrics;

public enum WorkflowObservationKind { WorkUnitValidation, RepairCommit, HostedWorkflow, VerificationComparison, FinalPrRequiredChecks }
public enum WorkflowObservationResult { Passed, Failed, Cancelled, TimedOut }
public enum WorkflowCiClass { Advisory, Final }

public sealed record WorkflowQualityDocument(string SchemaVersion, IReadOnlyList<WorkflowQualityObservation> Observations);

public sealed record WorkflowQualityObservation(
    string ObservationId,
    WorkflowObservationKind Kind,
    string Repository,
    string CommitSha,
    string? StageId = null,
    string? WorkUnitId = null,
    int? AttemptOrdinal = null,
    WorkflowObservationResult? Result = null,
    string? RepairCommitSha = null,
    string? RepairObservationId = null,
    string? Provider = null,
    string? ProviderRunId = null,
    WorkflowCiClass? CiClass = null,
    string? StartedAt = null,
    string? CompletedAt = null,
    long? DurationMilliseconds = null,
    WorkflowObservationResult? LocalResult = null,
    WorkflowObservationResult? HostedResult = null,
    string? PullRequestId = null);

public static class WorkflowQualityObservationParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static WorkflowQualityDocument Parse(string json)
    {
        var document = JsonSerializer.Deserialize<WorkflowQualityDocument>(json, Options)
            ?? throw new JsonException("Workflow-quality document must be an object.");
        Validate(document);
        return document;
    }

    public static WorkflowQualityDocument Load(string path) => Parse(File.ReadAllText(path));

    public static void Validate(WorkflowQualityDocument document)
    {
        if (document.SchemaVersion != "1.0.0") throw new JsonException("schemaVersion must equal '1.0.0'.");
        if (document.Observations is null) throw new JsonException("observations is required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var repairIds = new HashSet<string>(StringComparer.Ordinal);
        var repairCommits = new HashSet<(string Repository, string Sha)>(new RepositoryShaComparer());
        foreach (var item in document.Observations)
        {
            if (item is null) throw new JsonException("observations cannot contain null.");
            Required(item.ObservationId, "observationId");
            if (!ids.Add(item.ObservationId)) throw new JsonException($"Duplicate observationId '{item.ObservationId}'.");
            Required(item.Repository, "repository");
            Sha(item.CommitSha, "commitSha");
            switch (item.Kind)
            {
                case WorkflowObservationKind.WorkUnitValidation:
                    WorkUnit(item); Ordinal(item.AttemptOrdinal); Required(item.Result, "result");
                    Only(item, item.StageId, item.WorkUnitId, item.AttemptOrdinal, item.Result); break;
                case WorkflowObservationKind.RepairCommit:
                    WorkUnit(item); Required(item.RepairObservationId, "repairObservationId");
                    if (!repairIds.Add(item.RepairObservationId!)) throw new JsonException($"Duplicate repairObservationId '{item.RepairObservationId}'.");
                    Sha(item.RepairCommitSha, "repairCommitSha");
                    if (string.Equals(item.CommitSha, item.RepairCommitSha, StringComparison.OrdinalIgnoreCase)) throw new JsonException("repairCommitSha must identify a distinct commit.");
                    if (!repairCommits.Add((item.Repository, item.RepairCommitSha!))) throw new JsonException($"Duplicate repair commit '{item.RepairCommitSha}'.");
                    Only(item, item.StageId, item.WorkUnitId, item.RepairCommitSha, item.RepairObservationId); break;
                case WorkflowObservationKind.HostedWorkflow:
                    Required(item.Provider, "provider"); Required(item.ProviderRunId, "providerRunId"); Ordinal(item.AttemptOrdinal);
                    Required(item.CiClass, "ciClass"); Required(item.Result, "result"); Timing(item);
                    if (item.CiClass != WorkflowCiClass.Advisory) throw new JsonException("hostedWorkflow must have ciClass 'advisory'.");
                    WorkUnit(item);
                    Only(item, item.StageId, item.WorkUnitId, item.Provider, item.ProviderRunId, item.AttemptOrdinal, item.CiClass, item.Result, item.StartedAt, item.CompletedAt, item.DurationMilliseconds); break;
                case WorkflowObservationKind.VerificationComparison:
                    Required(item.LocalResult, "localResult"); Required(item.HostedResult, "hostedResult");
                    Only(item, item.LocalResult, item.HostedResult); break;
                case WorkflowObservationKind.FinalPrRequiredChecks:
                    Required(item.PullRequestId, "pullRequestId"); Required(item.Provider, "provider"); Required(item.ProviderRunId, "providerRunId");
                    Ordinal(item.AttemptOrdinal); Required(item.Result, "result"); Timing(item);
                    if (item.CiClass != WorkflowCiClass.Final) throw new JsonException("finalPrRequiredChecks must have ciClass 'final'.");
                    Only(item, item.PullRequestId, item.Provider, item.ProviderRunId, item.AttemptOrdinal, item.Result, item.StartedAt, item.CompletedAt, item.DurationMilliseconds, item.CiClass); break;
                default: throw new JsonException("Unknown observation kind.");
            }
        }
    }

    private static void Timing(WorkflowQualityObservation item)
    {
        var start = Date(item.StartedAt, "startedAt");
        var end = Date(item.CompletedAt, "completedAt");
        if (end < start) throw new JsonException("completedAt cannot precede startedAt.");
        if (item.DurationMilliseconds is null or < 0) throw new JsonException("durationMilliseconds must be non-negative.");
        var elapsed = (end - start).TotalMilliseconds;
        if (elapsed != item.DurationMilliseconds.Value) throw new JsonException("durationMilliseconds must equal completedAt minus startedAt in milliseconds.");
    }

    private static DateTimeOffset Date(string? value, string field)
    {
        if (value is null || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)) throw new JsonException($"{field} must be an ISO date-time.");
        return date;
    }
    private static void WorkUnit(WorkflowQualityObservation item) { Required(item.StageId, "stageId"); Required(item.WorkUnitId, "workUnitId"); }
    private static void Ordinal(int? value) { if (value is null or < 1) throw new JsonException("attemptOrdinal must be at least 1."); }
    private static void Sha(string? value, string field) { if (value is null || value.Length != 40 || !value.All(Uri.IsHexDigit)) throw new JsonException($"{field} must be a full 40-character hexadecimal SHA."); }
    private static void Required<T>(T? value, string field) where T : struct { if (value is null) throw new JsonException($"{field} is required."); }
    private static void Required(string? value, string field) { if (string.IsNullOrWhiteSpace(value)) throw new JsonException($"{field} is required."); }
    private static void Only(WorkflowQualityObservation item, params object?[] allowed)
    {
        var allowedSet = new HashSet<object?>(allowed);
        var values = new object?[] { item.StageId, item.WorkUnitId, item.AttemptOrdinal, item.Result, item.RepairCommitSha, item.RepairObservationId, item.Provider, item.ProviderRunId, item.CiClass, item.StartedAt, item.CompletedAt, item.DurationMilliseconds, item.LocalResult, item.HostedResult, item.PullRequestId };
        if (values.Where(x => x is not null).Except(allowedSet).Any()) throw new JsonException($"Observation '{item.ObservationId}' contains fields that do not apply to kind '{item.Kind}'.");
    }

    private sealed class RepositoryShaComparer : IEqualityComparer<(string Repository, string Sha)>
    {
        public bool Equals((string Repository, string Sha) x, (string Repository, string Sha) y) =>
            StringComparer.Ordinal.Equals(x.Repository, y.Repository) && StringComparer.OrdinalIgnoreCase.Equals(x.Sha, y.Sha);
        public int GetHashCode((string Repository, string Sha) value) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.Repository), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Sha));
    }
}
