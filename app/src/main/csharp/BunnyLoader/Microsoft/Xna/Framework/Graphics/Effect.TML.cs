using System;
using System.Collections.Generic;

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
    /// Nome do asset que gerou este Effect (<c>Luminance/Assets/.../PixelationShader</code>).
    /// E' a chave que o bridge usa para achar o <c>.fx</c> do mod e traduzir.
    /// Fica nulo no <c>new Effect(GraphicsDevice, byte[])</code> direto.
    /// </summary>
    internal string? AssetName;

    /// <summary>
    /// FNA: <c>Effect(GraphicsDevice, byte[])</c>. O bytecode e ignorado de
    /// proposito — nao ha como executa-lo neste port.
    /// </summary>
    public Effect(GraphicsDevice graphicsDevice, byte[] effectCode)
        : this(0)
    {
    }

    /// <summary>Efeito vazio para o mod escrever parametros sem estourar.</summary>
    internal static Effect CreateManaged() => CreateManaged(null);

    /// <summary>
    /// O mesmo efeito vazio, mas lembrando de qual asset veio. Sem o nome nao
    /// ha como traduzir o <c>.fx</c>: o Luminance so' recebe o <c>Asset&lt;Effect&gt;</c>
    /// e escreve parametros, entao o nome tem de vir junto na criacao.
    /// </summary>
    internal static Effect CreateManaged(string? assetName)
    {
        var effect = new Effect(0);
        effect.AssetName = assetName;
        return effect;
    }

    public EffectParameterCollection Parameters => parameters ??= new EffectParameterCollection(0);

    public EffectTechnique CurrentTechnique => technique ??= CreateTechnique("");

    /// <summary>Colecao ja' criada (sem criar uma vazia so' para ler).</summary>
    internal EffectParameterCollection? ParametersIfAny => parameters;

    internal EffectTechnique CreateTechnique(string name)
    {
        var created = new EffectTechnique(0);
        created.Owner = this;
        created.TechniqueName = name;
        return created;
    }

    public Effect Clone() => CreateManaged(AssetName);

    // v59: um stub ESTAVEL por asset. Antes, cada `Asset<Effect>.Value` criava
    // um Effect novo: o mod escrevia os parametros num objeto descartavel,
    // `Ref<Effect>`/`ManagedShader` ficavam com outro, e no Apply nao havia
    // parametro nenhum para subir para a GPU (o efeito sairia errado mesmo
    // depois de a ponte ser chamada). O Luminance tambem guarda o Ref<Effect>
    // e compara identidade; objeto novo a cada leitura quebra isso.
    static readonly Dictionary<string, Effect> Cached = new(StringComparer.Ordinal);

    internal static Effect CachedOrCreate(string? assetName)
    {
        string key = assetName ?? "";
        lock (Cached)
        {
            if (Cached.TryGetValue(key, out Effect? have)) return have;
            Effect created = CreateManaged(assetName);
            Cached[key] = created;
            return created;
        }
    }
}
