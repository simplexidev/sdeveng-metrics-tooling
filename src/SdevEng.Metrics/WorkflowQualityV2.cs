using System.Globalization;
using System.Text.Json;

namespace SdevEng.Metrics;

public sealed record WorkflowQualityV2Summary(
    int FirstPassLocalSuccessCount,
    int EligibleLocalScopeCount,
    int RepairCommitCount,
    int AdvisoryWorkflowRerunCount,
    int FinalWorkflowRerunCount,
    int LocalHostedComparisonCount,
    int LocalHostedDisagreementCount,
    IReadOnlyList<long> AdvisoryLatencyMilliseconds,
    IReadOnlyList<long> FinalPrLatencyMilliseconds);

/// <summary>Imports observed private evidence. Missing observations never count as success.</summary>
public static class WorkflowQualityV2
{
    private sealed record Hosted(string Repository, string Commit, string Run, int Attempt, string Tier, string Runner,
        string? PullRequest, DateTimeOffset? Started, DateTimeOffset? Completed, IReadOnlyDictionary<string, string> Checks);
    private sealed record Local(string Repository, string Commit, string Run, int Attempt, string Check, string Result);
    private sealed record Repair(string Repository, string Run, string Before, string After);

    public static WorkflowQualityV2Summary AggregateDirectory(string directory)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var hosted = new List<Hosted>();
        var local = new List<Local>();
        var repairs = new List<Repair>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var version)) continue;
            if (version.ValueKind == JsonValueKind.Number && version.GetInt32() == 2 && root.TryGetProperty("validationTier", out _))
                hosted.Add(ParseHosted(root, file));
            else if (version.ValueKind == JsonValueKind.String && version.GetString() == "2.0.0" && root.TryGetProperty("localValidations", out _))
                ParseLocal(root, file, local, repairs);
        }
        var uniqueHosted = hosted.GroupBy(x => (x.Repository, x.Commit, x.Run, x.Attempt, x.Tier, x.Runner))
            .Select(group =>
            {
                var first = group.First();
                if (group.Any(item => item.PullRequest != first.PullRequest || item.Started != first.Started || item.Completed != first.Completed ||
                    item.Checks.Count != first.Checks.Count || item.Checks.Any(check => !first.Checks.TryGetValue(check.Key, out var value) || value != check.Value)))
                    throw new InvalidDataException("Conflicting hosted evidence for one provider attempt.");
                return first;
            }).ToArray();
        var uniqueLocal = local.Distinct().ToArray();
        var uniqueRepairs = repairs.Distinct().ToArray();
        var localRuns = uniqueLocal.GroupBy(x => (x.Repository, x.Run), RunComparer.Instance).ToArray();
        var comparisons = (from item in uniqueLocal
                           from ci in uniqueHosted
                           where item.Repository == ci.Repository && item.Commit == ci.Commit && ci.Checks.TryGetValue(item.Check, out _)
                           select (item.Repository, item.Commit, item.Run, item.Check, ci.Run, ci.Attempt, ci.Runner, Local: item.Result, Hosted: ci.Checks[item.Check])).Distinct().ToArray();
        var advisory = uniqueHosted.Where(x => x.Tier == "advisory-commit").ToArray();
        var final = uniqueHosted.Where(x => x.Tier == "final-pr").ToArray();
        return new WorkflowQualityV2Summary(
            localRuns.Count(group => group.Where(x => x.Attempt == 1).Any() && group.Where(x => x.Attempt == 1).All(x => x.Result == "passed")),
            localRuns.Length,
            uniqueRepairs.Length,
            advisory.Where(x => x.Attempt > 1).Select(x => (x.Repository, x.Run, x.Attempt)).Distinct().Count(),
            final.Where(x => x.Attempt > 1).Select(x => (x.Repository, x.Run, x.Attempt)).Distinct().Count(),
            comparisons.Length,
            comparisons.Count(x => x.Local != x.Hosted),
            Latencies(advisory),
            Latencies(final));
    }

    private static long[] Latencies(IEnumerable<Hosted> observations) => observations
        .GroupBy(item => (item.Repository, item.Commit, item.Run, item.Attempt, item.Tier, item.PullRequest))
        .Where(group => group.All(item => item.Started is not null && item.Completed is not null))
        .Select(group => (long)(group.Max(item => item.Completed!.Value) - group.Min(item => item.Started!.Value)).TotalMilliseconds)
        .Order().ToArray();

    private static Hosted ParseHosted(JsonElement root, string file)
    {
        var repository = Required(root, "repository", file);
        var commit = Sha(Required(root, "commitSha", file), file);
        var run = Required(root, "providerRunId", file);
        var tier = Required(root, "validationTier", file);
        if (tier is not ("advisory-commit" or "final-pr")) throw new InvalidDataException($"Invalid validation tier in {file}.");
        var attempt = root.GetProperty("attemptOrdinal").GetInt32();
        if (attempt < 1) throw new InvalidDataException($"Invalid attempt ordinal in {file}.");
        var pullRequest = root.TryGetProperty("pullRequestId", out var pr) ? pr.GetString() : null;
        if (tier == "final-pr" && string.IsNullOrWhiteSpace(pullRequest)) throw new InvalidDataException($"Final PR evidence has no pull request identity in {file}.");
        DateTimeOffset? started = null;
        DateTimeOffset? completed = null;
        if (root.TryGetProperty("startedAt", out var start) && root.TryGetProperty("completedAt", out var end) && root.TryGetProperty("durationMilliseconds", out var elapsed))
        {
            started = DateTimeOffset.Parse(start.GetString()!, CultureInfo.InvariantCulture);
            completed = DateTimeOffset.Parse(end.GetString()!, CultureInfo.InvariantCulture);
            var duration = elapsed.GetInt64();
            if (duration < 0 || (long)(completed.Value - started.Value).TotalMilliseconds != duration)
                throw new InvalidDataException($"Hosted timing is inconsistent in {file}.");
        }
        else if (root.TryGetProperty("startedAt", out _) || root.TryGetProperty("completedAt", out _) || root.TryGetProperty("durationMilliseconds", out _))
            throw new InvalidDataException($"Hosted timing is incomplete in {file}.");
        var checks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var check in root.GetProperty("checks").EnumerateArray())
        {
            var name = Required(check, "check", file);
            var status = Required(check, "status", file);
            var version = check.GetProperty("schemaVersion").GetInt32();
            var exitCode = check.GetProperty("exitCode").GetInt32();
            if (version != 2 || Required(check, "source", file) != "hosted" ||
                Required(check, "environmentIdentity", file).Length > 512 ||
                status is not ("passed" or "failed" or "cancelled" or "timed-out") ||
                exitCode < 0 || (exitCode == 0) != (status == "passed") || !checks.TryAdd(name, status))
                throw new InvalidDataException($"Invalid or duplicate hosted check in {file}.");
        }
        return new Hosted(repository, commit, run, attempt, tier, Required(root, "runner", file), pullRequest, started, completed, checks);
    }

    private static void ParseLocal(JsonElement root, string file, List<Local> local, List<Repair> repairs)
    {
        foreach (var item in root.GetProperty("localValidations").EnumerateArray())
        {
            var result = Required(item, "result", file);
            if (result is not ("passed" or "failed" or "cancelled" or "timedOut")) throw new InvalidDataException($"Invalid local result in {file}.");
            var attempt = item.GetProperty("attemptOrdinal").GetInt32();
            if (attempt < 1) throw new InvalidDataException($"Invalid local attempt in {file}.");
            var commit = Sha(Required(item, "commitSha", file), file);
            var run = item.TryGetProperty("runId", out _) ? Required(item, "runId", file) : "commit:" + commit;
            local.Add(new Local(Required(item, "repository", file), commit,
                run, attempt, Required(item, "check", file), result == "timedOut" ? "timed-out" : result));
        }
        foreach (var item in root.GetProperty("repairCommits").EnumerateArray())
        {
            var before = Sha(Required(item, "beforeCommitSha", file), file);
            var after = Sha(Required(item, "repairCommitSha", file), file);
            if (before == after) throw new InvalidDataException($"Repair commit must differ from its prior commit in {file}.");
            var run = item.TryGetProperty("runId", out _) ? Required(item, "runId", file) : "commit:" + before;
            repairs.Add(new Repair(Required(item, "repository", file), run, before, after));
        }
    }

    private static string Required(JsonElement item, string name, string file)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Required {name} is missing in {file}.");
        return value.GetString()!;
    }

    private static string Sha(string value, string file)
    {
        if (value.Length is not (40 or 64) || !value.All(Uri.IsHexDigit)) throw new InvalidDataException($"Invalid commit SHA in {file}.");
        return value.ToLowerInvariant();
    }

    private sealed class RunComparer : IEqualityComparer<(string Repository, string Run)>
    {
        public static readonly RunComparer Instance = new();
        public bool Equals((string Repository, string Run) x, (string Repository, string Run) y) => x == y;
        public int GetHashCode((string Repository, string Run) value) => value.GetHashCode();
    }

}
