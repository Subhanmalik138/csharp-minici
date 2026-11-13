using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniCI;

/// <summary>Runs one shell command, capturing its output and enforcing a timeout.</summary>
public static class StepRunner
{
    public static async Task<StepResult> RunAsync(
        PipelineStep step,
        string baseDirectory,
        IReadOnlyDictionary<string, string>? inheritedEnv,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var workingDirectory = step.WorkingDirectory is null
            ? baseDirectory
            : Path.GetFullPath(Path.Combine(baseDirectory, step.WorkingDirectory));

        var (fileName, arguments) = ShellFor(step.Run);
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (inheritedEnv is not null)
            foreach (var (key, value) in inheritedEnv)
                startInfo.Environment[key] = value;
        if (step.Env is not null)
            foreach (var (key, value) in step.Env)
                startInfo.Environment[key] = value;

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new StepResult(step.Name, step.Run, -1, string.Empty,
                $"Failed to start shell: {ex.Message}", stopwatch.Elapsed, StepOutcome.Failed);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(step.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await Task.WhenAll(outputTask, errorTask);
            return new StepResult(step.Name, step.Run, -1,
                stdout.Append(await outputTask).ToString(),
                stderr.Append(await errorTask).ToString(),
                stopwatch.Elapsed, StepOutcome.TimedOut);
        }

        var output = await outputTask;
        var error = await errorTask;
        var outcome = process.ExitCode == 0 ? StepOutcome.Success : StepOutcome.Failed;
        return new StepResult(step.Name, step.Run, process.ExitCode,
            output, error, stopwatch.Elapsed, outcome);
    }

    private static (string FileName, string Arguments) ShellFor(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ("cmd.exe", "/c " + Quote(command));
        return ("/bin/bash", "-c " + Quote(command));
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
