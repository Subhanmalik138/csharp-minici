namespace MiniCI;

/// <summary>Matches file paths against glob patterns (<c>*</c>, <c>?</c>, <c>**</c>).</summary>
public static class Glob
{
    /// <summary>Returns relative paths under <paramref name="baseDirectory"/> matching the pattern.</summary>
    public static IReadOnlyList<string> Match(string baseDirectory, string pattern)
    {
        var patternParts = Split(pattern);
        var results = new List<string>();
        if (!Directory.Exists(baseDirectory)) return results;
        foreach (var file in Directory.EnumerateFiles(baseDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(baseDirectory, file);
            if (Matches(Split(relative), patternParts))
                results.Add(relative);
        }
        return results;
    }

    private static string[] Split(string path) =>
        path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static bool Matches(string[] path, string[] pattern)
    {
        return MatchParts(path, 0, pattern, 0);

        static bool MatchParts(string[] path, int pi, string[] pattern, int qi)
        {
            while (qi < pattern.Length)
            {
                if (pattern[qi] == "**")
                {
                    // `**` swallows zero or more whole segments.
                    for (var skip = 0; pi + skip <= path.Length; skip++)
                        if (MatchParts(path, pi + skip, pattern, qi + 1))
                            return true;
                    return false;
                }
                if (pi >= path.Length) return false;
                if (!MatchSegment(path[pi], pattern[qi])) return false;
                pi++;
                qi++;
            }
            return pi == path.Length;
        }
    }

    private static bool MatchSegment(string name, string pattern)
    {
        var ni = 0;
        var pi = 0;
        var star = -1;
        var retry = -1;
        while (ni < name.Length)
        {
            if (pi < pattern.Length && (pattern[pi] == '?' || pattern[pi] == name[ni]))
            {
                ni++;
                pi++;
            }
            else if (pi < pattern.Length && pattern[pi] == '*')
            {
                star = pi++;
                retry = ni;
            }
            else if (star >= 0)
            {
                pi = star + 1;
                ni = ++retry;
            }
            else
            {
                return false;
            }
        }
        while (pi < pattern.Length && pattern[pi] == '*') pi++;
        return pi == pattern.Length;
    }
}
