using System.Diagnostics;
using System.Text;

namespace MiniCI;

/// <summary>
/// Runs a pipeline: jobs level by level (see <see cref="JobDag"/>), jobs inside
/// a level in parallel, steps inside a job in order. A job whose dependency
/// failed is skipped, not run. Every run is recorded in the <see cref="RunStore"/>.
/// </summary>
public sealed class PipelineRunner
{
    private readonly RunStore _store;
    private readonly int _maxParallelJobs;

    public PipelineRunner(RunStore store, int maxParallelJobs = 0)
    {
        _store = store;
        _maxParallelJobs = maxParallelJobs > 0 ? maxParallelJobs : Environment.ProcessorCount;
    }

    public async Task<PipelineRun> RunAsync(
        Pipeline pipeline,
        string pipelinePath,
        string workingDirectory,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var runId = _store.NewRunId();
        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var logText = new StringBuilder();
        void Write(string line)
        {
            logText.AppendLine(line);
            log?.Report(line);
        }

        Write($"MiniCI run {runId} — pipeline '{pipeline.Name}'");
        Write($"Started {started:yyyy-MM-dd HH:mm:ss} UTC, max { _maxParallelJobs} parallel jobs");
        Write(string.Empty);

        var levels = JobDag.Levels(pipeline.Jobs);
        var results = new Dictionary<string, JobResult>(StringComparer.Ordinal);

        foreach (var level in levels)
        {
            using var gate = new SemaphoreSlim(_maxParallelJobs);
            var tasks = level.Select(async job =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    return await RunJobAsync(pipeline, job, runId, workingDirectory, results, Write, cancellationToken);
                }
                finally
                {
                    gate.Release();
                }
            });
            var levelResults = await Task.WhenAll(tasks);
            foreach (var result in levelResults)
                results[result.Name] = result;
        }

        var ordered = pipeline.Jobs.Select(j => results[j.Name]).ToList();
        var status = ordered.All(r => r.Status == JobStatus.Success)
            ? RunStatus.Success
            : RunStatus.Failed;
        var run = new PipelineRun(runId, pipeline.Name, pipelinePath, status,
            ordered, started, stopwatch.Elapsed);
        _store.Save(run, logText.ToString());

        Write(string.Empty);
        Write($"Run {runId}: {status} in {stopwatch.Elapsed.TotalSeconds:F1}s " +
              $"({ordered.Count(r => r.Status == JobStatus.Success)} succeeded, " +
              $"{ordered.Count(r => r.Status == JobStatus.Failed)} failed, " +
              $"{ordered.Count(r => r.Status == JobStatus.Skipped)} skipped)");
        return run;
    }

    private async Task<JobResult> RunJobAsync(
        Pipeline pipeline,
        PipelineJob job,
        string runId,
        string workingDirectory,
        IReadOnlyDictionary<string, JobResult> finished,
        Action<string> write,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var failedNeeds = job.Needs
            .Where(need => finished.TryGetValue(need, out var r) && r.Status != JobStatus.Success)
            .ToList();
        if (failedNeeds.Count > 0)
        {
            write($"[skip] {job.Name} — dependency failed: {string.Join(", ", failedNeeds)}");
            return new JobResult(job.Name, JobStatus.Skipped, [], [], TimeSpan.Zero, started);
        }

        write($"[job] {job.Name}");
        var env = MergeEnv(pipeline.Env, job.Env);
        var stepResults = new List<StepResult>();
        var failed = false;
        foreach (var step in job.Steps)
        {
            write($"  [step] {step.Name}: {step.Run}");
            var result = await StepRunner.RunAsync(step, workingDirectory, env, cancellationToken);
            stepResults.Add(result);
            WriteStepOutput(result, write);
            if (result.Outcome != StepOutcome.Success && !step.ContinueOnError)
            {
                failed = true;
                write($"  [fail] {step.Name} — " +
                    (result.Outcome == StepOutcome.TimedOut
                        ? $"timed out after {step.TimeoutSeconds}s"
                        : $"exit code {result.ExitCode}"));
                break;
            }
        }

        var artifacts = _store.CollectArtifacts(runId, job, workingDirectory);
        foreach (var artifact in artifacts)
            write($"  [artifact] {artifact}");

        var status = failed ? JobStatus.Failed : JobStatus.Success;
        write($"[{(status == JobStatus.Success ? "pass" : "fail")}] {job.Name} " +
              $"in {stopwatch.Elapsed.TotalSeconds:F1}s");
        return new JobResult(job.Name, status, stepResults, artifacts, stopwatch.Elapsed, started);
    }

    private static void WriteStepOutput(StepResult result, Action<string> write)
    {
        foreach (var line in SplitLines(result.Output))
            write("    " + line);
        foreach (var line in SplitLines(result.Error))
            write("    ! " + line);
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);

    private static IReadOnlyDictionary<string, string>? MergeEnv(
        IReadOnlyDictionary<string, string>? pipelineEnv,
        IReadOnlyDictionary<string, string>? jobEnv)
    {
        if (pipelineEnv is null && jobEnv is null) return null;
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (pipelineEnv is not null)
            foreach (var kv in pipelineEnv) merged[kv.Key] = kv.Value;
        if (jobEnv is not null)
            foreach (var kv in jobEnv) merged[kv.Key] = kv.Value;
        return merged;
    }
}
