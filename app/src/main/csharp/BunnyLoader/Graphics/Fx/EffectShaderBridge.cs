using System;
using System.Collections.Generic;
using BunnyLoader;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Graphics.Shaders;

namespace BunnyLoader.Graphics;

/// <summary>
/// Caminho do <c>Effect</c> — o que o Luminance usa (e o <c>MiscShaderData</c>
/// nao e').
///
/// O Luminance nunca pede um <c>MiscShaderData</c>: ele carrega
/// <c>Asset&lt;Effect&gt;</c> do proprio .tmod, guarda em <c>ManagedShader</code>,
/// escreve os parametros e chama <c>EffectPass.Apply()</c> para depois desenhar
/// com <c>PrimitiveRenderer</code>/<c>SpriteBatch</code>. Como o <c>Effect</code>
/// deste port e' gerenciado (nao ha bytecode D3D9 para executar), o
/// <c>Apply</code> era um no-op e o desenho saia com o shader padrao do jogo —
/// ou seja, nada.
///
/// Aqui o apply vira um <see cref="ScreenDraw"/>: o asset do efeito vira a
/// chave do <c>.fx</c>, o <see cref="FxRegistry"/> traduz uma vez, e os
/// parametros que o mod escreveu viram uniforms GL. A camada e' sempre
/// <c>Screen</code>: um filtro de tela inteira cobre o quadro.
///
/// Limitacoes desta primeira fatia (documentadas no handoff):
///   - so' a parte de TELA INTEIRA. Primitivas (trails/deathrays) precisam de
///     backend de geometria, que ainda nao existe;
///   - sampler sem id de textura do mod le' a tela (o que e' o certo para os
///     filtros que amostram a copia do quadro);
///   - <c>Matrix</c>/array de uniform nao sobe.
/// </summary>
internal static class EffectShaderBridge
{
    static readonly object Gate = new();

    static readonly Dictionary<string, FxShaderBinding?> Bindings = new(StringComparer.Ordinal);

    static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    static int applied;

    static int submitted;

    /// <summary>Quantos <c>Apply</c> de efeito gerenciado ja' chegaram (diagnostico).</summary>
    internal static int Applied
    {
        get
        {
            lock (Gate) return applied;
        }
    }

    /// <summary>Quantos desenhos realmente viraram <see cref="ScreenDraw"/>.</summary>
    internal static int Submitted
    {
        get
        {
            lock (Gate) return submitted;
        }
    }

    /// <summary>
    /// Chamado por <c>EffectPass.Apply()</c>. <paramref name="owner"/> nulo
    /// significa apply vindo de um <c>Effect</c> do proprio jogo (ponteiro):
    /// aquele caminho nao e' nosso.
    /// </summary>
    internal static void Apply(Effect? owner, string? passName)
    {
        string asset = owner?.AssetName ?? "";
        if (asset.Length == 0)
        {
            // v35: so' chega aqui Effect gerenciado (o Apply do jogo sai antes,
            // pelo ponteiro). Nome vazio = o <c>Asset&lt;Effect&gt;</c> nao veio do
            // <c>Asset&lt;Effect&gt;.Value</c> do shim, e sem nome nao ha' .fx para
            // procurar.
            Warn("(sem nome)", "shader: EffectPass.Apply de um Effect gerenciado SEM nome de asset; "
                + "nao da' para achar o .fx (o efeito nao desenha)");
            return;
        }

        lock (Gate) applied++;

        MiscShaderFrame.Install();

        string key = asset + "#" + (passName ?? "");
        FxShaderBinding? binding;
        lock (Gate)
        {
            if (!Bindings.TryGetValue(key, out binding))
            {
                binding = FxRegistry.Find(asset, string.IsNullOrEmpty(passName) ? null : passName);
                Bindings[key] = binding;
            }
        }

        if (binding == null || !binding.Ready)
        {
            Warn(key, $"shader {asset}{(string.IsNullOrEmpty(passName) ? "" : "#" + passName)}: "
                + $"sem programa GLSL ({(binding == null || binding.Error.Length == 0 ? "asset nao encontrado" : binding.Error)}); o efeito nao desenha");
            return;
        }

        try
        {
            ScreenDraw draw = Build(owner!, binding);
            MiscShaderFrame.Add(draw);
            lock (Gate) submitted++;
        }
        catch (Exception e)
        {
            Warn(key, $"shader {asset}: falhou ao montar o desenho: {e.GetType().Name}: {e.Message}");
        }
    }

    static ScreenDraw Build(Effect effect, FxShaderBinding binding)
    {
        FxProgram program = binding.Program;
        var draw = new ScreenDraw
        {
            Shader = binding.Id,
            Layer = ScreenEffects.Layer.Screen,
            Source = -1,
            // Quad de tela inteira: o efeito cobre o quadro, sem recorte.
            Box = new[] { 0f, 0f, 1f, 1f },
            Color = new[] { 1f, 1f, 1f, 1f },
        };

        draw.Uniforms.Add(("blDrawColor", 4, draw.Color));
        draw.Uniforms.Add(("blUvRect", 4, new[] { 0f, 0f, 1f, 1f }));
        draw.Uniforms.Add(("uStretch", 1, new[] { 1f }));

        EffectParameterCollection? parameters = effect.ParametersIfAny;
        if (parameters != null)
        {
            foreach (FxUniform uniform in program.Uniforms) AddUniform(parameters, uniform, draw);
            foreach (FxUniform uniform in program.Booleans) AddUniform(parameters, uniform, draw);
        }

        foreach (FxSampler sampler in program.Samplers)
            draw.Textures.Add((sampler.Glsl, SamplerTexture(parameters, sampler)));

        return draw;
    }

    static void AddUniform(EffectParameterCollection parameters, FxUniform uniform, ScreenDraw draw)
    {
        if (uniform.Components <= 0 || uniform.Name.Length == 0) return;

        object? value = parameters[uniform.Name].Value;
        if (value == null)
        {
            // Sem valor escrito pelo mod o uniform fica no default do GL (0.0),
            // e um 0.0 em divisor (`coords / pixelationFactor`, do
            // PixelationShader) vira NaN e pinta a tela inteira de lixo. 1.0 e'
            // neutro para quase todos os uniforms destes filtros.
            var ones = new float[uniform.Components];
            for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
            draw.Uniforms.Add((uniform.Glsl, uniform.Components, ones));
            return;
        }

        var values = new List<float>(4);
        if (!MiscPack(value, uniform.Components, values)) return;
        if (values.Count == 0 || values.Count > 256) return;
        draw.Uniforms.Add((uniform.Glsl, uniform.Components, values.ToArray()));
    }

    /// <summary>
    /// O <c>Pack</code> da ponte de <c>MiscShaderData</code> e' o mesmo formato
    /// (<c>SetValue</code> tambem guarda <c>object</code>); reusa em vez de
    /// manter duas tabelas que podem divergir.
    /// </summary>
    static bool MiscPack(object? value, int components, List<float> into)
    {
        return GlesShaderBridge.Pack(value, components, into);
    }

    /// <summary>
    /// Textura de um sampler. O Luminance passa <c>Texture2D</c> dos render
    /// targets dele; nao ha como virar id de textura GL deste lado, entao o
    /// sampler le' a tela (que e' justamente a copia do quadro nos filtros).
    /// </summary>
    static int SamplerTexture(EffectParameterCollection? parameters, FxSampler sampler)
    {
        if (parameters == null) return -1;

        object? value = parameters[sampler.Name].Value;

        // Nome de asset (string) — ArmorShaderData usa isso; aqui vale para
        // mods que carregam textura por nome.
        if (value is string path && path.Length > 0)
        {
            int id = FxRegistry.Texture(path);
            if (id != 0) return id;
        }

        return -1;
    }

    static void Warn(string key, string message)
    {
        lock (Gate)
        {
            if (!Reported.Add(key)) return;
        }
        Log.Warn(message);
    }
}
