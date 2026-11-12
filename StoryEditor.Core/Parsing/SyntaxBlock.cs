namespace StoryEditor2.Parsing;

public enum SyntaxKind
{
    Unknown,
    TypeDeclaration,
    ObjectDeclaration,
    GoalBlock,
    GoalTitle,
    InitSection,
    KnowledgeBaseSection,
    ExitSection
}

public sealed record SyntaxBlock(
    SyntaxKind Kind,
    string Header,
    int Start,
    int Length,
    IReadOnlyList<SyntaxBlock> Children)
{
    public int End => Start + Length;
}

public sealed record ParseDiagnostic(string Message, int Position);
public sealed record StructureParseResult(
    IReadOnlyList<SyntaxBlock> Blocks,
    IReadOnlyList<ParseDiagnostic> Diagnostics);
