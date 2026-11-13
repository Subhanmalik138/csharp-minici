using MiniCI;

namespace MiniCI.Tests;

public sealed class PipelineRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "minici-run-" + Guid.NewGuid().ToString("N")[..8]);

    public PipelineRunnerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string PipelineYaml = """
        name: e2e
        jobs:
          build:
            steps:
              - name: make file
                run: echo built > out.txt
            artifacts:
              - "out.txt"
          test:
            needs: [build]
            steps:
              - name: check file
                run: test -f out.txt && echo ok
        """;

    [Fact]
    public async Task RunsDependentJobsInOrderAndCollectsArtifacts()
    {
        var pipeline = PipelineLoader.Load(PipelineYaml, "e2e");
        var store = new RunStore(_dir);
        var runner = new PipelineRunner(store);

        var run = await runner.RunAsync(pipeline, "minici.yml", _dir);

        Assert.Equal(RunStatus.Success, run.Status);
        Assert.Equal(2, run.Jobs.Count);
        Assert.All(run.Jobs, j => Assert.Equal(JobStatus.Success, j.Status));
        Assert.Contains("ok", run.Jobs[1].Steps[0].Output);
        Assert.Single(run.Jobs[0].ArtifactPaths);
        Assert.True(File.Exists(Path.Combine(_dir, ".minici", "artifacts", run.Id, "build", "out.txt")));
        Assert.NotNull(store.Load(run.Id));
        Assert.NotNull(store.LoadLog(run.Id));
    }

    [Fact]
    public async Task SkipsJobsWhoseDependencyFailed()
    {
        const string yaml = """
            name: fail-case
            jobs:
              build:
                steps:
                  - name: boom
                    run: exit 1
              test:
                needs: [build]
                steps:
                  - name: never runs
                    run: echo nope
            """;
        var pipeline = PipelineLoader.Load(yaml, "fail-case");
        var run = await new PipelineRunner(new RunStore(_dir))
            .RunAsync(pipeline, "minici.yml", _dir);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(JobStatus.Failed, run.Jobs[0].Status);
        Assert.Equal(JobStatus.Skipped, run.Jobs[1].Status);
        Assert.Empty(run.Jobs[1].Steps);
    }

    [Fact]
    public async Task ContinuesAfterContinueOnErrorStep()
    {
        const string yaml = """
            name: coe
            jobs:
              build:
                steps:
                  - name: flaky
                    run: exit 1
                    continue-on-error: true
                  - name: still runs
                    run: echo survived
            """;
        var pipeline = PipelineLoader.Load(yaml, "coe");
        var run = await new PipelineRunner(new RunStore(_dir))
            .RunAsync(pipeline, "minici.yml", _dir);

        Assert.Equal(RunStatus.Success, run.Status);
        Assert.Equal(2, run.Jobs[0].Steps.Count);
        Assert.Contains("survived", run.Jobs[0].Steps[1].Output);
    }
}

public sealed class RunStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "minici-store-" + Guid.NewGuid().ToString("N")[..8]);

    public RunStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void RoundTripsRunAndListsNewestFirst()
    {
        var store = new RunStore(_dir);
        var older = new PipelineRun("20260101-000000-aaaaaa", "p", "minici.yml",
            RunStatus.Success, [], DateTimeOffset.UtcNow.AddHours(-1), TimeSpan.FromSeconds(1));
        var newer = new PipelineRun("20260102-000000-bbbbbb", "p", "minici.yml",
            RunStatus.Failed, [], DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        store.Save(older, "old log");
        store.Save(newer, "new log");

        Assert.Equal(RunStatus.Failed, store.Load(newer.Id)!.Status);
        Assert.Equal("old log", store.LoadLog(older.Id));
        Assert.Equal([newer.Id, older.Id], store.List().Select(r => r.Id));
        Assert.Null(store.Load("missing"));
    }
}
