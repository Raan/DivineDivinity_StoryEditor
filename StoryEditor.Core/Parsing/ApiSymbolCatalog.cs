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

public sealed record ApiParameterSignature(string Direction, string Type);

public sealed record ApiFunctionSignature(
    ApiSymbolKind Kind,
    IReadOnlyList<ApiParameterSignature> Parameters);

public sealed class ApiSymbolCatalog
{
    // Greedy parameter capture is intentional: Osiris types are wrapped in parentheses,
    // so the first ')' is not the end of the declaration's parameter list.
    private static readonly Regex DeclarationPattern = new(
        @"^[ \t]*(event|query|call|sysquery|syscall)[ \t]+([A-Za-z_]\w*)\((.*)\)[ \t]+\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ParameterPattern = new(
        @"\[(in|out)\]\(([A-Za-z_]\w*)\)_Arg\d+",
        RegexOptions.Compiled);

    private static readonly Regex ObjectPattern = new(
        @"^[ \t]*object\s*\{\s*([A-Za-z_]\w*)\s*,\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex TypePattern = new(
        @"^[ \t]*type\s*\{\s*([A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly IReadOnlyDictionary<int, string> ObjectTypeNames =
        new Dictionary<int, string>
        {
            [4] = "NPC", [5] = "OBJECT", [6] = "DIALOG", [7] = "REGION",
            [8] = "LOCATION", [9] = "NPC_CLASS", [10] = "OBJECT_CLASS",
            [11] = "DIALOG_EVENT", [12] = "ENGINE", [13] = "FUNCTION", [15] = "SREGION"
        };

    public IReadOnlyDictionary<string, ApiSymbolKind> Functions { get; }
    public IReadOnlyDictionary<string, ApiFunctionSignature> FunctionSignatures { get; }
    public IReadOnlySet<string> Objects { get; }
    public IReadOnlyDictionary<string, string> ObjectTypes { get; }
    public IReadOnlySet<string> Types { get; }

    private ApiSymbolCatalog(
        Dictionary<string, ApiSymbolKind> functions,
        Dictionary<string, ApiFunctionSignature> signatures,
        HashSet<string> objects,
        Dictionary<string, string> objectTypes,
        HashSet<string> types)
    {
        Functions = functions;
        FunctionSignatures = signatures;
        Objects = objects;
        ObjectTypes = objectTypes;
        Types = types;
    }

    public static ApiSymbolCatalog Build(string source)
    {
        var functions = new Dictionary<string, ApiSymbolKind>(StringComparer.Ordinal);
        var signatures = new Dictionary<string, ApiFunctionSignature>(StringComparer.Ordinal);
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
            string name = match.Groups[2].Value;
            var parameters = ParameterPattern.Matches(match.Groups[3].Value)
                .Select(parameter => new ApiParameterSignature(
                    parameter.Groups[1].Value, parameter.Groups[2].Value))
                .ToArray();
            functions[name] = kind;
            signatures[name] = new ApiFunctionSignature(kind, parameters);
        }

        var objectTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in ObjectPattern.Matches(source))
        {
            if (int.TryParse(match.Groups[2].Value, out int typeCode) &&
                ObjectTypeNames.TryGetValue(typeCode, out string? typeName))
                objectTypes[match.Groups[1].Value] = typeName;
        }
        var objects = objectTypes.Keys.ToHashSet(StringComparer.Ordinal);
        var types = TypePattern.Matches(source).Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        return new ApiSymbolCatalog(functions, signatures, objects, objectTypes, types);
    }
}
