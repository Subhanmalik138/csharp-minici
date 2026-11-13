namespace MiniCI;

/// <summary>Command-line interface: init, run, runs, status, logs.</summary>
public static class Cli
{
    public const string SamplePipeline = """
        # MiniCI pipeline — jobs run level by level; jobs in one level run in parallel.
        name: sample

        env:
          DOTNET_CLI_TELEMETRY_OPTOUT: "1"

        jobs:
          build:
            steps:
              - name: say hello
                run: echo "building..."
              - name: list files
                run: ls
            artifacts:
              - "*.log"

          test:
            needs: [build]
            steps:
              - name: run checks
                run: echo "all green"
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "init" => Init(),
                "run" => await RunPipelineAsync(args[1..]),
                "runs" => ListRuns(),
                "status" => Status(args.ElementAtOrDefault(1)),
                "logs" => Logs(args.ElementAtOrDefault(1), args.Contains("--job")
                    ? args.SkipWhile(a => a != "--job").ElementAtOrDefault(1) : null),
                _ => Unknown(args[0]),
            };
        }
        catch (PipelineException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"Error: file not found: {ex.FileName}");
            return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            MiniCI — a tiny CI runner. Usage:
              minici init                    write a sample minici.yml in this directory
              minici run [pipeline.yml]      run a pipeline (default: minici.yml)
              minici runs                    list past runs
              minici status <run-id>         show a run summary
              minici logs <run-id> [--job n] print a run's log (optionally one job's output)
            """);
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Run `minici --help`.");
        return 2;
    }

    private static int Init()
    {
        const string file = "minici.yml";
        if (File.Exists(file))
        {
            Console.Error.WriteLine($"{file} already exists — refusing to overwrite it.");
            return 2;
        }
        File.WriteAllText(file, SamplePipeline);
        Console.WriteLine($"Wrote {file}. Run it with: minici run");
        return 0;
    }

    private static async Task<int> RunPipelineAsync(string[] args)
    {
        var path = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "minici.yml";
        var jobsIndex = Array.IndexOf(args, "--jobs");
        var maxJobs = jobsIndex >= 0 && jobsIndex + 1 < args.Length
            && int.TryParse(args[jobsIndex + 1], out var j) ? j : 0;

        var pipeline = PipelineLoader.Load(
            File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
        var directory = Path.GetDirectoryName(path);
        var workingDirectory = Path.GetFullPath(
            string.IsNullOrEmpty(directory) ? "." : directory);
        var store = new RunStore(workingDirectory);
        var runner = new PipelineRunner(store, maxJobs);
        var run = await runner.RunAsync(pipeline, path, workingDirectory,
            new Progress<string>(Console.WriteLine));
        Console.WriteLine();
        Console.WriteLine($"Run id: {run.Id}  (minici status {run.Id} for details)");
        return run.Status == RunStatus.Success ? 0 : 1;
    }

    private static int ListRuns()
    {
        var store = new RunStore(Directory.GetCurrentDirectory());
        var runs = store.List();
        if (runs.Count == 0)
        {
            Console.WriteLine("No runs yet. Run `minici run` first.");
            return 0;
        }
        Console.WriteLine($"{"Run ID",-26} {"Started",-20} {"Status",-8} Jobs");
        foreach (var run in runs)
        {
            Console.WriteLine($"{run.Id,-26} {run.StartedAt:yyyy-MM-dd HH:mm:ss} " +
                $"{run.Status,-8} {run.Jobs.Count(r => r.Status == JobStatus.Success)}/{run.Jobs.Count} passed");
        }
        return 0;
    }

    private static int Status(string? runId)
    {
        if (runId is null)
        {
            Console.Error.WriteLine("Usage: minici status <run-id>");
            return 2;
        }
        var store = new RunStore(Directory.GetCurrentDirectory());
        var run = store.Load(runId);
        if (run is null)
        {
            Console.Error.WriteLine($"No run '{runId}'.");
            return 2;
        }
        Console.WriteLine($"Run {run.Id} — {run.PipelineName} ({run.PipelinePath})");
        Console.WriteLine($"Started {run.StartedAt:yyyy-MM-dd HH:mm:ss} UTC, took {run.Duration.TotalSeconds:F1}s — {run.Status}");
        foreach (var job in run.Jobs)
        {
            Console.WriteLine($"  {job.Status,-8} {job.Name} ({job.Duration.TotalSeconds:F1}s)" +
                (job.ArtifactPaths.Count > 0 ? $" — {job.ArtifactPaths.Count} artifact(s)" : ""));
            foreach (var step in job.Steps)
                Console.WriteLine($"    {step.Outcome,-9} {step.Name} (exit {step.ExitCode}, {step.Duration.TotalSeconds:F1}s)");
        }
        return 0;
    }

    private static int Logs(string? runId, string? jobName)
    {
        if (runId is null)
        {
            Console.Error.WriteLine("Usage: minici logs <run-id> [--job <name>]");
            return 2;
        }
        var store = new RunStore(Directory.GetCurrentDirectory());
        var log = store.LoadLog(runId);
        if (log is null)
        {
            Console.Error.WriteLine($"No log for run '{runId}'.");
            return 2;
        }
        if (jobName is null)
        {
            Console.Write(log);
            return 0;
        }
        // Print only the section for one job: from its [job] line to the next [job]/[pass]/[fail] line.
        var lines = log.Split('\n');
        var printing = false;
        var printed = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("[job] ", StringComparison.Ordinal))
            {
                if (printing) break;
                printing = line == $"[job] {jobName}";
            }
            if (printing)
            {
                Console.WriteLine(line);
                printed = true;
            }
        }
        if (!printed)
        {
            Console.Error.WriteLine($"Run '{runId}' has no job '{jobName}'.");
            return 2;
        }
        return 0;
    }
}
