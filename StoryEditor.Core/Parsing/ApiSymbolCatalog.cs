using System.Text.RegularExpressions;

namespace StoryEditor2.Parsing;

public enum ApiSymbolKind
{
    Event,
    Query,
    Call,
    SystemQuery,
    SystemCall
}

public sealed class ApiSymbolCatalog
{
    private static readonly Regex DeclarationPattern = new(
        @"^\s*(event|query|call|sysquery|syscall)\s+([A-Za-z_]\w*)\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ObjectPattern = new(
        @"^\s*object\s*\{\s*([A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex TypePattern = new(
        @"^\s*type\s*\{\s*([A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public IReadOnlyDictionary<string, ApiSymbolKind> Functions { get; }
    public IReadOnlySet<string> Objects { get; }
    public IReadOnlySet<string> Types { get; }

    private ApiSymbolCatalog(
        Dictionary<string, ApiSymbolKind> functions,
        HashSet<string> objects,
        HashSet<string> types)
    {
        Functions = functions;
        Objects = objects;
        Types = types;
    }

    public static ApiSymbolCatalog Build(string source)
    {
        var functions = new Dictionary<string, ApiSymbolKind>(StringComparer.Ordinal);
        foreach (Match match in DeclarationPattern.Matches(source))
        {
            var kind = match.Groups[1].Value switch
            {
                "event" => ApiSymbolKind.Event,
                "query" => ApiSymbolKind.Query,
                "call" => ApiSymbolKind.Call,
                "sysquery" => ApiSymbolKind.SystemQuery,
                "syscall" => ApiSymbolKind.SystemCall,
                _ => throw new InvalidOperationException("Unknown API declaration kind.")
            };
            functions[match.Groups[2].Value] = kind;
        }

        var objects = ObjectPattern.Matches(source).Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var types = TypePattern.Matches(source).Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        return new ApiSymbolCatalog(functions, objects, types);
    }
}
