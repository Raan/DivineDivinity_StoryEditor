using System.IO;
using StoryEditor2.Lexing;

namespace StoryEditor2.Documents;

public sealed class StoryDocument
{
    private StoryDocument(string path, string source, IReadOnlyList<Token> tokens)
    { Path = path; Source = source; Tokens = tokens; }

    public string Path { get; }
    public string Source { get; }
    public IReadOnlyList<Token> Tokens { get; }

    public static StoryDocument Load(string path)
    {
        string source = File.ReadAllText(path);
        return new StoryDocument(path, source, Lexer.Tokenize(source));
    }

    public bool IsLosslessTokenization()
    {
        string rebuilt = string.Concat(Tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Text));
        return string.Equals(Source, rebuilt, StringComparison.Ordinal);
    }
}