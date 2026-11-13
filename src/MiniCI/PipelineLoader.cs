namespace MiniCI;

/// <summary>Turns a parsed YAML document into a validated <see cref="Pipeline"/>.</summary>
public static class PipelineLoader
{
    public static Pipeline Load(string text, string fallbackName)
    {
        var root = YamlParser.Parse(text);
        if (root is not YamlMap map)
            throw new PipelineException("Pipeline file must be a mapping at the top level.");

        var name = map.GetScalar("name") is { Length: > 0 } n ? n : fallbackName;
        var env = ParseEnv(map.Get("env"), "env");

        var jobsNode = map.Get("jobs")
            ?? throw new PipelineException("Pipeline must define a 'jobs' mapping.");
        if (jobsNode is not YamlMap jobsMap || jobsMap.Entries.Count == 0)
            throw new PipelineException("Pipeline must define at least one job under 'jobs'.");

        var jobs = new List<PipelineJob>();
        foreach (var (jobName, jobNode) in jobsMap.Entries)
        {
            if (jobNode is not YamlMap jobMap)
                throw new PipelineException($"Job '{jobName}' must be a mapping.");
            jobs.Add(ParseJob(jobName, jobMap));
        }

        // Every `needs` entry must name a real job, and no job may need itself.
        var names = jobs.Select(j => j.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var job in jobs)
        {
            foreach (var need in job.Needs)
            {
                if (need == job.Name)
                    throw new PipelineException($"Job '{job.Name}' cannot need itself.");
                if (!names.Contains(need))
                    throw new PipelineException($"Job '{job.Name}' needs unknown job '{need}'.");
            }
        }

        return new Pipeline(name, jobs, env);
    }

    private static PipelineJob ParseJob(string name, YamlMap jobMap)
    {
        var stepsNode = jobMap.Get("steps")
            ?? throw new PipelineException($"Job '{name}' must define 'steps'.");
        if (stepsNode is not YamlList stepsList || stepsList.Items.Count == 0)
            throw new PipelineException($"Job '{name}' must define at least one step.");

        var steps = new List<PipelineStep>();
        foreach (var item in stepsList.Items)
        {
            if (item is not YamlMap stepMap)
                throw new PipelineException($"Job '{name}': each step must be a mapping.");
            steps.Add(ParseStep(name, stepMap));
        }

        var needs = ParseStringList(jobMap.Get("needs"), $"job '{name}': 'needs'");
        var artifacts = ParseStringList(jobMap.Get("artifacts"), $"job '{name}': 'artifacts'");
        var env = ParseEnv(jobMap.Get("env"), $"job '{name}': 'env'");
        return new PipelineJob(name, steps, needs, artifacts, env);
    }

    private static PipelineStep ParseStep(string jobName, YamlMap stepMap)
    {
        var stepName = stepMap.GetScalar("name") ?? "step";
        var run = stepMap.GetScalar("run");
        if (string.IsNullOrWhiteSpace(run))
            throw new PipelineException($"Job '{jobName}', step '{stepName}': 'run' must be a non-empty command.");

        var timeout = 300;
        if (stepMap.GetScalar("timeout") is { } timeoutText)
        {
            if (!int.TryParse(timeoutText, out timeout) || timeout <= 0)
                throw new PipelineException($"Job '{jobName}', step '{stepName}': 'timeout' must be a positive number of seconds.");
        }

        var continueOnError = stepMap.GetScalar("continue-on-error") is "true";
        var env = ParseEnv(stepMap.Get("env"), $"job '{jobName}', step '{stepName}': 'env'");
        return new PipelineStep(
            stepName, run,
            WorkingDirectory: stepMap.GetScalar("working-directory"),
            TimeoutSeconds: timeout,
            ContinueOnError: continueOnError,
            Env: env);
    }

    private static IReadOnlyList<string> ParseStringList(YamlNode? node, string where)
    {
        if (node is null) return [];
        if (node is YamlScalar s)
            return string.IsNullOrWhiteSpace(s.Value) ? [] : [s.Value];
        if (node is YamlList list)
        {
            var values = new List<string>();
            foreach (var item in list.Items)
            {
                if (item is not YamlScalar scalar || string.IsNullOrWhiteSpace(scalar.Value))
                    throw new PipelineException($"{where} must be a list of strings.");
                values.Add(scalar.Value);
            }
            return values;
        }
        throw new PipelineException($"{where} must be a string or a list of strings.");
    }

    private static IReadOnlyDictionary<string, string>? ParseEnv(YamlNode? node, string where)
    {
        if (node is null) return null;
        if (node is not YamlMap map)
            throw new PipelineException($"{where} must be a mapping of name: value.");
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in map.Entries)
        {
            if (value is not YamlScalar scalar)
                throw new PipelineException($"{where}: value for '{key}' must be a string.");
            env[key] = scalar.Value;
        }
        return env;
    }
}
