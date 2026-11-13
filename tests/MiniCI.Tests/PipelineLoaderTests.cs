using MiniCI;

namespace MiniCI.Tests;

public sealed class PipelineLoaderTests
{
    private const string Valid = """
        name: demo
        env:
          GLOBAL: "1"
        jobs:
          build:
            steps:
              - name: compile
                run: echo building
            artifacts:
              - "out/*.txt"
          test:
            needs: [build]
            steps:
              - name: check
                run: echo testing
                timeout: 60
        """;

    [Fact]
    public void LoadsValidPipeline()
    {
        var pipeline = PipelineLoader.Load(Valid, "fallback");
        Assert.Equal("demo", pipeline.Name);
        Assert.Equal("1", pipeline.Env!["GLOBAL"]);
        Assert.Equal(2, pipeline.Jobs.Count);
        Assert.Equal("build", pipeline.Jobs[0].Name);
        Assert.Equal("echo building", pipeline.Jobs[0].Steps[0].Run);
        Assert.Equal(["out/*.txt"], pipeline.Jobs[0].Artifacts);
        Assert.Equal(["build"], pipeline.Jobs[1].Needs);
        Assert.Equal(60, pipeline.Jobs[1].Steps[0].TimeoutSeconds);
    }

    [Fact]
    public void FallsBackToFileNameWhenNameMissing()
    {
        var pipeline = PipelineLoader.Load("jobs:\n  a:\n    steps:\n      - run: echo x\n", "myfile");
        Assert.Equal("myfile", pipeline.Name);
    }

    [Fact]
    public void RejectsUnknownNeed()
    {
        var yaml = Valid.Replace("needs: [build]", "needs: [nope]");
        var ex = Assert.Throws<PipelineException>(() => PipelineLoader.Load(yaml, "x"));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void RejectsSelfNeed()
    {
        const string yaml = """
            jobs:
              a:
                needs: [a]
                steps:
                  - run: echo x
            """;
        Assert.Throws<PipelineException>(() => PipelineLoader.Load(yaml, "x"));
    }

    [Fact]
    public void RejectsJobWithoutSteps()
    {
        const string yaml = "jobs:\n  a:\n    steps: []\n";
        Assert.Throws<PipelineException>(() => PipelineLoader.Load(yaml, "x"));
    }

    [Fact]
    public void RejectsStepWithoutRun()
    {
        const string yaml = """
            jobs:
              a:
                steps:
                  - name: nothing here
            """;
        Assert.Throws<PipelineException>(() => PipelineLoader.Load(yaml, "x"));
    }

    [Fact]
    public void RejectsBadTimeout()
    {
        const string yaml = """
            jobs:
              a:
                steps:
                  - run: echo x
                    timeout: -5
            """;
        Assert.Throws<PipelineException>(() => PipelineLoader.Load(yaml, "x"));
    }
}
