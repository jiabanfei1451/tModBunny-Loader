using System;
using System.Collections.Generic;

namespace BunnyLoader.Hlsl;

public sealed class HlslParseException : Exception
{
    public HlslParseException(int line, string message) : base($"linha {line}: {message}")
    {
    }
}

/// <summary>
/// O parser descendente recursivo do HLSL de efeito. Cobre o subconjunto que
/// os 41 `.fx` do FargowiltasSouls usam (e um pouco mais, de defesa):
/// globais, `sampler : register(sN)`, `struct` com semanticas, funcoes com
/// parametros `in/out/inout` e `technique { pass { ... } }`.
///
/// Nao ha `for`/`while`/`switch` nos arquivos reais, mas eles sao aceitos
/// (o GLSL tambem tem), e `discard`/`clip` tambem (o GLSL suporta).
/// </summary>
public sealed class HlslParser
{
    // Ordem de precedencia do HLSL/C, do mais fraco para o mais forte.
    static readonly string[][] Levels =
    {
        new[] { "||" },
        new[] { "&&|&" },                  // o `&` solto fica em Bits
        new[] { "==|!=" },
        new[] { "<|>|<=|>=" },
        new[] { "|" },
        new[] { "^" },
        new[] { "&" },
        new[] { "|<<" },
        new[] { "|>>" },
        new[] { "+|-" },
        new[] { "*|/|%" },
    };

    readonly List<HlslToken> tokens;
    int position;
    readonly string file;

    public HlslParser(List<HlslToken> tokens, string file)
    {
        this.tokens = tokens;
        this.file = file;
    }

    HlslToken Current => tokens[Math.Min(position, tokens.Count - 1)];

    HlslToken Peek(int ahead = 1) => tokens[Math.Min(position + ahead, tokens.Count - 1)];

    bool AtEnd => Current.Kind == HlslTokenKind.End;

    HlslToken Advance()
    {
        HlslToken token = Current;
        if (position < tokens.Count - 1) position++;
        return token;
    }

    HlslParseException Error(string message) => new(Current.Line, $"{message} (token '{Current.Text}') em {file}");

    bool Accept(string punct)
    {
        if (Current.IsPunct(punct))
        {
            Advance();
            return true;
        }
        return false;
    }

    bool AcceptIdent(string name)
    {
        if (Current.IsIdent(name))
        {
            Advance();
            return true;
        }
        return false;
    }

    void Expect(string punct)
    {
        if (!Accept(punct)) throw Error($"esperava '{punct}'");
    }

    string ExpectIdentifier()
    {
        if (Current.Kind != HlslTokenKind.Identifier) throw Error("esperava um identificador");
        return Advance().Text;
    }

    // --- documento -----------------------------------------------------------------------------

    public HlslDocument ParseDocument()
    {
        HlslDocument document = new();
        while (!AtEnd)
        {
            if (Accept(";")) continue;

            if (Current.IsIdent("struct"))
            {
                document.Structs.Add(ParseStruct());
                continue;
            }

            if (Current.IsIdent("technique") || Current.IsIdent("technique10") || Current.IsIdent("technique11"))
            {
                document.Techniques.Add(ParseTechnique());
                Accept(";");
                continue;
            }

            if (Current.IsIdent("typedef"))
            {
                // `typedef float4 myfloat4;` — so' guarda como alias
                Advance();
                SkipToSemicolon();
                Accept(";");
                continue;
            }

            // nome de tipo (com modificadores opcionais)
            SkipStorageModifiers();
            string type = ParseTypeName();

            if (Current.IsPunct("("))
            {
                // construtor no escopo global nao faz sentido; trata como erro
                throw Error("declaracao inesperada no escopo global");
            }

            string first = ExpectIdentifier();

            // funcao?
            if (Current.IsPunct("("))
            {
                document.Functions.Add(ParseFunctionRest(type, first));
                continue;
            }

            // global (um ou varios)
            HlslGlobals group = new() { Type = type };
            ParseVariableRest(group.Variables, first);
            while (Accept(","))
            {
                ParseVariableRest(group.Variables, ExpectIdentifier());
            }
            Accept(";");
            document.Globals.Add(group);
        }
        return document;
    }

    void SkipStorageModifiers()
    {
        while (Current.Kind == HlslTokenKind.Identifier)
        {
            string text = Current.Text;
            if (text is "static" or "uniform" or "extern" or "const" or "inline" or "shared")
            {
                Advance();
                continue;
            }
            break;
        }
    }

    string ParseTypeName()
    {
        if (Current.Kind != HlslTokenKind.Identifier) throw Error("esperava um nome de tipo");
        string name = Advance().Text;
        // `const float` dentro de parametro, `unsigned int`...
        if (name == "unsigned" && Current.IsIdent("int"))
        {
            Advance();
            return "uint";
        }
        if (name == "sampler2D" || name == "sampler3D" || name == "samplerCUBE" || name == "texture"
            || name == "texture2D" || name == "Texture2D" || name == "SamplerState")
        {
            // aceita mas o tradutor trata todos como sampler2D
            return "sampler";
        }
        return name;
    }

    HlslStruct ParseStruct()
    {
        Advance(); // struct
        HlslStruct result = new() { Name = ExpectIdentifier() };
        Expect("{");
        while (!Accept("}"))
        {
            if (AtEnd) throw Error("struct sem '}'");
            string type = ParseTypeName();
            string name = ExpectIdentifier();
            HlslStructField field = new() { Type = type, Name = name };
            if (Accept(":"))
            {
                field.Semantic = ExpectIdentifier();
            }
            if (Accept("["))
            {
                field.ArraySize = (int)ExpectInt();
                Expect("]");
            }
            Accept(";");
            result.Fields.Add(field);
        }
        return result;
    }

    HlslFunction ParseFunctionRest(string returnType, string name)
    {
        HlslFunction function = new() { ReturnType = returnType, Name = name };
        Expect("(");
        if (!Accept(")"))
        {
            while (true)
            {
                HlslParameter parameter = new();
                while (Current.IsIdent("in") || Current.IsIdent("out") || Current.IsIdent("inout") || Current.IsIdent("uniform") || Current.IsIdent("const"))
                {
                    if (Current.IsIdent("in")) parameter.In = true;
                    else if (Current.IsIdent("out")) parameter.Out = true;
                    else if (Current.IsIdent("inout")) { parameter.In = true; parameter.Out = true; }
                    Advance();
                }
                parameter.Type = ParseTypeName();
                parameter.Name = ExpectIdentifier();
                if (Accept("["))
                {
                    parameter.ArraySize = (int)ExpectInt();
                    Expect("]");
                }
                if (Accept(":")) parameter.Semantic = ExpectIdentifier();
                if (Accept("=")) parameter.Default = ParseExpression();
                function.Parameters.Add(parameter);
                if (Accept(",")) continue;
                Expect(")");
                break;
            }
        }
        if (Accept(":")) function.ReturnSemantic = ExpectIdentifier();
        if (Accept(";"))
        {
            // prototipo sem corpo: so' registro, o tradutor usa quando o corpo vem depois
            return function;
        }
        function.Body = ParseBlock();
        return function;
    }

    void ParseVariableRest(List<HlslVariable> list, string name)
    {
        HlslVariable variable = new() { Name = name };
        if (Accept("["))
        {
            variable.ArraySize = (int)ExpectInt();
            Expect("]");
        }
        if (Accept(":"))
        {
            if (Current.IsIdent("register") || Current.IsIdent("REGISTER") || Current.IsIdent("Register"))
            {
                Advance();
                Expect("(");
                string slot = Current.Text;
                if (slot.Length > 0 && (slot[0] == 's' || slot[0] == 'S' || slot[0] == 'c' || slot[0] == 'C'
                                        || slot[0] == 't' || slot[0] == 'T' || slot[0] == 'b' || slot[0] == 'B'))
                {
                    if (int.TryParse(slot.Substring(1), out int index)) variable.RegisterSlot = index;
                    Advance();
                }
                else if (Current.Kind == HlslTokenKind.Number)
                {
                    if (int.TryParse(Current.Text, out int index)) variable.RegisterSlot = index;
                    Advance();
                }
                Accept(")");
            }
            else
            {
                variable.Semantic = ExpectIdentifier();
            }
        }
        if (Accept("=")) variable.Initializer = ParseExpression();
        list.Add(variable);
        while (Accept("["))
        {
            // atribuicao de array (`float a[2] = { ... }`) — raro, pula
            while (!AtEnd && !Current.IsPunct("]")) Advance();
            Expect("]");
        }
    }

    HlslTechnique ParseTechnique()
    {
        Advance();
        HlslTechnique technique = new() { Name = Current.Kind == HlslTokenKind.Identifier ? Advance().Text : "Technique1" };
        Expect("{");
        while (!Accept("}"))
        {
            if (AtEnd) throw Error("technique sem '}'");
            if (!Current.IsIdent("pass"))
            {
                // anotacoes de efeito (`< ... >`); ignorar
                Advance();
                continue;
            }
            Advance();
            HlslPass pass = new() { Name = Current.Kind == HlslTokenKind.Identifier ? Advance().Text : "Pass0", File = file };
            Expect("{");
            while (!Accept("}"))
            {
                if (AtEnd) throw Error("pass sem '}'");
                string name = ExpectIdentifier();
                if (Accept("="))
                {
                    if (name == "PixelShader" || name == "VertexShader")
                    {
                        if (Current.IsIdent("compile"))
                        {
                            Advance();
                            string target = ExpectIdentifier(); // ps_3_0 etc.
                            string entry = ExpectIdentifier();
                            Expect("(");
                            Expect(")");
                            if (name == "PixelShader")
                            {
                                pass.PixelEntry = entry;
                                pass.PixelTarget = target;
                            }
                            else
                            {
                                pass.VertexEntry = entry;
                                pass.VertexTarget = target;
                            }
                        }
                        else
                        {
                            // `PixelShader = < ... >;` — nao suportado, pula
                            SkipToSemicolon();
                        }
                    }
                    else
                    {
                        int start = position;
                        SkipToSemicolon();
                        string raw = RawText(start, position);
                        pass.RenderStates.Add((name, raw));
                    }
                }
                Accept(";");
            }
        }
        return technique;
    }

    string RawText(int start, int end)
    {
        System.Text.StringBuilder sb = new();
        for (int i = start; i < end && i < tokens.Count; i++)
        {
            if (i > start) sb.Append(' ');
            sb.Append(tokens[i].Text);
        }
        return sb.ToString();
    }

    void SkipToSemicolon()
    {
        int depth = 0;
        while (!AtEnd)
        {
            if (Current.IsPunct("(") || Current.IsPunct("{") || Current.IsPunct("[")) depth++;
            else if (Current.IsPunct(")") || Current.IsPunct("}") || Current.IsPunct("]")) depth--;
            else if (Current.IsPunct(";") && depth <= 0)
            {
                return;
            }
            Advance();
        }
    }

    // --- instrucoes ----------------------------------------------------------------------------

    HlslBlock ParseBlock()
    {
        HlslBlock block = new() { Line = Current.Line };
        Expect("{");
        while (!Accept("}"))
        {
            if (AtEnd) throw Error("bloco sem '}'");
            block.Statements.Add(ParseStatement());
        }
        return block;
    }

    HlslStatement ParseStatement()
    {
        int line = Current.Line;

        if (Current.IsPunct("{")) return ParseBlock();
        if (Accept(";")) return new HlslBlock { Line = line };

        if (Current.IsIdent("if"))
        {
            Advance();
            Expect("(");
            HlslExpression condition = ParseExpression();
            Expect(")");
            HlslIf result = new() { Line = line, Condition = condition, Then = ParseStatement() };
            if (Current.IsIdent("else"))
            {
                Advance();
                result.Else = ParseStatement();
            }
            return result;
        }

        if (Current.IsIdent("return"))
        {
            Advance();
            HlslReturn result = new() { Line = line };
            if (!Current.IsPunct(";")) result.Value = ParseExpression();
            Accept(";");
            return result;
        }

        if (Current.IsIdent("discard"))
        {
            Advance();
            Accept(";");
            return new HlslDiscard { Line = line };
        }

        if (Current.IsIdent("break"))
        {
            Advance();
            Accept(";");
            return new HlslBreak { Line = line };
        }

        if (Current.IsIdent("continue"))
        {
            Advance();
            Accept(";");
            return new HlslContinue { Line = line };
        }

        if (Current.IsIdent("for"))
        {
            Advance();
            Expect("(");
            HlslFor result = new() { Line = line };
            if (!Current.IsPunct(";")) result.Init = ParseStatement();
            else Accept(";");
            if (!Current.IsPunct(";")) result.Condition = ParseExpression();
            Accept(";");
            if (!Current.IsPunct(")")) result.Step = ParseExpression();
            Expect(")");
            result.Body = ParseStatement();
            return result;
        }

        if (Current.IsIdent("while"))
        {
            Advance();
            Expect("(");
            HlslExpression condition = ParseExpression();
            Expect(")");
            return new HlslWhile { Line = line, Condition = condition, Body = ParseStatement() };
        }

        if (Current.IsIdent("do"))
        {
            Advance();
            HlslBlock body = ParseStatement() as HlslBlock ?? new HlslBlock { Line = line };
            if (!Current.IsIdent("while")) throw Error("esperava 'while' depois do bloco do");
            Advance();
            Expect("(");
            HlslExpression condition = ParseExpression();
            Expect(")");
            Accept(";");
            return new HlslDoWhile { Line = line, Body = body, Condition = condition };
        }

        // declaracao local?
        if (LooksLikeDeclaration(out string typeName))
        {
            SkipStorageModifiersInStatement();
            typeName = ParseTypeName();
            HlslDeclStatement declaration = new() { Type = typeName, Line = line };
            while (true)
            {
                string name = ExpectIdentifier();
                ParseVariableRestInto(declaration.Variables, name);
                if (Accept(",")) continue;
                break;
            }
            Accept(";");
            return declaration;
        }

        HlslExprStatement statement = new() { Expression = ParseExpression(), Line = line };
        Accept(";");
        return statement;
    }

    void SkipStorageModifiersInStatement()
    {
        while (Current.Kind == HlslTokenKind.Identifier && Current.Text is "static" or "const" or "uniform" or "extern")
        {
            Advance();
        }
    }

    void ParseVariableRestInto(List<HlslVariable> list, string name)
    {
        HlslVariable variable = new() { Name = name };
        if (Accept("["))
        {
            variable.ArraySize = (int)ExpectInt();
            Expect("]");
        }
        if (Accept("=")) variable.Initializer = ParseExpression();
        list.Add(variable);
    }

    bool LooksLikeDeclaration(out string typeName)
    {
        typeName = string.Empty;
        HlslToken token = Current;
        if (token.Kind != HlslTokenKind.Identifier) return false;

        string text = token.Text;
        if (text is "static" or "const" or "uniform" or "extern")
        {
            typeName = text;
            return true;
        }

        if (!HlslTypes.IsTypeName(text) && !IsKnownStruct(text)) return false;

        // `tipo nome` ou `tipo nome [` — se o proximo for `(`, e' chamada/construtor
        HlslToken next = Peek(1);
        if (next.Kind != HlslTokenKind.Identifier) return false;
        return true;
    }

    readonly HashSet<string> structNames = new(StringComparer.Ordinal);

    bool IsKnownStruct(string name) => structNames.Contains(name);

    public void RegisterStructNames(IEnumerable<string> names)
    {
        foreach (string name in names) structNames.Add(name);
    }

    // --- expressoes ----------------------------------------------------------------------------

    public HlslExpression ParseExpression() => ParseTernary();

    HlslExpression ParseTernary()
    {
        HlslExpression condition = ParseBinary(0);
        if (Accept("?"))
        {
            HlslExpression whenTrue = ParseExpression();
            Expect(":");
            HlslExpression whenFalse = ParseExpression();
            return new HlslTernary { Line = condition.Line, Condition = condition, WhenTrue = whenTrue, WhenFalse = whenFalse };
        }
        return condition;
    }

    HlslExpression ParseBinary(int level)
    {
        if (level >= Levels.Length) return ParseUnary();
        HlslExpression left = ParseBinary(level + 1);
        while (true)
        {
            string op = Current.Text;
            if (Current.Kind != HlslTokenKind.Punct || !Levels[level].Contains(TrimBar(op))) return left;
            Advance();
            HlslExpression right = ParseBinary(level + 1);
            left = new HlslBinary { Line = left.Line, Op = TrimBar(op), Left = left, Right = right };
        }
    }

    // os niveis "|&" servem so' para dar o `&` binario um lugar na escada
    static string TrimBar(string op) => op.StartsWith('|') && op.Length > 2 ? op.Substring(1) : op;

    HlslExpression ParseUnary()
    {
        int line = Current.Line;
        string text = Current.Text;
        if (Current.Kind == HlslTokenKind.Punct)
        {
            switch (text)
            {
                case "-":
                case "+":
                case "!":
                case "~":
                {
                    Advance();
                    HlslExpression operand = ParseUnary();
                    return new HlslUnary { Line = line, Op = text, Prefix = true, Operand = operand };
                }
                case "++":
                case "--":
                {
                    Advance();
                    HlslExpression operand = ParseUnary();
                    return new HlslUnary { Line = line, Op = text, Prefix = true, Operand = operand };
                }
                case "(":
                {
                    // cast?
                    if (Peek(1).Kind == HlslTokenKind.Identifier && HlslTypes.IsTypeName(Peek(1).Text) && Peek(2).IsPunct(")"))
                    {
                        string type = Peek(1).Text;
                        Advance();
                        Advance();
                        Advance();
                        HlslExpression operand = ParseUnary();
                        if (IsConstructorType(type))
                        {
                            return new HlslConstructor { Line = line, Type = type, Args = { operand } };
                        }
                        return new HlslCast { Line = line, Type = type, Operand = operand };
                    }
                    if (Peek(1).Kind == HlslTokenKind.Identifier && IsKnownStruct(Peek(1).Text) && Peek(2).IsPunct(")"))
                    {
                        string type = Peek(1).Text;
                        Advance();
                        Advance();
                        Advance();
                        return new HlslCast { Line = line, Type = type, Operand = ParseUnary() };
                    }
                    break;
                }
            }
        }

        return ParsePostfix();
    }

    static bool IsConstructorType(string type) => string.Equals(type, "float", StringComparison.Ordinal) && false;

    HlslExpression ParsePostfix()
    {
        HlslExpression expression = ParsePrimary();
        while (true)
        {
            if (Accept("."))
            {
                string member = ExpectIdentifier();
                expression = new HlslMember { Line = expression.Line, Target = expression, Member = member };
                continue;
            }
            if (Accept("["))
            {
                HlslExpression index = ParseExpression();
                Expect("]");
                expression = new HlslIndex { Line = expression.Line, Target = expression, IndexExpr = index };
                continue;
            }
            if (Accept("(") && expression is HlslName name)
            {
                HlslCall call = new() { Line = expression.Line, Target = name.Text };
                if (!Accept(")"))
                {
                    while (true)
                    {
                        call.Args.Add(ParseExpression());
                        if (Accept(",")) continue;
                        Expect(")");
                        break;
                    }
                }
                expression = call;
                continue;
            }
            if (Current.IsPunct("++") || Current.IsPunct("--"))
            {
                string op = Advance().Text;
                expression = new HlslUnary { Line = expression.Line, Op = op, Prefix = false, Operand = expression };
                continue;
            }
            break;
        }
        return expression;
    }

    HlslExpression ParsePrimary()
    {
        int line = Current.Line;
        if (Current.Kind == HlslTokenKind.Number)
        {
            string text = Advance().Text;
            bool isFloat = HlslTypes.IsFloatLiteral(text);
            object value;
            if (isFloat)
            {
                float.Parse(text.TrimEnd('f', 'F', 'h', 'H'), System.Globalization.CultureInfo.InvariantCulture);
                value = 0f;
            }
            else
            {
                value = 0;
            }
            return new HlslLiteral { Line = line, Text = text, IsFloatLiteral = isFloat, Value = value };
        }

        if (Current.Kind == HlslTokenKind.Identifier)
        {
            string text = Advance().Text;

            // construtor: float4(...), float2(...), bool(...)
            if (HlslTypes.IsConstructorName(text) && Accept("("))
            {
                HlslConstructor constructor = new() { Line = line, Type = text };
                if (!Accept(")"))
                {
                    while (true)
                    {
                        constructor.Args.Add(ParseExpression());
                        if (Accept(",")) continue;
                        Expect(")");
                        break;
                    }
                }
                return constructor;
            }

            return new HlslName { Line = line, Text = text };
        }

        if (Accept("("))
        {
            HlslExpression inner = ParseExpression();
            Expect(")");
            return inner;
        }

        throw Error("expressao inesperada");
    }

    long ExpectInt()
    {
        if (Current.Kind != HlslTokenKind.Number) throw Error("esperava um numero");
        string text = Advance().Text;
        if (long.TryParse(text.TrimEnd('u', 'U', 'l', 'L'), out long value)) return value;
        return 0;
    }
}
