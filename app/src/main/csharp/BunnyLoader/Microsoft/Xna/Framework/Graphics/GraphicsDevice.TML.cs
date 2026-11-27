using System;
using BunnyLoader.Interop;

namespace Microsoft.Xna.Framework.Graphics;

public sealed partial class GraphicsDevice
{
    /// <summary>
    /// FNA: colecao de texturas ligadas ao device. O IL dos mods faz
    /// Main.instance.GraphicsDevice.get_Textures() seguido de set_Item/get_Item.
    /// O dump do tModLoader traz GraphicsDevice sem membros, por isso o membro
    /// e escrito a mao (chamada nativa por nome + argc).
    /// </summary>
    public TextureCollection Textures
    {
        get
        {
            if (Pointer == 0 || GraphicsObjectCalls.IsBroken("GraphicsDevice.get_Textures"))
                return TextureCollection.Managed;
            try
            {
                return new TextureCollection(GraphicsObjectCalls.GetZero(Pointer, Class, "get_Textures"));
            }
            catch (Exception e)
            {
                GraphicsObjectCalls.Break("GraphicsDevice.get_Textures", e);
                return TextureCollection.Managed;
            }
        }
    }

    /// <summary>
    /// Leitor dos estados do device que o mod guarda e restaura em volta de um
    /// SpriteBatch.End — <c>BlendState</c>, <c>DepthStencilState</c>,
    /// <c>RasterizerState</c>, <c>SamplerStates</c>. Todos vieram do mesmo lugar:
    /// o <c>tools/modsandbox</c> acusou em sequência
    /// <c>GraphicsDevice.get_BlendState</c>, <c>get_SamplerStates</c> e
    /// <c>get_DepthStencilState</c> como métodos ausentes nos lambdas
    /// <c>&lt;UIWorldListItem_DrawSelf_Shader&gt;b__5_2/b__5_4</c> do FargoChinese
    /// (o dump do Android não tem GraphicsDevice nenhum, então o ProxyGen gerou
    /// a classe vazia e nada disso existe).
    ///
    /// Resolve o nativo por nome + argc (o dump de referência também não dá as
    /// assinaturas), memoriza a primeira falha e devolve <c>null</c>: quem só
    /// salva/restaura o estado antes de desenhar não quebra com null, e um getter
    /// de estado gráfico não é motivo para derrubar o desenho da UI.
    /// </summary>
    T? State<T>(string name, Func<nint, T> wrap) where T : class
    {
        if (Pointer == 0 || GraphicsObjectCalls.IsBroken("GraphicsDevice." + name))
            return null;
        try
        {
            nint pointer = GraphicsObjectCalls.GetZero(Pointer, Class, name);
            return pointer == 0 ? null : wrap(pointer);
        }
        catch (Exception e)
        {
            GraphicsObjectCalls.Break("GraphicsDevice." + name, e);
            return null;
        }
    }

    /// <summary>
    /// FNA: estado de mistura corrente (<c>callvirt get_BlendState()</c>). O
    /// setter é vazio de propósito: nenhum mod escreve nele hoje, e escrever num
    /// campo que não existe não teria efeito nenhum.
    /// </summary>
    public BlendState? BlendState
    {
        get => State("get_BlendState", p => (BlendState)new(p));
        set { }
    }

    /// <summary>FNA: estado de profundidade/estêncil corrente.</summary>
    public DepthStencilState? DepthStencilState
    {
        get => State("get_DepthStencilState", p => (DepthStencilState)new(p));
        set { }
    }

    /// <summary>FNA: estado de rasterização corrente (culling, scissor, viewport).</summary>
    public RasterizerState? RasterizerState
    {
        get => State("get_RasterizerState", p => (RasterizerState)new(p));
        set { }
    }

    /// <summary>
    /// FNA: colecao de sampler states do device. O tipo proxy da coleção existe
    /// (o dump tem SamplerStateCollection, TypeDefIndex 1055), só a instância
    /// vinha do GraphicsDevice, que o dump traz vazio.
    /// </summary>
    public SamplerStateCollection? SamplerStates => State("get_SamplerStates", p => (SamplerStateCollection)new(p));

    /// <summary>
    /// FNA: troca o alvo de renderizacao. O Luminance usa logo depois de pegar o
    /// RenderTarget2D (<c>ManagedRenderTarget.CopyContentsFrom</c> →
    /// <c>SetRenderTarget(target)</c> → <c>Clear</c> → Begin/Draw/End). Resolvido
    /// por nome + argc porque o dump nao traz GraphicsDevice nenhum.
    /// </summary>
    // v37: quantas linhas de log de alvo/clear ainda cabem. E' o rastro que falta
    // para explicar "tela branca so' com o Luminance ligado".
    static int rtProbeLeft = 60;
    // v38: o SetRenderTargets e' chamado TODO quadro pelo Luminance (restaurar
    // a tela). Contador proprio para nao engolir as linhas de Clear/SetRenderTarget.
    static int mrtProbeLeft = 8;

    // v57: se o alvo atual e' a tela (backbuffer) ou um render target do mod.
    // O Luminance aplica os filtros de tela dentro de um alvo (ping-pong
    // MainTarget/AuxiliaryTarget) e o desenho que copia o alvo anterior e' do
    // proprio jogo. Enfileirar um draw nosso de tela inteira nesse momento
    // pinta o quadro por cima no fim do frame (era a "tela branca").
    internal static bool TargetIsScreen = true;

    public void SetRenderTarget(RenderTarget2D? renderTarget)
    {
        TargetIsScreen = renderTarget is null;
        if (System.Threading.Interlocked.Decrement(ref rtProbeLeft) >= 0)
            global::BunnyLoader.Log.Info("[rt] SetRenderTarget(" +
                (renderTarget is null ? "tela" : $"ptr=0x{renderTarget.Pointer:x}") + ")");
        if (Pointer == 0 || GraphicsObjectCalls.IsBroken("GraphicsDevice.SetRenderTarget")) return;
        try
        {
            var (info, fn) = GraphicsObjectCalls.Resolve(Class.Handle, "SetRenderTarget", 1);
            nint target = renderTarget is null ? 0 : renderTarget.Pointer;
            var call = GameCall.Returning(RetKind.Int);
            call.Arg(Pointer);
            call.Arg(target);
            call.Arg(info);
            call.Invoke(fn);
        }
        catch (Exception e)
        {
            GraphicsObjectCalls.Break("GraphicsDevice.SetRenderTarget", e);
        }
    }

    /// <summary>
    /// FNA: <c>Clear(Color)</c> (argc=1; os outros Clear do FNA tem 3 e 4
    /// argumentos). <c>Color</c> e um struct de 4 bytes com layout ABGR, entao vai
    /// por valor no registrador, como o nativo espera.
    /// </summary>
    public void Clear(Color color)
    {
        if (System.Threading.Interlocked.Decrement(ref rtProbeLeft) >= 0)
            global::BunnyLoader.Log.Info($"[rt] Clear(r={color.R} g={color.G} b={color.B} a={color.A})");
        if (Pointer == 0 || GraphicsObjectCalls.IsBroken("GraphicsDevice.Clear")) return;
        try
        {
            var (info, fn) = GraphicsObjectCalls.Resolve(Class.Handle, "Clear", 1);
            var call = GameCall.Returning(RetKind.Int);
            call.Arg(Pointer);
            call.Arg(color);
            call.Arg(info);
            call.Invoke(fn);
        }
        catch (Exception e)
        {
            GraphicsObjectCalls.Break("GraphicsDevice.Clear", e);
        }
    }

    /// <summary>
    /// FNA: <c>GetRenderTargets()</c> (dump: 0xFA8340, argc 0). O Luminance
    /// guarda o resultado em <c>HandleTargetUpdateLoop</c>, troca o alvo com
    /// <c>SetRenderTargets</c> e restaura no fim do laco — sem esta leitura o
    /// primeiro frame com render target do mod morria em MissingMethod.
    /// </summary>
    public RenderTargetBinding[] GetRenderTargets()
    {
        if (Pointer == 0 || GraphicsObjectCalls.IsBroken("GraphicsDevice.GetRenderTargets")) return Array.Empty<RenderTargetBinding>();
        try
        {
            (nint info, nint fn) = GraphicsObjectCalls.Resolve(Class.Handle, "GetRenderTargets", 0);
            var call = GameCall.Returning(RetKind.Int);
            call.Arg(Pointer);
            call.Arg(info);
            nint array = (nint)call.Invoke(fn);
            if (array == 0) return Array.Empty<RenderTargetBinding>();
            // RenderTargetBinding e struct de 16 bytes: no IL2CPP o array
            // guarda os valores embutidos, entao quem le e GameArray<T>.
            return new GameArray<RenderTargetBinding>(array);
        }
        catch (Exception e)
        {
            GraphicsObjectCalls.Break("GraphicsDevice.GetRenderTargets", e);
            return Array.Empty<RenderTargetBinding>();
        }
    }

    /// <summary>
    /// FNA: <c>SetRenderTargets(RenderTargetBinding[])</c>. O dump do Android
    /// nao tem este overload (so <c>SetRenderTarget(RenderTarget2D)</c> e
    /// <c>GetRenderTargets</c>), entao nao ha o que chamar: o metodo existe
    /// para o IL resolver e o estado do device fica como esta. Quem desenha no
    /// alvo logo depois usa <c>SetRenderTarget</c> de qualquer forma.
    /// </summary>
    public void SetRenderTargets(RenderTargetBinding[]? renderTargets)
    {
        // v38: isto NAO e' enfeite. No FNA, `SetRenderTargets(vazio)` significa
        // "volta a desenhar na tela" — e o Luminance chama exatamente isso todo
        // quadro (RenderTargetManager, depois de desenhar nos alvos dele). Como
        // era stub, o device ficava preso no ultimo alvo do mod: o mundo era
        // desenhado num render target que nunca ia para a tela e a tela ficava
        // branca. O sintoma sumia sem o Luminance porque so' ele usa este overload.
        int count = renderTargets?.Length ?? 0;
        if (System.Threading.Interlocked.Decrement(ref mrtProbeLeft) >= 0)
            global::BunnyLoader.Log.Info($"[rt] SetRenderTargets({count})" + (count == 0 ? " -> tela (backbuffer)" : ""));
        if (count == 0)
        {
            SetRenderTarget(null);
            return;
        }
        // O nativo so' tem SetRenderTarget de UM alvo; multi-alvo (MRT) nao
        // existe neste build — usa o primeiro e avisa uma vez.
        nint pointer = renderTargets![0].RenderTarget?.Pointer ?? 0;
        if (count > 1 && System.Threading.Interlocked.Decrement(ref mrtProbeLeft) >= 0)
            global::BunnyLoader.Log.Warn($"[rt] SetRenderTargets com {count} alvos: o nativo so' aceita 1 (usando o primeiro)");
        SetRenderTarget(pointer == 0 ? null : new RenderTarget2D(pointer));
    }

    /// <summary>
    /// FNA: <c>ScissorRectangle</c> (dump: get 0xFA8328 / set 0xFA80B4).
    /// <c>Rectangle</c> tem 16 bytes, entao vai por valor em dois registradores
    /// — igual ao que o ProxyGen gera para os metodos nativos com esse struct.
    /// </summary>
    public unsafe Rectangle ScissorRectangle
    {
        get
        {
            if (Pointer == 0 || GraphicsObjectCalls.IsBroken("GraphicsDevice.get_ScissorRectangle")) return default;
            try
            {
                (nint info, nint fn) = GraphicsObjectCalls.Resolve(Class.Handle, "get_ScissorRectangle", 0);
                CallFrame frame = default;
                frame.X[0] = (long)Pointer;
                frame.X[1] = (long)info;
                frame.Ret = RetKind.S16;
                GameInterop.Call(fn, &frame);
                return *(Rectangle*)frame.Out;
            }
            catch (Exception e)
            {
                GraphicsObjectCalls.Break("GraphicsDevice.get_ScissorRectangle", e);
                return default;
            }
        }
        set
        {
            if (Pointer == 0 || GraphicsObjectCalls.IsBroken("GraphicsDevice.set_ScissorRectangle")) return;
            try
            {
                (nint info, nint fn) = GraphicsObjectCalls.Resolve(Class.Handle, "set_ScissorRectangle", 1);
                CallFrame frame = default;
                frame.X[0] = (long)Pointer;
                *(Rectangle*)&frame.X[1] = value;
                frame.X[3] = (long)info;
                frame.Ret = RetKind.Int;
                GameInterop.Call(fn, &frame);
            }
            catch (Exception e)
            {
                GraphicsObjectCalls.Break("GraphicsDevice.set_ScissorRectangle", e);
            }
        }
    }

    /// <summary>
    /// FNA: <c>SetVertexBuffer</c>. O dump do Android nao tem este membro (a
    /// geometria do port vai por <c>DrawGeometry</c>), e o
    /// <c>PrimitiveRenderer</c> do Luminance so o usa junto com
    /// <c>DrawPrimitives</c>, que tambem e no-op. Guardar o buffer sem desenhar
    /// deixa o mod vivo e o desenho dele invisivel.
    /// </summary>
    public void SetVertexBuffer(VertexBuffer? vertexBuffer)
    {
    }

    /// <summary>FNA: <c>Indices</c> (setter). Mesmo caso de <see cref="SetVertexBuffer"/>.</summary>
    public IndexBuffer? Indices
    {
        get => null;
        set { }
    }

    /// <summary>
    /// FNA: <c>DrawPrimitives</c>. Sem VBO/estado de shader neste port nao ha
    /// como emitir a geometria; o metodo existe para o IL do Luminance
    /// (<c>PrimitiveRenderer.RenderCircleEdge</c>) resolver.
    /// </summary>
    public void DrawPrimitives(PrimitiveType primitiveType, int vertexStart, int primitiveCount)
    {
    }

    /// <summary>FNA: <c>DrawIndexedPrimitives</c> (mesmo caso de <see cref="DrawPrimitives"/>).</summary>
    public void DrawIndexedPrimitives(PrimitiveType primitiveType, int baseVertex, int minVertexIndex, int numVertices, int startIndex, int primitiveCount)
    {
    }
}
