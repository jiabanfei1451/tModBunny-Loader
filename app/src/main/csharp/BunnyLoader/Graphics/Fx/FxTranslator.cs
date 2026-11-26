using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BunnyLoader.Graphics;

// HLSL (.fx) -> GLSL ES 3.00 (fragment only) translator.
//
// Why fragment only: the Android port draws every screen shader with our own
// full screen quad (ShaderLibrary::Vertex in the native layer), so the vertex
// shader inside the mod effect is not used at all. Dropping it also removes
// `mul(...)`, `matrix`, `SV_POSITION` outputs and the whole vertex stage from
// the translation surface.
//
// The generated source is handed to Host.Native()->ScreenShader(name, source):
// the native BuildFragment() prepends "#version 300 es", the precision, the
// `vUV`/`fragColor` declarations, the shared uniforms (uScreen, uColor, ...)
// and the <bl/...> include library. So this translator must NOT emit any of
// those; it only emits the effect specific uniforms/structs/functions plus
// `void main()`.
//
// Supported subset (matches the 46 .fx files shipped by Luminance 1.0.14 and
// FargowiltasSouls 1.7.3.9, which is what we need first):
//   - sampler declarations with : register(sN)
//   - struct declarations with semantics
//   - global effect variables (become uniforms, keeping the HLSL name so the
//     mod's Effect.Parameters["name"].SetValue(...) still matches)
//   - helper functions with plain GLSL compatible bodies
//   - if / else, return, block scopes, declarations, assignments
//   - no loops, no arrays, no ternary, no discard, no preprocessor
// Anything outside that subset is reported through Error instead of producing
// broken GLSL.
// One sampler of the effect. The register is what the mod's texture parameters
// land on: `sampler uImage1 : register(s1)` receives the texture the mod set as
// uImage1, whatever the sampler is called (the corpus has custom names such as
// "diagonalNoise" at s1).
public sealed class FxSampler
{
    public string Name = "";

    public string Glsl = "";

    public int Register = -1;
}

// One global of the effect promoted to a uniform. The mod writes the Effect
// parameter with the HLSL name, so `Name` is the lookup key and `Glsl` is what
// the emitted source calls it (they differ for the names the native prelude
// already declares, e.g. uColor/uTime).
public sealed class FxUniform
{
    public string Name = "";

    public string Glsl = "";

    public string Type = "";

    // 1..4 floats; 0 means the bridge cannot upload it (matrix, array, struct).
    public int Components;
}

// One pass of a technique. `Pixel` is empty when the pass has no pixel shader.
public sealed class FxPass
{
    public string Technique = "";

    public string Name = "";

    public string Pixel = "";
}

public sealed class FxProgram
{
    public bool Ok;
    public string? Error;

    // Complete fragment source for ScreenShader().
    public string Glsl = "";

    // Pixel shader entry name in the original .fx.
    public string PixelEntry = "";

    // Samplers in register order, with the emitted identifier for each.
    public readonly List<FxSampler> Samplers = new List<FxSampler>();

    // Globals promoted to uniforms (HLSL name -> emitted identifier).
    public readonly List<FxUniform> Uniforms = new List<FxUniform>();

    // Bool globals: "uniform float <name>_v;" plus "#define <name> (<name>_v > 0.5)",
    // because the native uniform upload only knows glUniformNfv.
    public readonly List<FxUniform> Booleans = new List<FxUniform>();

    // Techniques and passes found in the file, in order.
    public readonly List<FxPass> Passes = new List<FxPass>();

    // Diagnostics: which semantics each pixel input was mapped to.
    public readonly List<string> Inputs = new List<string>();

    // Diagnostics: things the translation had to drop (matrix uniforms, ...).
    public readonly List<string> Notes = new List<string>();

    // v54: o .fx tem estagio de vertice de verdade (matriz de mundo e
    // dados por vertice). Traduzimos so' o fragmento e desenhamos um quad de
    // TELA INTEIRA com a cor do draw — para um shader desses isso nao e' o
    // efeito, e' uma mascara: o Luminance usa StandardPrimitiveShader, cujo
    // pixel e' `return input.Color`, para preencher quads; desenhado de tela
    // inteira com blDrawColor branco vira a tela toda branca (era o
    // "mundo nao desenha"). Entao marcamos e a ponte nao o usa como filtro.
    public bool NeedsGeometry;

    // Por que foi marcado (vai para o log uma vez por shader).
    public string GeometryReason = "";

    public FxUniform? Uniform(string name)
    {
        foreach (FxUniform uniform in Uniforms)
            if (uniform.Name == name) return uniform;
        return null;
    }

    public FxSampler? Sampler(string name)
    {
        foreach (FxSampler sampler in Samplers)
            if (sampler.Name == name) return sampler;
        return null;
    }

    public string SamplerRegister(string name)
    {
        FxSampler? sampler = Sampler(name);
        return sampler == null || sampler.Register < 0 ? "" : "s" + sampler.Register.ToString(CultureInfo.InvariantCulture);
    }
}

public static class FxTranslator
{
    public static FxProgram Translate(string source, string? passName = null)
    {
        var program = new FxProgram();
        try
        {
            var parsed = new Parser(source).Parse(passName);
            program.PixelEntry = parsed.Pixel;
            program.Passes.AddRange(parsed.Passes);
            new Emitter(program, parsed).Emit();
            program.Ok = program.Error == null;
        }
        catch (FxException error)
        {
            program.Error = error.Message;
        }
        catch (Exception error)
        {
            program.Error = error.GetType().Name + ": " + error.Message;
        }

        return program;
    }
}

sealed class FxException : Exception
{
    public FxException(string message) : base(message) { }
}

// ---------------------------------------------------------------- tokenizer

enum TokKind { Ident, Number, Punct, End }

readonly struct Tok
{
    public readonly TokKind Kind;
    public readonly string Text;
    public readonly int Line;

    public Tok(TokKind kind, string text, int line)
    {
        Kind = kind;
        Text = text;
        Line = line;
    }

    public bool Is(string text) => Kind != TokKind.End && string.Equals(Text, text, StringComparison.Ordinal);

    public override string ToString() => Kind + ":" + Text;
}

sealed class Lexer
{
    readonly string text;
    int at;
    int line = 1;

    public Lexer(string text) => this.text = text;

    public List<Tok> Run()
    {
        var list = new List<Tok>();
        while (true)
        {
            Tok tok = Next();
            list.Add(tok);
            if (tok.Kind == TokKind.End) break;
        }

        return list;
    }

    Tok Next()
    {
        SkipTrivia();
        if (at >= text.Length) return new Tok(TokKind.End, "", line);

        char c = text[at];
        if (char.IsDigit(c) || (c == '.' && at + 1 < text.Length && char.IsDigit(text[at + 1])))
            return Number();

        if (char.IsLetter(c) || c == '_') return Ident();

        // two char operators first
        if (at + 1 < text.Length)
        {
            string pair = text.Substring(at, 2);
            switch (pair)
            {
                case "&&": case "||": case "==": case "!=": case "<=": case ">=":
                case "+=": case "-=": case "*=": case "/=": case "%=": case "++": case "--":
                    at += 2;
                    return new Tok(TokKind.Punct, pair, line);
            }
        }

        at++;
        return new Tok(TokKind.Punct, c.ToString(), line);
    }

    Tok Ident()
    {
        int start = at;
        while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '_')) at++;
        return new Tok(TokKind.Ident, text.Substring(start, at - start), line);
    }

    Tok Number()
    {
        int start = at;
        while (at < text.Length && (char.IsDigit(text[at]) || text[at] == '.')) at++;
        // exponent is not needed by the corpus but costs three lines
        if (at < text.Length && (text[at] == 'e' || text[at] == 'E'))
        {
            at++;
            if (at < text.Length && (text[at] == '+' || text[at] == '-')) at++;
            while (at < text.Length && char.IsDigit(text[at])) at++;
        }

        // HLSL literal suffixes (1.0f, 2h) must not reach GLSL: remember where
        // the number ends before eating them.
        int end = at;
        while (at < text.Length && (text[at] == 'f' || text[at] == 'F' || text[at] == 'h' || text[at] == 'H')) at++;
        return new Tok(TokKind.Number, text.Substring(start, end - start), line);
    }

    void SkipTrivia()
    {
        while (at < text.Length)
        {
            char c = text[at];
            if (c == '\n')
            {
                line++;
                at++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                at++;
                continue;
            }

            if (c == '/' && at + 1 < text.Length && text[at + 1] == '/')
            {
                while (at < text.Length && text[at] != '\n') at++;
                continue;
            }

            if (c == '/' && at + 1 < text.Length && text[at + 1] == '*')
            {
                at += 2;
                while (at + 1 < text.Length && !(text[at] == '*' && text[at + 1] == '/'))
                {
                    if (text[at] == '\n') line++;
                    at++;
                }

                at = Math.Min(text.Length, at + 2);
                continue;
            }

            break;
        }
    }
}

// ------------------------------------------------------------------- model

sealed class FxField
{
    public string Type = "";
    public string Name = "";
    public string Semantic = "";
}

sealed class FxParam
{
    public string Type = "";
    public string Name = "";
    public string Semantic = "";
}

sealed class FxFunction
{
    public string Type = "";
    public string Name = "";
    public string ReturnSemantic = "";
    public readonly List<FxParam> Params = new List<FxParam>();
    public Block Body = new Block();
}

sealed class FxGlobal
{
    public string Type = "";
    public string Name = "";
    public bool IsConst;
}

sealed class FxModule
{
    public string Pixel = "";
    public string Vertex = "";
    public readonly Dictionary<string, List<FxField>> Structs = new Dictionary<string, List<FxField>>(StringComparer.Ordinal);
    public readonly List<FxGlobal> Globals = new List<FxGlobal>();
    public readonly List<FxSampler> Samplers = new List<FxSampler>();
    public readonly List<FxPass> Passes = new List<FxPass>();
    public readonly Dictionary<string, FxFunction> Functions = new Dictionary<string, FxFunction>(StringComparer.Ordinal);
}

// -------------------------------------------------------------- expression

abstract class Expr { }

sealed class LitExpr : Expr
{
    public string Text = "";
}

sealed class NameExpr : Expr
{
    public string Id = "";
}

sealed class MemberExpr : Expr
{
    public Expr Target = null!;
    public string Field = "";
}

sealed class CallExpr : Expr
{
    public string Name = "";
    public readonly List<Expr> Args = new List<Expr>();
}

sealed class UnaryExpr : Expr
{
    public string Op = "";
    public Expr Value = null!;
}

sealed class BinaryExpr : Expr
{
    public string Op = "";
    public Expr Left = null!;
    public Expr Right = null!;
}

sealed class CastExpr : Expr
{
    public string Type = "";
    public Expr Value = null!;
}

// --------------------------------------------------------------- statement

abstract class Stmt { }

sealed class Block : Stmt
{
    public readonly List<Stmt> Body = new List<Stmt>();
}

sealed class DeclStmt : Stmt
{
    public string Type = "";
    public readonly List<(string Name, Expr? Init)> Items = new List<(string, Expr?)>();
}

sealed class AssignStmt : Stmt
{
    public Expr Target = null!;
    public string Op = "";
    public Expr Value = null!;
}

sealed class CallStmt : Stmt
{
    public Expr Call = null!;
}

sealed class IfStmt : Stmt
{
    public Expr Cond = null!;
    public Stmt Then = null!;
    public Stmt? Else;
}

sealed class ReturnStmt : Stmt
{
    public Expr? Value;

    // `discard;` in HLSL — a ReturnStmt with no value and this flag.
    public bool Discard;
}

// ------------------------------------------------------------------ parser

sealed class Parser
{
    readonly List<Tok> toks;
    int at;
    FxModule? current;

    public Parser(string source) => toks = new Lexer(source).Run();

    Tok Peek(int offset = 0) => toks[Math.Min(at + offset, toks.Count - 1)];

    Tok Take() => toks[at++];

    bool Accept(string text)
    {
        if (!Peek().Is(text)) return false;
        at++;
        return true;
    }

    void Expect(string text)
    {
        if (!Accept(text)) throw new FxException($"expected '{text}' near '{Peek().Text}' (line {Peek().Line})");
    }

    string Ident()
    {
        Tok tok = Take();
        if (tok.Kind != TokKind.Ident) throw new FxException($"expected identifier near '{tok.Text}' (line {tok.Line})");
        return tok.Text;
    }

    public FxModule Parse(string? passName)
    {
        var module = new FxModule();
        current = module;

        while (Peek().Kind != TokKind.End)
        {
            Tok head = Peek();

            if (head.Is("technique") || head.Is("technique10") || head.Is("technique11"))
            {
                ParseTechnique(module, passName);
                continue;
            }

            if (head.Is("struct"))
            {
                ParseStruct(module);
                continue;
            }

            if (head.Is("sampler") || head.Is("sampler2D") || head.Is("sampler3D") || head.Is("samplerCUBE") || head.Is("SamplerState"))
            {
                ParseSampler(module);
                continue;
            }

            if (head.Is("cbuffer") || head.Is("static"))
            {
                // not used by the current corpus; skip to the matching ';'/'}'
                SkipDeclaration();
                continue;
            }

            ParseTopLevelDeclaration(module);
        }

        if (module.Pixel.Length == 0) throw new FxException("no PixelShader found");
        return module;
    }

    void SkipDeclaration()
    {
        int depth = 0;
        while (Peek().Kind != TokKind.End)
        {
            Tok tok = Take();
            if (tok.Is("{")) depth++;
            else if (tok.Is("}")) depth--;
            else if (tok.Is(";") && depth <= 0) return;
            if (depth < 0) return;
        }
    }

    void ParseStruct(FxModule module)
    {
        Expect("struct");
        string name = Ident();
        Expect("{");
        var fields = new List<FxField>();
        while (!Peek().Is("}") && Peek().Kind != TokKind.End)
        {
            var field = new FxField { Type = Ident(), Name = Ident() };
            if (Accept(":")) field.Semantic = ReadSemantic();
            while (Accept("["))
            {
                Take();
                Expect("]");
            }

            Expect(";");
            fields.Add(field);
        }

        Expect("}");
        Accept(";");
        module.Structs[name] = fields;
    }

    string ReadSemantic()
    {
        string semantic = Ident();
        // SV_Target0 style semantics already come as one identifier; the
        // remaining case is TEXCOORD0 which the lexer keeps whole as well.
        return semantic;
    }

    void ParseSampler(FxModule module)
    {
        Take(); // sampler / sampler2D / sampler3D / samplerCUBE / SamplerState
        string name = Ident();
        int register = -1;
        if (Accept(":"))
        {
            // : register(sN)
            Ident();
            Expect("(");
            string slot = Ident();
            Expect(")");
            if (slot.Length > 1 && (slot[0] == 's' || slot[0] == 'S') && int.TryParse(slot[1..], out int index)) register = index;
        }

        Accept(";");
        // 3D/cube are bound as 2D for now; the register keeps the image pairing.
        module.Samplers.Add(new FxSampler { Name = name, Register = register });
    }

    void ParseTechnique(FxModule module, string? passName)
    {
        Take(); // technique / technique10 / technique11
        string technique = "";
        if (Peek().Kind == TokKind.Ident && !Peek().Is("{")) technique = Take().Text; // technique name
        Expect("{");

        while (!Peek().Is("}") && Peek().Kind != TokKind.End)
        {
            if (Peek().Is("pass"))
            {
                Take();
                string name = Peek().Kind == TokKind.Ident && !Peek().Is("{") ? Take().Text : "";
                Expect("{");
                bool selected = passName == null || string.Equals(name, passName, StringComparison.Ordinal);
                string pixel = "", vertex = "";
                while (!Peek().Is("}") && Peek().Kind != TokKind.End)
                {
                    string what = Ident(); // PixelShader / VertexShader / ...
                    Expect("=");
                    if (Accept("compile"))
                    {
                        string profile = Ident();
                        string entry = Ident();
                        Expect("(");
                        Expect(")");
                        if (profile.StartsWith("ps", StringComparison.Ordinal)) pixel = entry;
                        else if (profile.StartsWith("vs", StringComparison.Ordinal)) vertex = entry;
                    }
                    else
                    {
                        SkipExpressionToSemicolon();
                    }

                    Accept(";");
                    if (Peek().Is("{")) SkipBlock();
                    if (what.Length == 0) break;
                }

                Expect("}");
                module.Passes.Add(new FxPass { Technique = technique, Name = name, Pixel = pixel });
                // The first pass that matches the requested name wins; with no
                // pass name the first pass with a pixel shader does.
                if (selected && module.Pixel.Length == 0 && pixel.Length > 0) module.Pixel = pixel;
                if (selected && module.Vertex.Length == 0 && vertex.Length > 0) module.Vertex = vertex;
                continue;
            }

            Take();
        }

        Expect("}");
        Accept(";");
    }

    void SkipBlock()
    {
        Expect("{");
        int depth = 1;
        while (depth > 0 && Peek().Kind != TokKind.End)
        {
            Tok tok = Take();
            if (tok.Is("{")) depth++;
            else if (tok.Is("}")) depth--;
        }
    }

    void SkipExpressionToSemicolon()
    {
        while (Peek().Kind != TokKind.End && !Peek().Is(";") && !Peek().Is("}")) Take();
    }

    void ParseTopLevelDeclaration(FxModule module)
    {
        bool isConst = false;
        while (Peek().Is("uniform") || Peek().Is("extern") || Peek().Is("shared") || Peek().Is("const")
            || Peek().Is("row_major") || Peek().Is("column_major") || Peek().Is("static"))
        {
            if (Peek().Is("const") || Peek().Is("static")) isConst = true;
            Take();
        }

        string type = Ident();
        string name = Ident();

        if (Peek().Is("("))
        {
            var function = new FxFunction { Type = type, Name = name };
            Expect("(");
            if (!Peek().Is(")"))
            {
                do
                {
                    // parameter modifiers (in VertexShaderInput input)
                    while (Peek().Is("in") || Peek().Is("out") || Peek().Is("inout") || Peek().Is("uniform") || Peek().Is("const")) Take();
                    var param = new FxParam { Type = Ident(), Name = Ident() };
                    if (Accept(":")) param.Semantic = ReadSemantic();
                    if (Accept("=")) SkipExpressionToCommaOrParen();
                    function.Params.Add(param);
                } while (Accept(","));
            }

            Expect(")");
            if (Accept(":")) function.ReturnSemantic = ReadSemantic();
            if (Peek().Is("{")) function.Body = ParseBlock();
            else Accept(";"); // prototype
            Accept(";"); // the corpus ends some function definitions with "};"
            module.Functions[name] = function;
            return;
        }

        // global variables, possibly "float a, b = 1;"
        var global = new FxGlobal { Type = type, Name = name, IsConst = isConst };
        module.Globals.Add(global);
        while (Accept(","))
        {
            string more = Ident();
            module.Globals.Add(new FxGlobal { Type = type, Name = more, IsConst = global.IsConst });
            if (Accept("=")) SkipExpressionToCommaOrSemicolon();
        }

        if (Accept("=")) SkipExpressionToSemicolon();
        Accept(";");
    }

    void SkipExpressionToCommaOrSemicolon()
    {
        int depth = 0;
        while (Peek().Kind != TokKind.End)
        {
            Tok tok = Peek();
            if (tok.Is("(") || tok.Is("[")) depth++;
            else if (tok.Is(")") || tok.Is("]")) depth--;
            if (depth <= 0 && (tok.Is(",") || tok.Is(")") || tok.Is(";"))) return;
            Take();
        }
    }

    void SkipExpressionToCommaOrParen()
    {
        int depth = 0;
        while (Peek().Kind != TokKind.End)
        {
            Tok tok = Peek();
            if (tok.Is("(")) depth++;
            else if (tok.Is(")"))
            {
                if (depth == 0) return;
                depth--;
            }

            if (depth == 0 && tok.Is(",")) return;
            Take();
        }
    }

    Block ParseBlock()
    {
        Expect("{");
        var block = new Block();
        while (!Peek().Is("}") && Peek().Kind != TokKind.End) block.Body.Add(ParseStatement());
        Expect("}");
        return block;
    }

    Stmt ParseStatement()
    {
        Tok head = Peek();

        // stray ';' (the corpus has "float4 c = ...;;")
        if (head.Is(";"))
        {
            Take();
            return new Block();
        }

        if (head.Is("{")) return ParseBlock();
        if (head.Is("if"))
        {
            Take();
            Expect("(");
            Expr cond = ParseExpression();
            Expect(")");
            Stmt then = ParseStatement();
            Stmt? otherwise = null;
            if (Accept("else")) otherwise = ParseStatement();
            return new IfStmt { Cond = cond, Then = then, Else = otherwise };
        }

        if (head.Is("return"))
        {
            Take();
            if (Accept(";")) return new ReturnStmt();
            Expr value = ParseExpression();
            Expect(";");
            return new ReturnStmt { Value = value };
        }

        if (head.Is("discard"))
        {
            Take();
            Expect(";");
            return new ReturnStmt { Discard = true };
        }

        // declaration? TYPE name ... — any identifier followed by an identifier
        // is a declaration in this grammar (struct types included).
        if (head.Kind == TokKind.Ident && Peek(1).Kind == TokKind.Ident)
        {
            string type = Take().Text;
            var decl = new DeclStmt { Type = type };
            do
            {
                string name = Ident();
                Expr? init = Accept("=") ? ParseAssignment() : null;
                decl.Items.Add((name, init));
            } while (Accept(","));

            Expect(";");
            return decl;
        }

        Expr expr = ParseExpression();
        if (Peek().Kind == TokKind.Punct && IsAssignOp(Peek().Text))
        {
            string op = Take().Text;
            Expr value = ParseExpression();
            Expect(";");
            return new AssignStmt { Target = expr, Op = op, Value = value };
        }

        Expect(";");
        return new CallStmt { Call = expr };
    }

    static bool IsAssignOp(string text) => text is "=" or "+=" or "-=" or "*=" or "/=" or "%=";
    static bool IsTypeName(string text) =>
        text is "float" or "float1" or "float2" or "float3" or "float4" or "float2x2" or "float3x3" or "float4x4"
            or "half" or "half2" or "half3" or "half4"
            or "int" or "int2" or "int3" or "int4"
            or "uint" or "uint2" or "uint3" or "uint4"
            or "bool" or "bool2" or "bool3" or "bool4"
            or "double" or "double2" or "double3" or "double4"
            or "matrix" or "void";

    // HLSL operators, highest precedence last.
    static int Precedence(string op) => op switch
    {
        "||" => 1,
        "&&" => 2,
        "|" => 3,
        "^" => 4,
        "&" => 5,
        "==" or "!=" => 6,
        "<" or ">" or "<=" or ">=" => 7,
        "<<" or ">>" => 8,
        "+" or "-" => 9,
        "*" or "/" or "%" => 10,
        _ => 0
    };

    Expr ParseExpression() => ParseAssignment();

    Expr ParseAssignment()
    {
        Expr left = ParseBinary(1);
        return left;
    }

    Expr ParseBinary(int minPrecedence)
    {
        Expr left = ParseUnary();
        while (true)
        {
            Tok tok = Peek();
            if (tok.Kind != TokKind.Punct) break;
            int priority = Precedence(tok.Text);
            if (priority == 0 || priority < minPrecedence) break;
            Take();
            Expr right = ParseBinary(priority + 1);
            left = new BinaryExpr { Op = tok.Text, Left = left, Right = right };
        }

        return left;
    }

    Expr ParseUnary()
    {
        Tok tok = Peek();
        if (tok.Is("(") && Peek(1).Kind == TokKind.Ident && Peek(2).Is(")")
            && (IsTypeName(Peek(1).Text) || current?.Structs.ContainsKey(Peek(1).Text) == true))
        {
            Take();
            string castType = Take().Text;
            Expect(")");
            return new CastExpr { Type = castType, Value = ParseUnary() };
        }

        if (tok.Is("-") || tok.Is("!") || tok.Is("+") || tok.Is("~"))
        {
            Take();
            return new UnaryExpr { Op = tok.Text, Value = ParseUnary() };
        }

        return ParsePostfix();
    }

    Expr ParsePostfix()
    {
        Expr expr = ParsePrimary();
        while (true)
        {
            if (Accept("."))
            {
                expr = new MemberExpr { Target = expr, Field = Ident() };
                continue;
            }

            if (Peek().Is("["))
            {
                Throw(new FxException($"array indexing is not supported (line {Peek().Line})"));
            }

            break;
        }

        return expr;
    }

    Expr ParsePrimary()
    {
        Tok tok = Peek();
        if (tok.Is("("))
        {
            Take();
            Expr inner = ParseExpression();
            Expect(")");
            return inner;
        }

        if (tok.Kind == TokKind.Number)
        {
            Take();
            return new LitExpr { Text = tok.Text };
        }

        if (tok.Kind == TokKind.Ident)
        {
            Take();
            if (Accept("("))
            {
                var call = new CallExpr { Name = tok.Text };
                if (!Peek().Is(")"))
                {
                    do
                    {
                        call.Args.Add(ParseAssignment());
                    } while (Accept(","));
                }

                Expect(")");
                return call;
            }

            return new NameExpr { Id = tok.Text };
        }

        throw new FxException($"unexpected '{tok.Text}' (line {tok.Line})");
    }

    void Throw(Exception error) => throw error;
}

// ----------------------------------------------------------------- emitter

sealed class Emitter
{
    static readonly Dictionary<string, string> TypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["float1"] = "float", ["float2"] = "vec2", ["float3"] = "vec3", ["float4"] = "vec4",
        ["half"] = "float", ["half2"] = "vec2", ["half3"] = "vec3", ["half4"] = "vec4",
        ["int2"] = "ivec2", ["int3"] = "ivec3", ["int4"] = "ivec4",
        ["uint2"] = "uvec2", ["uint3"] = "uvec3", ["uint4"] = "uvec4",
        ["bool2"] = "bvec2", ["bool3"] = "bvec3", ["bool4"] = "bvec4",
        ["double"] = "float", ["double2"] = "vec2", ["double3"] = "vec3", ["double4"] = "vec4",
        ["matrix"] = "mat4", ["float2x2"] = "mat2", ["float3x3"] = "mat3", ["float4x4"] = "mat4"
    };

    // Matriz (de mundo/projecao). Um .fx que declara uma precisa da geometria
    // de verdade: nao da' para virar filtro de tela inteira (ver
    // FxProgram.NeedsGeometry).
    static bool IsMatrixType(string type) =>
        type is "matrix" or "float2x2" or "float3x3" or "float4x4"
            or "half2x2" or "half3x3" or "half4x4" or "double2x2" or "double3x3" or "double4x4";

    static readonly HashSet<string> Intrinsics = new HashSet<string>(StringComparer.Ordinal)
    {
        // provided by the native prelude / our macro header
        "tex2D", "lerp", "frac", "saturate", "atan2", "rsqrt", "mad",
        // identical in GLSL
        "sin", "cos", "tan", "asin", "acos", "atan", "pow", "exp", "exp2", "log", "log2", "sqrt",
        "abs", "sign", "floor", "ceil", "round", "trunc", "min", "max", "clamp", "step", "smoothstep",
        "length", "distance", "dot", "cross", "normalize", "reflect", "refract", "faceforward",
        "mod", "fmod", "any", "all", "not", "radians", "degrees", "fract",
        "blScreen", "blSource", "blTex", "blUV", "texture", "textureLod"
    };

    readonly FxProgram program;
    readonly FxModule module;
    readonly StringBuilder body = new StringBuilder();
    readonly HashSet<string> usedGlobals = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> reachable = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> emittedFunctions = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> emittedNames = new HashSet<string>(StringComparer.Ordinal);
    readonly Dictionary<string, string> renames = new Dictionary<string, string>(StringComparer.Ordinal);
    readonly HashSet<string> locals = new HashSet<string>(StringComparer.Ordinal);
    readonly Dictionary<string, FxFunction> functions;
    bool inEntry;

    // Everything the native prelude (graphics/ShaderLibrary.cpp) already
    // declares: emitting a second declaration of any of these is a compile
    // error, so an effect global with one of these names is renamed.
    static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "uScreen", "uSource", "uScreenTexel", "uSourceTexel", "uView", "uResolution",
        "uAspect", "uTime", "uAge", "uProgress", "uIntensity", "uCenter", "uRadius", "uColor",
        "uBox", "uStretch", "vUV", "fragColor", "blDrawColor", "blUvRect",
        "blScreen", "blSource", "blComposite", "gl_FragColor", "gl_FragCoord", "texture", "texture2D"
    };

    static string GlslName(string name) => Reserved.Contains(name) ? name + "_fx" : name;

    static int Components(string type) => type switch
    {
        "float" or "float1" or "half" or "int" or "uint" or "bool" or "double" => 1,
        "float2" or "half2" or "int2" or "uint2" or "bool2" or "double2" => 2,
        "float3" or "half3" or "int3" or "uint3" or "bool3" or "double3" => 3,
        "float4" or "half4" or "int4" or "uint4" or "bool4" or "double4" => 4,
        _ => 0
    };

    public Emitter(FxProgram program, FxModule module)
    {
        this.program = program;
        this.module = module;
        functions = module.Functions;
    }

    public void Emit()
    {
        FxFunction entry = Entry();
        CollectUsed(entry);

        // v54: matriz de mundo declarada (mesmo usada so' no estagio de
        // vertice, que a gente descarta) = shader por-vertice. Ver
        // FxProgram.NeedsGeometry.
        foreach (FxGlobal global in module.Globals)
        {
            if (!IsMatrixType(global.Type) || program.NeedsGeometry) continue;
            program.NeedsGeometry = true;
            program.GeometryReason = global.Name + " (" + global.Type + "): matriz de mundo, shader por-vertice";
        }

        // Globals that collide with the prelude get a renamed uniform; the
        // bridge uploads the mod's parameter (HLSL name) into it.
        foreach (FxGlobal global in module.Globals)
            if (usedGlobals.Contains(global.Name)) renames[global.Name] = GlslName(global.Name);
        foreach (FxSampler sampler in module.Samplers) sampler.Glsl = GlslName(sampler.Name);

        var text = new StringBuilder();
        text.Append("// Translated from HLSL by Bunny Loader (fragment only).\n");
        text.Append("// Entry: ").Append(entry.Name).Append("; the vertex stage is the built in quad.\n");
        text.Append("#define lerp(a,b,t) mix(a,b,t)\n");
        text.Append("#define frac(x) fract(x)\n");
        text.Append("#define saturate(x) clamp(x, 0.0, 1.0)\n");
        text.Append("#define atan2(y,x) atan(y,x)\n");
        text.Append("#define rsqrt(x) inversesqrt(x)\n");
        text.Append("#define mad(a,b,c) ((a)*(b)+(c))\n");
        text.Append("#define tex2D(s,uv) texture(s, uv)\n");
        text.Append("#define tex2Dlod(s,uv) textureLod(s, (uv).xy, (uv).w)\n");
        // HLSL `any`/`all`/`not` são predicados de bool e no GLSL exigem bvec —
        // `any(float)` nem compila. Os efeitos deste corpus só os usam em
        // ARITMÉTICA (`1 - any(a)`), então a versão que devolve 0.0/1.0 é a que
        // preserva o resultado (MetaballEdgeShader do Luminance depende disto).
        text.Append("#define blAny(x) ((x) != 0.0 ? 1.0 : 0.0)\n");
        text.Append("#define blAll(x) ((x) != 0.0 ? 1.0 : 0.0)\n");
        text.Append("#define blNot(x) ((x) == 0.0 ? 1.0 : 0.0)\n");
        text.Append('\n');
        text.Append("// Per draw values the effect cannot know: the quad UV rect inside the\n");
        text.Append("// sampled texture, the sprite colour and the trail stretch.\n");
        text.Append("uniform vec4 blDrawColor;\n");
        text.Append("uniform vec4 blUvRect;\n");
        text.Append("uniform float uStretch;\n");
        text.Append("// The pipeline draws over the screen without GL blending, so alpha < 1 is\n");
        text.Append("// composited against the screen here instead of being thrown away.\n");
        text.Append("vec4 blComposite(vec4 c) {\n");
        text.Append("    float a = clamp(c.a, 0.0, 1.0);\n");
        text.Append("    return vec4(mix(blScreen(vUV).rgb, c.rgb, a), 1.0);\n");
        text.Append("}\n\n");

        // structs used by the entry signature / functions
        foreach (KeyValuePair<string, List<FxField>> entry2 in module.Structs)
        {
            if (!UsesType(entry2.Key)) continue;
            text.Append("struct ").Append(entry2.Key).Append(" {\n");
            foreach (FxField field in entry2.Value)
                text.Append("    ").Append(MapType(field.Type)).Append(' ').Append(field.Name).Append(";\n");
            text.Append("};\n\n");
        }

        // samplers
        foreach (FxSampler sampler in module.Samplers)
        {
            text.Append("uniform sampler2D ").Append(sampler.Glsl).Append(";\n");
            program.Samplers.Add(sampler);
        }

        if (module.Samplers.Count > 0) text.Append('\n');

        // effect globals become uniforms, before the functions that use them
        foreach (FxGlobal global in module.Globals)
        {
            if (!usedGlobals.Contains(global.Name) || global.IsConst) continue;
            // bools have their own path below (float uniform + macro).
            if (global.Type == "bool") continue;
            string glsl = renames.TryGetValue(global.Name, out string? renamed) ? renamed : global.Name;
            if (!emittedNames.Add(glsl)) continue;
            int components = Components(global.Type);
            if (components == 0)
            {
                program.Notes.Add(global.Name + " (" + global.Type + "): sem upload de uniform, fica zerado");
                if (IsMatrixType(global.Type) && !program.NeedsGeometry)
                {
                    program.NeedsGeometry = true;
                    program.GeometryReason = global.Name + " (" + global.Type + "): matriz de mundo, shader por-vertice";
                }
                text.Append("uniform ").Append(MapType(global.Type)).Append(' ').Append(glsl).Append(";\n");
                continue;
            }

            text.Append("uniform ").Append(MapType(global.Type)).Append(' ').Append(glsl).Append(";\n");
            program.Uniforms.Add(new FxUniform { Name = global.Name, Glsl = glsl, Type = global.Type, Components = components });
        }

        // bool globals: a float uniform plus a macro, so the native glUniformNfv
        // path can fill them from anywhere in the shader (helper functions too).
        foreach (FxGlobal global in module.Globals)
        {
            if (!usedGlobals.Contains(global.Name) || global.IsConst || global.Type != "bool") continue;
            string glsl = renames.TryGetValue(global.Name, out string? renamed) ? renamed : global.Name;
            if (!emittedNames.Add(glsl + "_v")) continue;
            text.Append("uniform float ").Append(glsl).Append("_v;\n");
            text.Append("#define ").Append(glsl).Append(" (").Append(glsl).Append("_v > 0.5)\n");
            program.Booleans.Add(new FxUniform { Name = global.Name, Glsl = glsl + "_v", Type = "bool", Components = 1 });
        }

        text.Append('\n');

        // helper functions reachable from the pixel shader
        foreach (FxFunction function in functions.Values)
        {
            if (string.Equals(function.Name, entry.Name, StringComparison.Ordinal)) continue;
            if (reachable.Contains(function.Name)) EmitFunction(text, function);
        }

        // entry point
        text.Append("void main()\n{\n");
        locals.Clear();
        foreach (FxParam param in entry.Params) locals.Add(param.Name);
        bool wrote = EmitInputs(text, entry);
        if (wrote) text.Append('\n');

        var inner = new StringBuilder();
        inEntry = true;
        EmitBlock(inner, entry.Body, 1);
        inEntry = false;
        text.Append(inner.ToString());
        if (!EndsWithReturn(entry.Body))
            text.Append("    fragColor = blComposite(vec4(0.0));\n");
        text.Append("}\n");

        program.Glsl = text.ToString();
    }

    FxFunction Entry()
    {
        if (!functions.TryGetValue(module.Pixel, out FxFunction? entry))
            throw new FxException($"pixel shader '{module.Pixel}' not found");
        return entry;
    }

    static bool EndsWithReturn(Block block) =>
        block.Body.Count > 0 && block.Body[^1] is ReturnStmt;

    bool EmitInputs(StringBuilder text, FxFunction entry)
    {
        bool wrote = false;
        foreach (FxParam param in entry.Params)
        {
            string mapped = MapType(param.Type);
            if (module.Structs.TryGetValue(param.Type, out List<FxField>? fields) && !IsBuiltinVector(param.Type))
            {
                text.Append("    ").Append(param.Type).Append(' ').Append(param.Name).Append(";\n");
                foreach (FxField field in fields)
                {
                    text.Append("    ").Append(param.Name).Append('.').Append(field.Name).Append(" = ")
                        .Append(SemanticValue(field.Semantic, MapType(field.Type), field.Name)).Append(";\n");
                }

                program.Inputs.Add(param.Name + ": struct " + param.Type);
                wrote = true;
                continue;
            }

            text.Append("    ").Append(mapped).Append(' ').Append(param.Name).Append(" = ")
                .Append(SemanticValue(param.Semantic, mapped, param.Name)).Append(";\n");
            program.Inputs.Add(param.Name + ": " + param.Semantic + " -> " + SemanticValue(param.Semantic, mapped, param.Name));
            wrote = true;
        }

        return wrote;
    }

    string SemanticValue(string semantic, string type, string name)
    {
        string upper = semantic.ToUpperInvariant();
        // COLOR0 is the sprite colour the mod drew with, not the shader's
        // uColor parameter (Tanren: in TML sampleColor comes from the vertex).
        if (upper.StartsWith("COLOR", StringComparison.Ordinal)) return type + "(" + "blDrawColor" + ")";
        if (upper.StartsWith("TEXCOORD", StringComparison.Ordinal) || upper.StartsWith("UV", StringComparison.Ordinal))
        {
            // blUvRect maps the drawn quad onto the sampled texture: (0,0,1,1)
            // for a quad that covers the whole texture/screen, the sprite rect
            // inside the sheet for an armor shader.
            string uv = "(blUvRect.xy + vUV * blUvRect.zw)";
            return type switch
            {
                "vec2" => uv,
                "vec3" => "vec3(" + uv + ", uStretch)",
                "vec4" => "vec4(" + uv + ", 0.0, uStretch)",
                "float" => uv + ".x",
                _ => uv
            };
        }

        if (upper is "SV_POSITION" or "VPOS" or "POSITION0" or "POSITION")
            return type switch
            {
                "vec4" => "gl_FragCoord",
                "vec2" => "gl_FragCoord.xy",
                _ => "gl_FragCoord"
            };

        if (upper.StartsWith("SV_TARGET", StringComparison.Ordinal)) return "vec4(0.0)";
        return type == "vec4" ? "vec4(0.0)" : type + "(0.0)";
    }

    void EmitFunction(StringBuilder text, FxFunction function)
    {
        emittedFunctions.Add(function.Name);
        text.Append(MapType(function.Type)).Append(' ').Append(function.Name).Append('(');
        for (int i = 0; i < function.Params.Count; i++)
        {
            if (i > 0) text.Append(", ");
            FxParam param = function.Params[i];
            text.Append(MapType(param.Type)).Append(' ').Append(param.Name);
        }

        text.Append(")\n{\n");
        var inner = new StringBuilder();
        locals.Clear();
        foreach (FxParam param in function.Params) locals.Add(param.Name);
        EmitBlock(inner, function.Body, 1);
        text.Append(inner);
        if (!EndsWithReturn(function.Body) && MapType(function.Type) != "void")
            text.Append("    return ").Append(MapType(function.Type)).Append("(0.0);\n");
        text.Append("}\n\n");
    }

    void EmitBlock(StringBuilder text, Stmt statement, int indent)
    {
        switch (statement)
        {
            case Block block:
                foreach (Stmt inner in block.Body) EmitBlock(text, inner, indent);
                return;
            case DeclStmt decl:
                foreach ((string local, Expr? _) in decl.Items) locals.Add(local);
                text.Append(Pad(indent)).Append(MapType(decl.Type)).Append(' ');
                for (int i = 0; i < decl.Items.Count; i++)
                {
                    if (i > 0) text.Append(", ");
                    text.Append(decl.Items[i].Name);
                    if (decl.Items[i].Init is { } init)
                        text.Append(" = ").Append(Expression(init));
                }

                text.Append(";\n");
                return;
            case AssignStmt assign:
                text.Append(Pad(indent)).Append(Lvalue(assign.Target)).Append(' ').Append(assign.Op).Append(' ')
                    .Append(Expression(assign.Value)).Append(";\n");
                return;
            case CallStmt call:
                text.Append(Pad(indent)).Append(Expression(call.Call)).Append(";\n");
                return;
            case IfStmt branch:
                text.Append(Pad(indent)).Append("if (").Append(Expression(branch.Cond)).Append(")\n");
                EmitScoped(text, branch.Then, indent);
                if (branch.Else is { } otherwise)
                {
                    text.Append(Pad(indent)).Append("else\n");
                    EmitScoped(text, otherwise, indent);
                }

                return;
            case ReturnStmt ret:
                if (ret.Discard)
                {
                    text.Append(Pad(indent)).Append("discard;\n");
                }
                else if (ret.Value == null)
                {
                    text.Append(Pad(indent)).Append("return;\n");
                }
                else if (inEntry)
                {
                    // The entry point writes fragColor and falls out; the value
                    // is composited against the screen (see blComposite).
                    text.Append(Pad(indent)).Append("fragColor = blComposite(").Append(Expression(ret.Value)).Append(");\n");
                    text.Append(Pad(indent)).Append("return;\n");
                }
                else
                {
                    text.Append(Pad(indent)).Append("return ").Append(Expression(ret.Value)).Append(";\n");
                }

                return;
        }
    }

    void EmitScoped(StringBuilder text, Stmt statement, int indent)
    {
        text.Append(Pad(indent)).Append("{\n");
        EmitBlock(text, statement, indent + 1);
        text.Append(Pad(indent)).Append("}\n");
    }

    static string Pad(int indent) => new string(' ', indent * 4);

    string Lvalue(Expr expr) => Expression(expr);

    string Expression(Expr expr)
    {
        switch (expr)
        {
            case LitExpr lit:
                return lit.Text.EndsWith(".", StringComparison.Ordinal) ? lit.Text + "0" : lit.Text;
            case NameExpr name:
                if (module.Globals.Exists(g => g.Name == name.Id)) usedGlobals.Add(name.Id);
                // A local that shadows a renamed global keeps its own name.
                if (locals.Contains(name.Id)) return name.Id;
                return renames.TryGetValue(name.Id, out string? renamed) ? renamed : name.Id;
            case MemberExpr member:
                return Expression(member.Target) + "." + member.Field;
            case CallExpr call:
                return Call(call);
            case UnaryExpr unary:
                return unary.Op + "(" + Expression(unary.Value) + ")";
            case BinaryExpr binary:
                if (binary.Op == "%") return "mod(" + Expression(binary.Left) + ", " + Expression(binary.Right) + ")";
                return "(" + Expression(binary.Left) + " " + binary.Op + " " + Expression(binary.Right) + ")";
            case CastExpr cast:
                return MapType(cast.Type) + "(" + Expression(cast.Value) + ")";
            default:
                throw new FxException("unsupported expression " + expr.GetType().Name);
        }
    }

    string Call(CallExpr call)
    {
        string name = call.Name;

        // type constructors: float4(...) becomes vec4(...)
        if (IsType(name)) return MapType(name) + "(" + Arguments(call) + ")";

        // HLSL: predicados de bool; GLSL: só bvec. Ver as macros do cabeçalho.
        if (name == "any") return "blAny(" + Arguments(call) + ")";
        if (name == "all") return "blAll(" + Arguments(call) + ")";
        if (name == "not") return "blNot(" + Arguments(call) + ")";

        if (!Intrinsics.Contains(name) && !functions.ContainsKey(name))
            throw new FxException($"call to unknown function '{name}'");

        return name + "(" + Arguments(call) + ")";
    }

    string Arguments(CallExpr call)
    {
        var text = new StringBuilder();
        for (int i = 0; i < call.Args.Count; i++)
        {
            if (i > 0) text.Append(", ");
            text.Append(Expression(call.Args[i]));
        }

        return text.ToString();
    }

    static bool IsType(string NameOrNull) =>
        NameOrNull is "float" or "float1" or "float2" or "float3" or "float4" or "half" or "half2" or "half3" or "half4"
            or "int" or "int2" or "int3" or "int4" or "uint" or "uint2" or "uint3" or "uint4"
            or "bool" or "bool2" or "bool3" or "bool4" or "matrix" or "float2x2" or "float3x3" or "float4x4"
            or "double" or "double2" or "double3" or "double4";

    static bool IsBuiltinVector(string type) => type is "float" or "float2" or "float3" or "float4"
        or "half" or "half2" or "half3" or "half4" or "int" or "int2" or "int3" or "int4"
        or "bool" or "bool2" or "bool3" or "bool4" or "uint" or "uint2" or "uint3" or "uint4";

    void CollectUsed(FxFunction entry)
    {
        WalkStatement(entry.Body);
        foreach (FxParam param in entry.Params)
            if (module.Structs.TryGetValue(param.Type, out List<FxField>? fields))
                foreach (FxField field in fields)
                    if (module.Globals.Exists(g => g.Name == field.Name)) usedGlobals.Add(field.Name);
    }

    void WalkStatement(Stmt statement)
    {
        switch (statement)
        {
            case Block block:
                foreach (Stmt inner in block.Body) WalkStatement(inner);
                return;
            case DeclStmt decl:
                foreach ((string _, Expr? init) in decl.Items)
                    if (init != null) WalkExpr(init);
                return;
            case AssignStmt assign:
                WalkExpr(assign.Target);
                WalkExpr(assign.Value);
                return;
            case CallStmt call:
                WalkExpr(call.Call);
                return;
            case IfStmt branch:
                WalkExpr(branch.Cond);
                WalkStatement(branch.Then);
                if (branch.Else != null) WalkStatement(branch.Else);
                return;
            case ReturnStmt ret:
                if (ret.Value != null) WalkExpr(ret.Value);
                return;
        }
    }

    void WalkExpr(Expr expr)
    {
        switch (expr)
        {
            case NameExpr name:
                if (module.Globals.Exists(g => g.Name == name.Id)) usedGlobals.Add(name.Id);
                return;
            case MemberExpr member:
                WalkExpr(member.Target);
                return;
            case CallExpr call:
                foreach (Expr arg in call.Args) WalkExpr(arg);
                if (functions.TryGetValue(call.Name, out FxFunction? user) && reachable.Add(call.Name))
                {
                    WalkStatement(user.Body);
                    ForeachParams(user);
                }

                return;
            case UnaryExpr unary:
                WalkExpr(unary.Value);
                return;
            case BinaryExpr binary:
                WalkExpr(binary.Left);
                WalkExpr(binary.Right);
                return;
            case CastExpr cast:
                WalkExpr(cast.Value);
                return;
        }
    }

    void ForeachParams(FxFunction function)
    {
        foreach (FxParam param in function.Params)
            if (module.Structs.TryGetValue(param.Type, out List<FxField>? fields))
                foreach (FxField field in fields)
                    if (module.Globals.Exists(g => g.Name == field.Name)) usedGlobals.Add(field.Name);
    }

    bool UsesType(string type) => functions.Values.Any(f => f.Type == type)
        || functions.Values.Any(f => f.Params.Exists(p => p.Type == type));

    static string MapType(string type) => TypeNames.TryGetValue(type, out string? mapped) ? mapped : type;
}
