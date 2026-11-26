using System;

namespace BunnyLoader.Hlsl;

/// <summary>
/// Os nomes e literais que o parser do HLSL de efeito precisa reconhecer.
/// Fica separado do <see cref="HlslParser"/> porque o lexer tambem consulta
/// (literal float/int) e o emissor reusa a mesma tabela de tipos.
/// </summary>
public static class HlslTypes
{
    // Tipos escalares/vectoriais/matriciais do HLSL que o corpus usa.
    static readonly string[] Names =
    {
        "void", "bool", "int", "uint", "half", "float", "double",
        "bool1", "bool2", "bool3", "bool4",
        "int1", "int2", "int3", "int4",
        "uint1", "uint2", "uint3", "uint4",
        "half1", "half2", "half3", "half4",
        "float1", "float2", "float3", "float4",
        "double1", "double2", "double3", "double4",
        "matrix", "float2x2", "float3x3", "float4x4", "half2x2", "half3x3", "half4x4",
        "sampler", "sampler1D", "sampler2D", "sampler3D", "samplerCUBE",
        "texture", "texture2D", "Texture2D", "SamplerState", "SamplerComparisonState",
        "string",
    };

    public static bool IsTypeName(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (string name in Names)
            if (string.Equals(name, text, StringComparison.Ordinal)) return true;
        return false;
    }

    public static bool IsConstructorName(string? text) => IsTypeName(text) && !string.Equals(text, "void", StringComparison.Ordinal);

    /// <summary>true quando o literal tem parte fracionaria ou expoente.</summary>
    public static bool IsFloatLiteral(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string trimmed = text.TrimEnd('f', 'F', 'h', 'H');
        return trimmed.IndexOf('.') >= 0 || trimmed.IndexOf('e') >= 0 || trimmed.IndexOf('E') >= 0;
    }

    public static bool IsIntLiteral(string text) => !IsFloatLiteral(text);
}
