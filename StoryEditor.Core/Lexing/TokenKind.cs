namespace StoryEditor2.Lexing;
public enum TokenKind { Trivia, Identifier, IntegerLiteral, RealLiteral, StringLiteral, Punctuation, EndOfFile }
public sealed record Token(TokenKind Kind, string Text, int Start, int Length)
{
    public int End => Start + Length;
}