namespace MiniCI;

/// <summary>
/// Schedules jobs into execution levels: every job in a level only depends on
/// jobs from earlier levels, so a whole level can run in parallel. A dependency
/// cycle is a pipeline error, reported with the cycle itself.
/// </summary>
public static class JobDag
{
    /// <summary>Groups jobs into levels, earliest first. Throws on a cycle.</summary>
    public static IReadOnlyList<IReadOnlyList<PipelineJob>> Levels(IReadOnlyList<PipelineJob> jobs)
    {
        var byName = jobs.ToDictionary(j => j.Name, StringComparer.Ordinal);
        var remaining = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var job in jobs)
            remaining[job.Name] = job.Needs.ToHashSet(StringComparer.Ordinal);

        var levels = new List<IReadOnlyList<PipelineJob>>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(kv => kv.Value.All(n => done.Contains(n)))
                .Select(kv => kv.Key)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            if (ready.Count == 0)
                throw new PipelineException($"Dependency cycle detected: {DescribeCycle(byName, remaining)}.");
            levels.Add(ready.Select(n => byName[n]).ToList());
            foreach (var name in ready)
            {
                remaining.Remove(name);
                done.Add(name);
            }
        }
        return levels;
    }

    /// <summary>Finds one cycle among the jobs that are still unscheduled.</summary>
    private static string DescribeCycle(
        Dictionary<string, PipelineJob> byName,
        Dictionary<string, HashSet<string>> remaining)
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0 = unseen, 1 = on stack, 2 = done
        var stack = new List<string>();
        foreach (var start in remaining.Keys)
        {
            if (Visit(start)) return string.Join(" -> ", stack) + " -> " + stack[0];
        }
        return string.Join(", ", remaining.Keys);

        bool Visit(string name)
        {
            state[name] = 1;
            stack.Add(name);
            foreach (var need in byName[name].Needs)
            {
                if (!remaining.ContainsKey(need)) continue;
                if (state.TryGetValue(need, out var s) && s == 1)
                {
                    var i = stack.IndexOf(need);
                    stack.RemoveRange(0, i);
                    return true;
                }
                if (!state.ContainsKey(need) && Visit(need)) return true;
            }
            stack.RemoveAt(stack.Count - 1);
            state[name] = 2;
            return false;
        }
    }
}
