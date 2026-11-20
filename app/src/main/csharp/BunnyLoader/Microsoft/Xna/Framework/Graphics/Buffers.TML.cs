using System;

namespace Microsoft.Xna.Framework.Graphics;

// Buffers de vértice/índice da FNA.
//
// Faltavam no shim e eram o que faltava para o Luminance sequer carregar:
// `PrimitiveRenderer` tem campos `DynamicVertexBuffer` / `DynamicIndexBuffer`,
// e tipo de campo ausente = TypeLoadException ao carregar a classe, o que
// derruba o assembly inteiro dentro de Mod.Autoload.
//
// Do lado do desenho nada disto é usado: quem desenha é o jogo (il2cpp) e o
// caminho de render do mod acaba passando por métodos que o reescritor de IL
// já coloca em quarentena (faltam membros de GraphicsDevice/Effect). Estas
// classes existem para o TIPO carregar; `SetData` é no-op de propósito.

public enum BufferUsage
{
    None = 0,
    WriteOnly = 1,
}

public enum IndexElementSize
{
    SixteenBits = 0,
    ThirtyTwoBits = 1,
}

public enum PrimitiveType
{
    TriangleList = 0,
    TriangleStrip = 1,
    LineList = 2,
    LineStrip = 3,
    PointList = 4,
}

public enum SetDataOptions
{
    None = 0,
    Discard = 1,
    NoOverwrite = 2,
}

public class VertexBuffer : IDisposable
{
    public GraphicsDevice GraphicsDevice { get; }

    public int VertexCount { get; }

    public BufferUsage BufferUsage { get; }

    public VertexDeclaration VertexDeclaration { get; }

    public VertexBuffer(GraphicsDevice graphicsDevice, VertexDeclaration vertexDeclaration, int vertexCount, BufferUsage bufferUsage)
    {
        GraphicsDevice = graphicsDevice;
        VertexDeclaration = vertexDeclaration;
        VertexCount = vertexCount;
        BufferUsage = bufferUsage;
    }

    public VertexBuffer(GraphicsDevice graphicsDevice, int sizeInBytes, BufferUsage bufferUsage)
        : this(graphicsDevice, null, sizeInBytes, bufferUsage)
    {
    }

    public void SetData<T>(T[] data) where T : struct
    {
    }

    public void SetData<T>(T[] data, int startIndex, int elementCount) where T : struct
    {
    }

    // FNA tem o overload com SetDataOptions; o Luminance usa este (o de 3
    // argumentos nao bastava) e o DynamicVertexBuffer herda daqui.
    public void SetData<T>(T[] data, int startIndex, int elementCount, SetDataOptions options) where T : struct
    {
    }

    public void Dispose()
    {
        // Nada do lado nativo (ver o comentário do topo).
    }
}

public class DynamicVertexBuffer : VertexBuffer
{
    public DynamicVertexBuffer(GraphicsDevice graphicsDevice, VertexDeclaration vertexDeclaration, int vertexCount, BufferUsage bufferUsage)
        : base(graphicsDevice, vertexDeclaration, vertexCount, bufferUsage)
    {
    }

    public DynamicVertexBuffer(GraphicsDevice graphicsDevice, int sizeInBytes, BufferUsage bufferUsage)
        : base(graphicsDevice, sizeInBytes, bufferUsage)
    {
    }
}

public class IndexBuffer : IDisposable
{
    public GraphicsDevice GraphicsDevice { get; }

    public int IndexCount { get; }

    public IndexElementSize IndexElementSize { get; }

    public BufferUsage BufferUsage { get; }

    public IndexBuffer(GraphicsDevice graphicsDevice, IndexElementSize indexElementSize, int indexCount, BufferUsage bufferUsage)
    {
        GraphicsDevice = graphicsDevice;
        IndexElementSize = indexElementSize;
        IndexCount = indexCount;
        BufferUsage = bufferUsage;
    }

    public IndexBuffer(GraphicsDevice graphicsDevice, Type indexType, int indexCount, BufferUsage bufferUsage)
        : this(graphicsDevice, indexType == typeof(short) ? IndexElementSize.SixteenBits : IndexElementSize.ThirtyTwoBits, indexCount, bufferUsage)
    {
    }

    public void SetData<T>(T[] data) where T : struct
    {
    }

    public void SetData<T>(T[] data, int startIndex, int elementCount) where T : struct
    {
    }

    public void SetData<T>(T[] data, int startIndex, int elementCount, SetDataOptions options) where T : struct
    {
    }

    public void Dispose()
    {
    }
}

public class DynamicIndexBuffer : IndexBuffer
{
    public DynamicIndexBuffer(GraphicsDevice graphicsDevice, IndexElementSize indexElementSize, int indexCount, BufferUsage bufferUsage)
        : base(graphicsDevice, indexElementSize, indexCount, bufferUsage)
    {
    }

    public DynamicIndexBuffer(GraphicsDevice graphicsDevice, Type indexType, int indexCount, BufferUsage bufferUsage)
        : base(graphicsDevice, indexType, indexCount, bufferUsage)
    {
    }
}
