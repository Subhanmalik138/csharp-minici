using System.Text.Json;

namespace MiniCI;

/// <summary>
/// Persists run records and logs under <c>.minici/</c> in the working directory,
/// and collects job artifacts into <c>.minici/artifacts/&lt;run-id&gt;/&lt;job&gt;/</c>.
/// </summary>
public sealed class RunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Root { get; }

    public RunStore(string workingDirectory)
    {
        Root = Path.Combine(workingDirectory, ".minici");
    }

    public string RunsDirectory => Path.Combine(Root, "runs");
    public string ArtifactsDirectory => Path.Combine(Root, "artifacts");

    public string NewRunId() =>
        DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];

    public void Save(PipelineRun run, string logText)
    {
        Directory.CreateDirectory(RunsDirectory);
        var json = JsonSerializer.Serialize(run, JsonOptions);
        File.WriteAllText(Path.Combine(RunsDirectory, run.Id + ".json"), json);
        File.WriteAllText(Path.Combine(RunsDirectory, run.Id + ".log"), logText);
    }

    public PipelineRun? Load(string runId)
    {
        var path = Path.Combine(RunsDirectory, runId + ".json");
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<PipelineRun>(File.ReadAllText(path), JsonOptions);
    }

    public string? LoadLog(string runId)
    {
        var path = Path.Combine(RunsDirectory, runId + ".log");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public IReadOnlyList<PipelineRun> List()
    {
        if (!Directory.Exists(RunsDirectory)) return [];
        return Directory.EnumerateFiles(RunsDirectory, "*.json")
            .Select(path => JsonSerializer.Deserialize<PipelineRun>(File.ReadAllText(path), JsonOptions))
            .Where(run => run is not null)
            .Cast<PipelineRun>()
            .OrderByDescending(run => run.StartedAt)
            .ToList();
    }

    /// <summary>Copies every file matching the job's artifact patterns into the run's artifact dir.</summary>
    public IReadOnlyList<string> CollectArtifacts(
        string runId, PipelineJob job, string baseDirectory)
    {
        var collected = new List<string>();
        if (job.Artifacts.Count == 0) return collected;
        foreach (var pattern in job.Artifacts)
        {
            foreach (var relative in Glob.Match(baseDirectory, pattern))
            {
                var source = Path.Combine(baseDirectory, relative);
                var destination = Path.Combine(ArtifactsDirectory, runId, job.Name, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                collected.Add(Path.Combine(job.Name, relative));
            }
        }
        return collected;
    }
}
