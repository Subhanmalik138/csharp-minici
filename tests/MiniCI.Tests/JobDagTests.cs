using MiniCI;

namespace MiniCI.Tests;

public sealed class JobDagTests
{
    private static PipelineJob Job(string name, params string[] needs) =>
        new(name, [new PipelineStep("s", "echo x")], needs, []);

    [Fact]
    public void GroupsIndependentJobsIntoOneLevel()
    {
        var levels = JobDag.Levels([Job("a"), Job("b"), Job("c")]);
        Assert.Single(levels);
        Assert.Equal(3, levels[0].Count);
    }

    [Fact]
    public void OrdersChainIntoSeparateLevels()
    {
        var levels = JobDag.Levels([Job("c", "b"), Job("a"), Job("b", "a")]);
        Assert.Equal(3, levels.Count);
        Assert.Equal("a", levels[0][0].Name);
        Assert.Equal("b", levels[1][0].Name);
        Assert.Equal("c", levels[2][0].Name);
    }

    [Fact]
    public void KeepsDiamondBranchesInSameLevel()
    {
        var levels = JobDag.Levels([
            Job("top", "left", "right"), Job("left", "base"),
            Job("right", "base"), Job("base")]);
        Assert.Equal(3, levels.Count);
        Assert.Equal(["left", "right"], levels[1].Select(j => j.Name).OrderBy(n => n));
        Assert.Equal("top", levels[2][0].Name);
    }

    [Fact]
    public void DetectsCycleAndNamesIt()
    {
        var ex = Assert.Throws<PipelineException>(() =>
            JobDag.Levels([Job("a", "b"), Job("b", "a")]));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a", ex.Message);
    }
}
