using CodeMaze.MazeLang.Ast;
using CodeMaze.MazeLang.Compiler.AST;

namespace CodeMaze.MazeLang.Parser.Text;

internal enum TokenKind { Word, Number, LParen, RParen, Comma, Eof }

internal readonly record struct Token(
    TokenKind Kind, string Text, Keyword? Keyword, int Number, int Line, SourceLocation Location);

internal static class Lexer
{
    public static List<Token> Lex(string text, LanguageTable lang, List<Diagnostic> diags)
    {
        var tokens = new List<Token>();
        int pos = 0, line = 1, lineStart = 0;
        var prefix = lang.CommentPrefix;

        while (pos < text.Length)
        {
            char c = text[pos];
            var loc = new SourceLocation(line, pos - lineStart + 1);

            if (c == '\n') { pos++; line++; lineStart = pos; continue; }
            if (char.IsWhiteSpace(c)) { pos++; continue; }

            if (prefix.Length > 0 && string.CompareOrdinal(text, pos, prefix, 0, prefix.Length) == 0)
            {
                while (pos < text.Length && text[pos] != '\n') pos++;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int s = pos;
                while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_')) pos++;
                var norm = lang.Normalize(text[s..pos]);
                tokens.Add(new Token(TokenKind.Word, norm, lang.TryKeyword(norm), 0, line, loc));
                continue;
            }

            if (char.IsDigit(c))
            {
                int s = pos;
                while (pos < text.Length && char.IsDigit(text[pos])) pos++;
                var digits = text[s..pos];
                if (!int.TryParse(digits, out var n))
                {
                    diags.Add(new Diagnostic($"The number '{digits}' is too large.", loc));
                    n = 0;
                }
                tokens.Add(new Token(TokenKind.Number, digits, null, n, line, loc));
                continue;
            }

            pos++;
            switch (c)
            {
                case '(': tokens.Add(new Token(TokenKind.LParen, "(", null, 0, line, loc)); break;
                case ')': tokens.Add(new Token(TokenKind.RParen, ")", null, 0, line, loc)); break;
                case ',': tokens.Add(new Token(TokenKind.Comma, ",", null, 0, line, loc)); break;
                default: diags.Add(new Diagnostic($"Unexpected character '{c}'.", loc)); break;
            }
        }

        tokens.Add(new Token(TokenKind.Eof, "", null, 0, line, new SourceLocation(line, pos - lineStart + 1)));
        return tokens;
    }
}