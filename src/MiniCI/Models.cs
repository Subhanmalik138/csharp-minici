namespace MiniCI;

/// <summary>A single shell command inside a job.</summary>
public sealed record PipelineStep(
    string Name,
    string Run,
    string? WorkingDirectory = null,
    int TimeoutSeconds = 300,
    bool ContinueOnError = false,
    IReadOnlyDictionary<string, string>? Env = null);

/// <summary>A job: an ordered list of steps plus its dependencies and artifacts.</summary>
public sealed record PipelineJob(
    string Name,
    IReadOnlyList<PipelineStep> Steps,
    IReadOnlyList<string> Needs,
    IReadOnlyList<string> Artifacts,
    IReadOnlyDictionary<string, string>? Env = null);

/// <summary>A whole pipeline: a named set of jobs with optional shared env.</summary>
public sealed record Pipeline(
    string Name,
    IReadOnlyList<PipelineJob> Jobs,
    IReadOnlyDictionary<string, string>? Env = null);

public enum StepOutcome { Success, Failed, TimedOut, Skipped }

public sealed record StepResult(
    string Name,
    string Command,
    int ExitCode,
    string Output,
    string Error,
    TimeSpan Duration,
    StepOutcome Outcome);

public enum JobStatus { Success, Failed, Skipped }

public sealed record JobResult(
    string Name,
    JobStatus Status,
    IReadOnlyList<StepResult> Steps,
    IReadOnlyList<string> ArtifactPaths,
    TimeSpan Duration,
    DateTimeOffset StartedAt);

public enum RunStatus { Success, Failed }

public sealed record PipelineRun(
    string Id,
    string PipelineName,
    string PipelinePath,
    RunStatus Status,
    IReadOnlyList<JobResult> Jobs,
    DateTimeOffset StartedAt,
    TimeSpan Duration);
