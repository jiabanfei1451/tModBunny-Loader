using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BunnyLoader.Hlsl;

/// <summary>
/// Um token do HLSL de efeito. `kind` e' o que o parser olha; `text` guarda o
/// lexema cru (o tradutor reescreve tipos, intrinsics e modificadores).
/// </summary>
public sealed class HlslToken
{
    public HlslTokenKind Kind;
    public string Text = string.Empty;
    public int Line;
    public int Column;

    public override string ToString() => $"{Kind}:{Text}";

    public bool Is(HlslTokenKind kind) => Kind == kind;

    public bool IsPunct(string text) => Kind == HlslTokenKind.Punct && Text == text;

    public bool IsIdent(string text) => Kind == HlslTokenKind.Identifier && Text == text;
}

public enum HlslTokenKind
{
    Identifier,
    Number,
    String,
    Punct,
    End,
}

/// <summary>
/// O lexer do HLSL de `.fx`. So' cuida do que aparece nesses arquivos: numeros,
/// identificadores, strings, comentarios (`//` e `/* */`), pre-processador
/// basico (`#define`/`#ifdef`/`#ifndef`/`#else`/`#endif`, sem avaliacao de
/// expressao) e pontuacao de 1 de 2 caracteres.
///
/// Ele nao valida a gramatica: o <see cref="HlslParser"/> faz isso; aqui a
/// unica decisao e' `&amp;&amp;`/`||`/`&lt;=`/... virarem um token so'.
/// </summary>
public static class HlslLexer
{
    const string TwoCharPunct =
        "&& || <= >= == != += -= *= /= %= ++ -- << >> :: ";

    public static List<HlslToken> Lex(string source)
    {
        List<HlslToken> tokens = new();
        // Estados de `#define`: uma macro vira `valor` sem texto (o parser nunca
        // ve o nome), o que cobre todas as macros usadas nos mods.
        Dictionary<string, string?> defines = new(StringComparer.Ordinal);
        Stack<(bool active, bool taken)> conditions = new();
        bool active = true;

        int i = 0;
        int line = 1;
        int lineStart = 0;
        int n = source?.Length ?? 0;

        int Column(int position) => position - lineStart + 1;

        while (i < n)
        {
            char c = source[i];

            // --- espaco e quebras -------------------------------------------------
            if (c == '\n')
            {
                i++;
                line++;
                lineStart = i;
                continue;
            }
            if (char.IsWhiteSpace(c) || c == '\r')
            {
                i++;
                continue;
            }

            // --- comentarios ------------------------------------------------------
            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                while (i < n && source[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < n && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    if (source[i] == '\n')
                    {
                        line++;
                        lineStart = i + 1;
                    }
                    i++;
                }
                i = Math.Min(i + 2, n);
                continue;
            }

            // --- pre-processador --------------------------------------------------
            if (c == '#')
            {
                int directiveStart = i;
                i++;
                while (i < n && (source[i] == ' ' || source[i] == '\t')) i++;
                int begin = i;
                while (i < n && IsIdentChar(source[i])) i++;
                string directive = source.Substring(begin, i - begin);
                string rest = ReadRestOfLine(source, ref i, out int endLine, ref line, ref lineStart);

                switch (directive)
                {
                    case "define":
                    {
                        int p = 0;
                        while (p < rest.Length && char.IsWhiteSpace(rest[p])) p++;
                        int nameStart = p;
                        while (p < rest.Length && IsIdentChar(rest[p])) p++;
                        string name = rest.Substring(nameStart, p - nameStart);
                        if (active && name.Length > 0) defines[name] = rest.Substring(Math.Min(p, rest.Length)).Trim();
                        break;
                    }
                    case "undef":
                    {
                        string name = rest.Trim();
                        if (active && name.Length > 0) defines.Remove(name);
                        break;
                    }
                    case "ifdef":
                    case "ifndef":
                    {
                        bool defined = defines.ContainsKey(rest.Trim());
                        bool state = directive == "ifdef" ? defined : !defined;
                        conditions.Push((active, state));
                        active = active && state;
                        break;
                    }
                    case "if":
                    {
                        // `#if 1` / `#if 0` sao os unicos usos reais; o resto fica
                        // "ativo" para nao perder codigo se o mod escrever algo
                        // esquisito (o validador do GLSL acusa depois).
                        string trimmed = rest.Trim();
                        bool state = trimmed != "0";
                        conditions.Push((active, state));
                        active = active && state;
                        break;
                    }
                    case "else":
                    {
                        if (conditions.Count > 0)
                        {
                            (bool outer, bool taken) = conditions.Pop();
                            bool state = !taken;
                            conditions.Push((outer, true));
                            active = outer && state;
                        }
                        break;
                    }
                    case "endif":
                    {
                        if (conditions.Count > 0)
                        {
                            (bool outer, _) = conditions.Pop();
                            active = outer;
                        }
                        break;
                    }
                    case "include":
                    case "pragma":
                    case "line":
                        break;
                    default:
                        // diretiva desconhecida: ignora a linha inteira
                        break;
                }

                _ = directiveStart;
                _ = endLine;
                continue;
            }

            // --- string -----------------------------------------------------------
            if (c == '"')
            {
                int start = i;
                i++;
                while (i < n && source[i] != '"' && source[i] != '\n')
                {
                    if (source[i] == '\\' && i + 1 < n) i++;
                    i++;
                }
                int end = i;
                if (i < n && source[i] == '"') i++;
                if (active)
                {
                    tokens.Add(new HlslToken
                    {
                        Kind = HlslTokenKind.String,
                        Text = source.Substring(start, Math.Min(end + 1, n) - start),
                        Line = line,
                        Column = Column(start),
                    });
                }
                continue;
            }

            // --- numero -----------------------------------------------------------
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(source[i + 1])))
            {
                int start = i;
                i = ReadNumber(source, i);
                if (active)
                {
                    tokens.Add(new HlslToken
                    {
                        Kind = HlslTokenKind.Number,
                        Text = source.Substring(start, i - start),
                        Line = line,
                        Column = Column(start),
                    });
                }
                continue;
            }

            // --- identificador ----------------------------------------------------
            if (IsIdentStart(c))
            {
                int start = i;
                i++;
                while (i < n && IsIdentChar(source[i])) i++;
                string text = source.Substring(start, i - start);
                if (active)
                {
                    tokens.Add(new HlslToken
                    {
                        Kind = HlslTokenKind.Identifier,
                        Text = text,
                        Line = line,
                        Column = Column(start),
                    });
                }
                continue;
            }

            // --- pontuacao --------------------------------------------------------
            {
                int start = i;
                string two = i + 1 < n ? source.Substring(i, 2) : string.Empty;
                if (two.Length == 2 && TwoCharPunct.Contains(two, StringComparison.Ordinal))
                {
                    i += 2;
                    if (active) tokens.Add(new HlslToken { Kind = HlslTokenKind.Punct, Text = two, Line = line, Column = Column(start) });
                }
                else
                {
                    i++;
                    if (active) tokens.Add(new HlslToken { Kind = HlslTokenKind.Punct, Text = source.Substring(start, 1), Line = line, Column = Column(start) });
                }
                continue;
            }
        }

        tokens.Add(new HlslToken { Kind = HlslTokenKind.End, Text = string.Empty, Line = line, Column = Column(n) });
        return tokens;
    }

    static string ReadRestOfLine(string source, ref int i, out int endLine, ref int line, ref int lineStart)
    {
        int n = source.Length;
        int start = i;
        while (i < n && source[i] != '\n')
        {
            // continua em `\` no fim da linha
            if (source[i] == '\\' && i + 1 < n && source[i + 1] == '\n')
            {
                i += 2;
                line++;
                lineStart = i;
                continue;
            }
            i++;
        }
        endLine = line;
        string rest = source.Substring(start, i - start);
        if (i < n)
        {
            i++;
            line++;
            lineStart = i;
        }
        return rest;
    }

    static int ReadNumber(string source, int i)
    {
        int n = source.Length;
        if (source[i] == '0' && i + 1 < n && (source[i + 1] == 'x' || source[i + 1] == 'X'))
        {
            i += 2;
            while (i < n && Uri.IsHexDigit(source[i])) i++;
            if (i < n && (source[i] == 'u' || source[i] == 'U')) i++;
            return i;
        }

        while (i < n && char.IsDigit(source[i])) i++;
        if (i < n && source[i] == '.')
        {
            i++;
            while (i < n && char.IsDigit(source[i])) i++;
        }
        if (i < n && (source[i] == 'e' || source[i] == 'E'))
        {
            int save = i;
            i++;
            if (i < n && (source[i] == '+' || source[i] == '-')) i++;
            if (i < n && char.IsDigit(source[i]))
            {
                while (i < n && char.IsDigit(source[i])) i++;
            }
            else
            {
                i = save;
            }
        }
        if (i < n && (source[i] == 'f' || source[i] == 'F' || source[i] == 'h' || source[i] == 'H'
                      || source[i] == 'u' || source[i] == 'U' || source[i] == 'l' || source[i] == 'L'))
        {
            i++;
        }
        return i;
    }

    public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    public static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Valor numerico do token, para o codigo gerado poder corrigir `1` -&gt; `1.0`.</summary>
    public static bool TryFloat(string text, out float value)
    {
        string trimmed = text.TrimEnd('f', 'F', 'h', 'H');
        return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static string Unescape(string literal)
    {
        if (literal.Length < 2) return literal;
        StringBuilder sb = new(literal.Length);
        for (int i = 1; i < literal.Length - 1; i++)
        {
            char c = literal[i];
            if (c != '\\' || i + 1 >= literal.Length - 1)
            {
                sb.Append(c);
                continue;
            }
            char next = literal[++i];
            switch (next)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case '\\': sb.Append('\\'); break;
                case '"': sb.Append('"'); break;
                default: sb.Append(next); break;
            }
        }
        return sb.ToString();
    }
}
