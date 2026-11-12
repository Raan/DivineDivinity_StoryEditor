namespace StoryEditor2.Lexing;

public sealed class Lexer
{
    private readonly string _source;
    private readonly List<Token> _tokens = new();
    private int _position;
    private Lexer(string source) => _source = source;

    public static IReadOnlyList<Token> Tokenize(string source)
    {
        var lexer = new Lexer(source);
        lexer.Scan();
        return lexer._tokens;
    }

    private void Scan()
    {
        while (_position < _source.Length)
        {
            int start = _position;
            char c = Current;
            if (char.IsWhiteSpace(c))
            {
                do { _position++; } while (_position < _source.Length && char.IsWhiteSpace(Current));
                Add(TokenKind.Trivia, start); continue;
            }
            if (c == '/' && Peek(1) == '/')
            {
                _position += 2;
                while (_position < _source.Length && Current is not '\r' and not '\n') _position++;
                Add(TokenKind.Trivia, start); continue;
            }
            if (c == '/' && Peek(1) == '*')
            {
                _position += 2;
                while (_position < _source.Length && !(Current == '*' && Peek(1) == '/')) _position++;
                if (_position < _source.Length) _position += 2;
                Add(TokenKind.Trivia, start); continue;
            }
            if (c == '"')
            {
                ScanString(); Add(TokenKind.StringLiteral, start); continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                _position++;
                while (_position < _source.Length && (char.IsLetterOrDigit(Current) || Current == '_')) _position++;
                Add(TokenKind.Identifier, start); continue;
            }
            if (char.IsDigit(c))
            {
                ScanNumber();
                Add(_source.AsSpan(start, _position - start).IndexOf('.') >= 0
                    ? TokenKind.RealLiteral : TokenKind.IntegerLiteral, start);
                continue;
            }
            if ((c is '<' or '>' or '=' or '!') && Peek(1) == '=') _position += 2;
            else _position++;
            Add(TokenKind.Punctuation, start);
        }
        _tokens.Add(new Token(TokenKind.EndOfFile, string.Empty, _source.Length, 0));
    }

    private void ScanString()
    {
        _position++;
        while (_position < _source.Length)
        {
            if (Current == '\\' && _position + 1 < _source.Length) { _position += 2; continue; }
            if (Current == '"') { _position++; return; }
            _position++;
        }
    }
    private void ScanNumber()
    {
        while (_position < _source.Length && char.IsDigit(Current)) _position++;
        if (_position < _source.Length && Current == '.' && _position + 1 < _source.Length && char.IsDigit(Peek(1)))
        {
            _position++;
            while (_position < _source.Length && char.IsDigit(Current)) _position++;
        }
    }
    private char Current => _source[_position];
    private char Peek(int n) => _position + n < _source.Length ? _source[_position + n] : '\0';
    private void Add(TokenKind kind, int start) => _tokens.Add(new Token(kind, _source[start.._position], start, _position - start));
}