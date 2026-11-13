namespace StoryEditor2.Parsing;

/// <summary>
/// Editable INIT/EXIT action list that retains the original trivia around statements.
/// Trivia immediately preceding a statement travels with that statement when reordered.
/// </summary>
public sealed class LifecycleActionListDocument
{
    private sealed class Entry(string prefix, string action)
    {
        public string Prefix { get; set; } = prefix;
        public string Action { get; set; } = action;
    }

    private readonly List<Entry> _entries = [];
    private readonly string _header;
    private readonly string _footer;
    private string _trailing;
    private readonly string _lineEnding;
    private readonly string _indent;
    private readonly ApiSymbolCatalog? _api;

    public IReadOnlyList<string> Actions => _entries.Select(entry => entry.Action).ToArray();

    private LifecycleActionListDocument(string source, ApiSymbolCatalog? api)
    {
        _api = api;
        var parsed = new ActionListParser().Parse(source, api);
        if (parsed.Diagnostics.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                parsed.Diagnostics.Select(d => d.Message)));

        int openBrace = source.IndexOf('{');
        int closeBrace = source.LastIndexOf('}');
        if (openBrace < 0 || closeBrace <= openBrace)
            throw new InvalidOperationException("The lifecycle section has no valid body.");

        _header = source[..(openBrace + 1)];
        _footer = source[closeBrace..];
        int cursor = openBrace + 1;
        foreach (var statement in parsed.Statements)
        {
            _entries.Add(new Entry(
                source[cursor..statement.Start],
                source.Substring(statement.Start, statement.Length - 1)));
            cursor = statement.Start + statement.Length;
        }
        _trailing = source[cursor..closeBrace];

        _lineEnding = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string firstPrefix = _entries.FirstOrDefault()?.Prefix ?? _trailing;
        int lastNewline = firstPrefix.LastIndexOf('\n');
        _indent = lastNewline >= 0
            ? new string(firstPrefix[(lastNewline + 1)..].TakeWhile(char.IsWhiteSpace).ToArray())
            : "    ";
    }

    public static LifecycleActionListDocument Parse(string source, ApiSymbolCatalog? api = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new LifecycleActionListDocument(source, api);
    }

    public void Add(string action, int? afterIndex = null)
    {
        string normalized = ValidateAction(action);
        int insertAt = afterIndex.HasValue
            ? Math.Clamp(afterIndex.Value + 1, 0, _entries.Count)
            : _entries.Count;
        string prefix = _lineEnding + _indent;
        if (_entries.Count == 0 && !_trailing.Contains('\n') && !_trailing.Contains('\r'))
            prefix = _trailing.Length == 0 ? " " : "";
        _entries.Insert(insertAt, new Entry(prefix, normalized));
    }

    public void Update(int index, string action)
    {
        if (index < 0 || index >= _entries.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _entries[index].Action = ValidateAction(action);
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= _entries.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        string prefix = _entries[index].Prefix;
        _entries.RemoveAt(index);
        if (index < _entries.Count)
            _entries[index].Prefix = prefix + _entries[index].Prefix;
        else
            _trailing = prefix + _trailing;
    }

    public bool Move(int index, int direction)
    {
        if (index < 0 || index >= _entries.Count)
            return false;
        int target = index + Math.Sign(direction);
        if (target < 0 || target >= _entries.Count || direction == 0)
            return false;
        (_entries[index], _entries[target]) = (_entries[target], _entries[index]);
        return true;
    }

    public string ToSource() =>
        _header +
        string.Concat(_entries.Select(entry => entry.Prefix + entry.Action + ";")) +
        _trailing + _footer;

    private string ValidateAction(string action)
    {
        string normalized = (action ?? "").Trim();
        if (normalized.EndsWith(';'))
            normalized = normalized[..^1].TrimEnd();
        if (normalized.Length == 0)
            throw new ArgumentException("Enter an Osiris action.", nameof(action));

        var parsed = new ActionListParser().Parse("INIT { " + normalized + "; }", _api);
        if (parsed.Diagnostics.Count > 0 || parsed.Statements.Count != 1)
        {
            string message = parsed.Diagnostics.Count > 0
                ? string.Join(Environment.NewLine, parsed.Diagnostics.Select(d => d.Message))
                : "Enter exactly one action statement.";
            throw new ArgumentException(message, nameof(action));
        }
        return normalized;
    }
}
