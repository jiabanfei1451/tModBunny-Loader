using System.Collections.Generic;

namespace BunnyLoader.Hlsl;

/// <summary>
/// A arvore de um `.fx`: o que o <see cref="HlslParser"/> entrega e o
/// <see cref="HlslTranslator"/> consome. Nada aqui e' GLSL ainda — so' a forma
/// do HLSL (tipos nas palavras do original, semantica no campo `Semantic`).
/// </summary>
public sealed class HlslDocument
{
    public readonly List<HlslStruct> Structs = new();
    public readonly List<HlslGlobals> Globals = new();
    public readonly List<HlslFunction> Functions = new();
    public readonly List<HlslTechnique> Techniques = new();
}

public sealed class HlslStruct
{
    public string Name = string.Empty;
    public readonly List<HlslStructField> Fields = new();
}

public sealed class HlslStructField
{
    public string Type = string.Empty;
    public string Name = string.Empty;
    public string? Semantic;
    public int ArraySize;
}

/// <summary>Um grupo de globais da mesma declaração (`float a, b;`).</summary>
public sealed class HlslGlobals
{
    public string Type = string.Empty;
    public readonly List<HlslVariable> Variables = new();
}

public sealed class HlslVariable
{
    public string Name = string.Empty;
    public int ArraySize;
    public int RegisterSlot = -1;
    public string? Semantic;
    public HlslExpression? Initializer;
}

public sealed class HlslFunction
{
    public string ReturnType = string.Empty;
    public string Name = string.Empty;
    public string? ReturnSemantic;
    public readonly List<HlslParameter> Parameters = new();
    public HlslBlock? Body;
}

public sealed class HlslParameter
{
    public string Type = string.Empty;
    public string Name = string.Empty;
    public string? Semantic;
    public bool In;
    public bool Out;
    public bool InOut;
    public int ArraySize;
    public HlslExpression? Default;

    public bool IsInput => !Out || InOut;

    public bool IsOutput => Out || InOut;
}

public sealed class HlslTechnique
{
    public string Name = string.Empty;
    public readonly List<HlslPass> Passes = new();
}

public sealed class HlslPass
{
    public string Name = string.Empty;
    /// <summary>`compile ps_3_0 PixelShaderFunction()` → "PixelShaderFunction".</summary>
    public string? PixelEntry;
    public string? VertexEntry;
    public string? PixelTarget;
    public string? VertexTarget;
    /// <summary>`AlphaBlendEnable = true;` (o valor e' o text bruto; so' alguns nomes importam).</summary>
    public readonly List<(string Name, string Value)> RenderStates = new();
    public string File = string.Empty;
}

// --- expressoes ---------------------------------------------------------------------------------

public abstract class HlslExpression
{
    public int Line;
}

public sealed class HlslLiteral : HlslExpression
{
    public bool IsFloatLiteral;
    public string Text = string.Empty;
    public object? Value;
}

public sealed class HlslName : HlslExpression
{
    public string Text = string.Empty;
}

public sealed class HlslMember : HlslExpression
{
    public HlslExpression Target = null!;
    public string Member = string.Empty;
}

public sealed class HlslIndex : HlslExpression
{
    public HlslExpression Target = null!;
    public HlslExpression IndexExpr = null!;
}

public sealed class HlslCall : HlslExpression
{
    public string Target = string.Empty;
    public readonly List<HlslExpression> Args = new();
}

public sealed class HlslUnary : HlslExpression
{
    public string Op = string.Empty;
    /// <summary>`-`/`+`/`!`/`~` e' prefixo; `++`/`--` tambem.</summary>
    public bool Prefix;
    public HlslExpression Operand = null!;
}

public sealed class HlslBinary : HlslExpression
{
    public string Op = string.Empty;
    public HlslExpression Left = null!;
    public HlslExpression Right = null!;
}

public sealed class HlslTernary : HlslExpression
{
    public HlslExpression Condition = null!;
    public HlslExpression WhenTrue = null!;
    public HlslExpression WhenFalse = null!;
}

public sealed class HlslCast : HlslExpression
{
    public string Type = string.Empty;
    public HlslExpression Operand = null!;
}

/// <summary>`float2(1, 2)` / `(VertexShaderOutput) 0` / `float4(final, a)`.</summary>
public sealed class HlslConstructor : HlslExpression
{
    public string Type = string.Empty;
    public readonly List<HlslExpression> Args = new();
}

// --- instrucoes ---------------------------------------------------------------------------------

public abstract class HlslStatement
{
    public int Line;
}

public sealed class HlslBlock : HlslStatement
{
    public readonly List<HlslStatement> Statements = new();
}

public sealed class HlslDeclStatement : HlslStatement
{
    public string Type = string.Empty;
    public readonly List<HlslVariable> Variables = new();
}

public sealed class HlslExprStatement : HlslStatement
{
    public HlslExpression Expression = null!;
}

public sealed class HlslIf : HlslStatement
{
    public HlslExpression Condition = null!;
    public HlslStatement Then = null!;
    public HlslStatement? Else;
}

public sealed class HlslReturn : HlslStatement
{
    public HlslExpression? Value;
}

public sealed class HlslBreak : HlslStatement;

public sealed class HlslContinue : HlslStatement;

public sealed class HlslDiscard : HlslStatement;

public sealed class HlslFor : HlslStatement
{
    public HlslStatement? Init;
    public HlslExpression? Condition;
    public HlslExpression? Step;
    public HlslStatement Body = null!;
}

public sealed class HlslWhile : HlslStatement
{
    public HlslExpression Condition = null!;
    public HlslStatement Body = null!;
}

public sealed class HlslDoWhile : HlslStatement
{
    public HlslStatement Body = null!;
    public HlslExpression Condition = null!;
}
