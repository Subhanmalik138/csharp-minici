using MiniCI;

namespace MiniCI.Tests;

public sealed class StepRunnerTests
{
    [Fact]
    public async Task CapturesStdoutAndExitCode()
    {
        var result = await StepRunner.RunAsync(
            new PipelineStep("hello", "echo hello-minici"),
            Directory.GetCurrentDirectory(), null);
        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello-minici", result.Output);
    }

    [Fact]
    public async Task ReportsNonZeroExitCode()
    {
        var result = await StepRunner.RunAsync(
            new PipelineStep("fail", "exit 3"),
            Directory.GetCurrentDirectory(), null);
        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task PassesEnvironmentVariables()
    {
        var result = await StepRunner.RunAsync(
            new PipelineStep("env", "echo $MINICI_TEST_VALUE"),
            Directory.GetCurrentDirectory(),
            new Dictionary<string, string> { ["MINICI_TEST_VALUE"] = "forty-two" });
        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Contains("forty-two", result.Output);
    }

    [Fact]
    public async Task TimesOutLongRunningStep()
    {
        var result = await StepRunner.RunAsync(
            new PipelineStep("slow", "sleep 30", TimeoutSeconds: 1),
            Directory.GetCurrentDirectory(), null);
        Assert.Equal(StepOutcome.TimedOut, result.Outcome);
        Assert.True(result.Duration < TimeSpan.FromSeconds(10));
    }
}

public sealed class GlobTests
{
    [Fact]
    public void MatchesStarAndDoubleStar()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub", "deep"));
        File.WriteAllText(Path.Combine(dir.Path, "sub", "b.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "sub", "deep", "c.log"), "x");

        Assert.Equal(["a.txt"], Glob.Match(dir.Path, "*.txt"));
        Assert.Equal(["sub/b.txt"], Glob.Match(dir.Path, "sub/*.txt"));
        Assert.Equal(["sub/deep/c.log"], Glob.Match(dir.Path, "**/*.log"));
        Assert.Equal(["a.txt", "sub/b.txt"], Glob.Match(dir.Path, "**/*.txt").OrderBy(p => p).ToList());
        Assert.Empty(Glob.Match(dir.Path, "*.md"));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "minici-test-" + Guid.NewGuid().ToString("N")[..8]);
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
