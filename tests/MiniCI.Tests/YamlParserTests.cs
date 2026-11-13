using MiniCI;

namespace MiniCI.Tests;

public sealed class YamlParserTests
{
    [Fact]
    public void ParsesFlatMap()
    {
        var root = (YamlMap)YamlParser.Parse("name: demo\nversion: 3\n");
        Assert.Equal("demo", root.GetScalar("name"));
        Assert.Equal("3", root.GetScalar("version"));
    }

    [Fact]
    public void ParsesNestedMap()
    {
        var root = (YamlMap)YamlParser.Parse("jobs:\n  build:\n    steps:\n      - run: echo hi\n");
        var jobs = (YamlMap)root.Get("jobs")!;
        var build = (YamlMap)jobs.Get("build")!;
        var steps = (YamlList)build.Get("steps")!;
        var step = (YamlMap)steps.Items[0];
        Assert.Equal("echo hi", step.GetScalar("run"));
    }

    [Fact]
    public void ParsesListOfMapsWithInlineFirstEntry()
    {
        var root = (YamlMap)YamlParser.Parse("""
            steps:
              - name: build
                run: make
              - name: test
                run: make test
            """);
        var steps = (YamlList)root.Get("steps")!;
        Assert.Equal(2, steps.Items.Count);
        Assert.Equal("make", ((YamlMap)steps.Items[0]).GetScalar("run"));
        Assert.Equal("test", ((YamlMap)steps.Items[1]).GetScalar("name"));
    }

    [Fact]
    public void HandlesQuotedScalarsAndComments()
    {
        var root = (YamlMap)YamlParser.Parse("""
            # a comment
            name: "hello: world" # trailing comment
            single: 'it''s fine'
            """);
        Assert.Equal("hello: world", root.GetScalar("name"));
        Assert.Equal("it's fine", root.GetScalar("single"));
    }

    [Fact]
    public void ParsesFlowList()
    {
        var root = (YamlMap)YamlParser.Parse("needs: [build, test]\n");
        var needs = (YamlList)root.Get("needs")!;
        Assert.Equal(["build", "test"],
            needs.Items.Cast<YamlScalar>().Select(s => s.Value));
    }

    [Fact]
    public void RejectsTabs()
    {
        Assert.Throws<PipelineException>(() => YamlParser.Parse("name:\n\tfoo: bar\n"));
    }

    [Fact]
    public void RejectsDuplicateKeys()
    {
        Assert.Throws<PipelineException>(() => YamlParser.Parse("name: a\nname: b\n"));
    }

    [Fact]
    public void RejectsEmptyFile()
    {
        Assert.Throws<PipelineException>(() => YamlParser.Parse("  \n# only a comment\n"));
    }
}
