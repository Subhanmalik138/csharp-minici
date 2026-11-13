namespace MiniCI;

/// <summary>Thrown when a pipeline file is not valid YAML (subset) or not a valid pipeline.</summary>
public sealed class PipelineException : Exception
{
    public PipelineException(string message) : base(message) { }
}

/// <summary>Minimal YAML node model: enough for pipeline files, nothing more.</summary>
public abstract record YamlNode;

public sealed record YamlScalar(string Value) : YamlNode;

public sealed record YamlMap(IReadOnlyList<KeyValuePair<string, YamlNode>> Entries) : YamlNode
{
    public YamlNode? Get(string key) =>
        Entries.FirstOrDefault(e => e.Key == key).Value;

    public string? GetScalar(string key) =>
        Get(key) is YamlScalar s ? s.Value : null;
}

public sealed record YamlList(IReadOnlyList<YamlNode> Items) : YamlNode;

/// <summary>
/// A small, dependency-free parser for the block-style YAML subset MiniCI
/// pipelines use: nested maps, block sequences, quoted scalars, flow lists
/// (<c>[a, b]</c>), and <c>#</c> comments. Tabs are rejected; indentation
/// must be spaces. Anything outside the subset is a hard error, not a guess.
/// </summary>
public static class YamlParser
{
    private sealed record Line(int Indent, string Content, int Number);

    public static YamlNode Parse(string text)
    {
        var lines = new List<Line>();
        var raw = text.Split('\n');
        for (var i = 0; i < raw.Length; i++)
        {
            var line = raw[i].TrimEnd('\r');
            if (line.Contains('\t'))
                throw new PipelineException($"Line {i + 1}: tabs are not allowed, use spaces.");
            var stripped = StripComment(line);
            if (string.IsNullOrWhiteSpace(stripped)) continue;
            var indent = 0;
            while (indent < stripped.Length && stripped[indent] == ' ') indent++;
            lines.Add(new Line(indent, stripped[indent..], i + 1));
        }

        if (lines.Count == 0)
            throw new PipelineException("Empty pipeline file.");

        var pos = 0;
        var node = ParseBlock(lines, ref pos, lines[0].Indent);
        if (pos < lines.Count)
            throw new PipelineException($"Line {lines[pos].Number}: unexpected content.");
        return node;
    }

    private static YamlNode ParseBlock(List<Line> lines, ref int pos, int indent)
    {
        if (pos >= lines.Count)
            throw new PipelineException("Unexpected end of file.");
        if (lines[pos].Indent < indent)
            throw new PipelineException($"Line {lines[pos].Number}: bad indentation.");
        return lines[pos].Content.StartsWith("- ", StringComparison.Ordinal)
            || lines[pos].Content == "-"
            ? ParseList(lines, ref pos, indent)
            : ParseMap(lines, ref pos, indent);
    }

    private static YamlMap ParseMap(List<Line> lines, ref int pos, int indent)
    {
        var entries = new List<KeyValuePair<string, YamlNode>>();
        while (pos < lines.Count && lines[pos].Indent == indent
               && !IsListItem(lines[pos].Content))
        {
            var line = lines[pos];
            var colon = FindKeyColon(line.Content);
            if (colon < 0)
                throw new PipelineException($"Line {line.Number}: expected 'key: value'.");
            var key = Unquote(line.Content[..colon].Trim());
            if (key.Length == 0)
                throw new PipelineException($"Line {line.Number}: empty key.");
            if (entries.Any(e => e.Key == key))
                throw new PipelineException($"Line {line.Number}: duplicate key '{key}'.");
            var rest = line.Content[(colon + 1)..].Trim();
            pos++;
            YamlNode value;
            if (rest.Length > 0)
            {
                value = ParseInline(rest, line.Number);
            }
            else if (pos < lines.Count && lines[pos].Indent > indent)
            {
                value = ParseBlock(lines, ref pos, lines[pos].Indent);
            }
            else
            {
                value = new YamlScalar(string.Empty); // `key:` with no value = empty scalar
            }
            entries.Add(new KeyValuePair<string, YamlNode>(key, value));
        }
        return new YamlMap(entries);
    }

    private static YamlList ParseList(List<Line> lines, ref int pos, int indent)
    {
        var items = new List<YamlNode>();
        while (pos < lines.Count && lines[pos].Indent == indent
               && IsListItem(lines[pos].Content))
        {
            var line = lines[pos];
            var afterDash = line.Content == "-"
                ? string.Empty
                : line.Content[2..].TrimStart();
            if (afterDash.Length == 0)
            {
                // `-` alone: nested block on the following lines.
                pos++;
                if (pos < lines.Count && lines[pos].Indent > indent)
                    items.Add(ParseBlock(lines, ref pos, lines[pos].Indent));
                else
                    items.Add(new YamlScalar(string.Empty));
                continue;
            }

            if (FindKeyColon(afterDash) >= 0 && !afterDash.StartsWith('"') && !afterDash.StartsWith('\''))
            {
                // `- key: value` — a map whose first entry sits on the dash line.
                var entries = new List<KeyValuePair<string, YamlNode>>();
                var firstIndent = indent + 2;
                ParseMapEntry(lines, ref pos, firstIndent, afterDash, line.Number, entries, inlineFirst: true);
                while (pos < lines.Count && lines[pos].Indent == firstIndent
                       && !IsListItem(lines[pos].Content))
                {
                    ParseMapEntry(lines, ref pos, firstIndent, lines[pos].Content, lines[pos].Number, entries, inlineFirst: false);
                }
                items.Add(new YamlMap(entries));
            }
            else
            {
                items.Add(ParseInline(afterDash, line.Number));
                pos++;
            }
        }
        return new YamlList(items);
    }

    private static void ParseMapEntry(
        List<Line> lines, ref int pos, int indent, string content, int lineNumber,
        List<KeyValuePair<string, YamlNode>> entries, bool inlineFirst)
    {
        var colon = FindKeyColon(content);
        if (colon < 0)
            throw new PipelineException($"Line {lineNumber}: expected 'key: value'.");
        var key = Unquote(content[..colon].Trim());
        if (entries.Any(e => e.Key == key))
            throw new PipelineException($"Line {lineNumber}: duplicate key '{key}'.");
        var rest = content[(colon + 1)..].Trim();
        if (!inlineFirst) pos++;
        else pos++;
        YamlNode value;
        if (rest.Length > 0)
        {
            value = ParseInline(rest, lineNumber);
        }
        else if (pos < lines.Count && lines[pos].Indent > indent)
        {
            value = ParseBlock(lines, ref pos, lines[pos].Indent);
        }
        else
        {
            value = new YamlScalar(string.Empty);
        }
        entries.Add(new KeyValuePair<string, YamlNode>(key, value));
    }

    private static YamlNode ParseInline(string text, int lineNumber)
    {
        if (text.StartsWith('[') && text.EndsWith(']'))
        {
            var inner = text[1..^1].Trim();
            if (inner.Length == 0) return new YamlList([]);
            return new YamlList(SplitFlow(inner).Select(part => new YamlScalar(Unquote(part.Trim()))).ToList());
        }
        if (text.StartsWith('{') && text.EndsWith('}'))
        {
            var inner = text[1..^1].Trim();
            var entries = new List<KeyValuePair<string, YamlNode>>();
            if (inner.Length > 0)
            {
                foreach (var part in SplitFlow(inner))
                {
                    var colon = part.IndexOf(':');
                    if (colon < 0)
                        throw new PipelineException($"Line {lineNumber}: bad flow map entry '{part}'.");
                    entries.Add(new KeyValuePair<string, YamlNode>(
                        Unquote(part[..colon].Trim()), new YamlScalar(Unquote(part[(colon + 1)..].Trim()))));
                }
            }
            return new YamlMap(entries);
        }
        return new YamlScalar(Unquote(text));
    }

    private static IEnumerable<string> SplitFlow(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var quote = '\0';
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c == '"' || c == '\'')
            {
                quote = c;
            }
            else if (c == '[' || c == '{') depth++;
            else if (c == ']' || c == '}') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return parts;
    }

    private static bool IsListItem(string content) =>
        content == "-" || content.StartsWith("- ", StringComparison.Ordinal);

    /// <summary>Finds the colon that separates a mapping key from its value.</summary>
    private static int FindKeyColon(string content)
    {
        var quote = '\0';
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }
            if (c == '"' || c == '\'') { quote = c; continue; }
            if (c == ':' && (i + 1 == content.Length || content[i + 1] == ' '))
                return i;
        }
        return -1;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1]
                .Replace("\\n", "\n").Replace("\\t", "\t")
                .Replace("\\\"", "\"").Replace("\\\\", "\\");
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1].Replace("''", "'");
        return value;
    }

    private static string StripComment(string line)
    {
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c == '"' || c == '\'')
            {
                quote = c;
            }
            else if (c == '#' && (i == 0 || line[i - 1] == ' ' || line[i - 1] == '\t'))
            {
                return line[..i].TrimEnd();
            }
        }
        return line;
    }
}
