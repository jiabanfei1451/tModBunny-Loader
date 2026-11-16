namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// O contrato mínimo de vértice da FNA.
///
/// Não existia nada disto no shim, e foi o que derrubou o Luminance (e, por
/// tabela, o FargowiltasSouls): `Luminance.Core.Graphics.VertexPosition2DColorTexture`
/// implementa esta interface, então carregar o tipo exige resolver a interface
/// e a assinatura do membro dela. Sem o tipo, `RuntimeTypeHandle.CanCastTo`
/// estoura `TypeLoadException: Could not resolve type with token ... expected
/// class 'IVertexType'`, e como isso acontece dentro de
/// `Mod.Autoload` → `IsAssignableFrom` sobre a lista inteira de tipos, o
/// assembly todo era descartado.
///
/// O lado do desenho não passa por aqui: quem desenha é o jogo (il2cpp), e o
/// `VertexDeclaration` daqui é só o descritor que o mod monta para dizer como
/// os campos do struct estão arrumados. Por isso uma implementação gerenciada
/// pura basta — não há dispositivo gráfico do outro lado desta struct.
public interface IVertexType
{
    VertexDeclaration VertexDeclaration { get; }
}

/// <summary>Espelha <c>VertexElementFormat</c> da FNA, na mesma ordem.</summary>
public enum VertexElementFormat
{
    Single = 0,
    Vector2 = 1,
    Vector3 = 2,
    Vector4 = 3,
    Color = 4,
    Byte4 = 5,
    Short2 = 6,
    Short4 = 7,
    NormalizedShort2 = 8,
    NormalizedShort4 = 9,
    HalfVector2 = 10,
    HalfVector4 = 11,
}

/// <summary>Espelha <c>VertexElementUsage</c> da FNA, na mesma ordem.</summary>
public enum VertexElementUsage
{
    Position = 0,
    Color = 1,
    TextureCoordinate = 2,
    Normal = 3,
    Binormal = 4,
    Tangent = 5,
    BlendIndices = 6,
    BlendWeight = 7,
    Depth = 8,
    Fog = 9,
    PointSize = 10,
    Sample = 11,
    TessellationFactor = 12,
}

public struct VertexElement
{
    public int Offset;
    public VertexElementFormat VertexElementFormat;
    public VertexElementUsage VertexElementUsage;
    public int UsageIndex;

    public VertexElement(int offset, VertexElementFormat elementFormat, VertexElementUsage elementUsage, int usageIndex)
    {
        Offset = offset;
        VertexElementFormat = elementFormat;
        VertexElementUsage = elementUsage;
        UsageIndex = usageIndex;
    }

    public override string ToString() =>
        $"{{Offset:{Offset} Format:{VertexElementFormat} Usage:{VertexElementUsage} UsageIndex:{UsageIndex}}}";
}

public class VertexDeclaration : System.IDisposable
{
    /// <summary>
    /// O tamanho de um vértice, deduzido das declarações quando não vem
    /// explícito — é o que a FNA faz no construtor de array único.
    /// </summary>
    public int VertexStride { get; private set; }

    public string Name { get; set; } = string.Empty;

    readonly VertexElement[] elements;

    public VertexDeclaration(params VertexElement[] elements)
        : this(StrideOf(elements), elements)
    {
    }

    public VertexDeclaration(int vertexStride, params VertexElement[] elements)
    {
        this.elements = elements ?? System.Array.Empty<VertexElement>();
        VertexStride = vertexStride;
    }

    public VertexElement[] GetVertexElements() => (VertexElement[])elements.Clone();

    static int StrideOf(VertexElement[] elements)
    {
        int stride = 0;
        if (elements == null) return 0;
        foreach (VertexElement element in elements)
        {
            int size = SizeOf(element.VertexElementFormat);
            if (element.Offset + size > stride) stride = element.Offset + size;
        }
        return stride;
    }

    static int SizeOf(VertexElementFormat format) => format switch
    {
        VertexElementFormat.Single => 4,
        VertexElementFormat.Vector2 => 8,
        VertexElementFormat.Vector3 => 12,
        VertexElementFormat.Vector4 => 16,
        VertexElementFormat.Color => 4,
        VertexElementFormat.Byte4 => 4,
        VertexElementFormat.Short2 => 4,
        VertexElementFormat.Short4 => 8,
        VertexElementFormat.NormalizedShort2 => 4,
        VertexElementFormat.NormalizedShort4 => 8,
        VertexElementFormat.HalfVector2 => 4,
        VertexElementFormat.HalfVector4 => 8,
        _ => 0,
    };

    public void Dispose()
    {
        // Nada do lado nativo existe para liberar (ver o comentário de IVertexType).
    }
}
