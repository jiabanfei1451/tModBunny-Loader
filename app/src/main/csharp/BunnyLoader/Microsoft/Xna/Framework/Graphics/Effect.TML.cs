using System;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// FNA: <c>Effect</c>. Este port nao tem hospedeiro de HLSL: o dump do
/// tModLoader traz <c>Effect</c> com apenas <c>Parameters</c>,
/// <c>CurrentTechnique</c>, <c>Clone</c> e <c>OnApply</c> — nao existe
/// <c>Effect(GraphicsDevice, byte[])</c> nem compilador de bytecode D3D9.
/// O Luminance (ShaderManager.LoadShaders) cria o Effect a partir de
/// <c>Asset&lt;Effect&gt;.Value</c> e depois so escreve parametros e chama
/// <c>EffectPass.Apply</c>; sem hospedeiro, a unica saida honesta e um objeto
/// gerenciado que aceita tudo e nao desenha nada.
///
/// Instancias com <c>Pointer != 0</c> nao existem hoje (nada no jogo cria
/// Effect para o mod), por isso todos os membros daqui sao gerenciados.
/// </summary>
public sealed partial class Effect
{
    EffectParameterCollection? parameters;
    EffectTechnique? technique;

    /// <summary>
    /// FNA: <c>Effect(GraphicsDevice, byte[])</c>. O bytecode e ignorado de
    /// proposito — nao ha como executa-lo neste port.
    /// </summary>
    public Effect(GraphicsDevice graphicsDevice, byte[] effectCode)
        : this(0)
    {
    }

    /// <summary>Efeito vazio para o mod escrever parametros sem estourar.</summary>
    internal static Effect CreateManaged() => new(0);

    public EffectParameterCollection Parameters => parameters ??= new EffectParameterCollection(0);

    public EffectTechnique CurrentTechnique => technique ??= new EffectTechnique(0);

    public Effect Clone() => CreateManaged();
}
