using System;
using System.Collections.Generic;
using BunnyLoader;
using BunnyLoader.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.DataStructures;

namespace Terraria.Graphics.Shaders;

/// <summary>
/// Ponte entre <see cref="MiscShaderData"/> (API de shader do tModLoader) e a
/// pipeline GLES do loader (<c>screenShader</c>/<c>screenSubmit</c>).
///
/// O caminho da esquerda para a direita:
///   1. o mod monta o shader (<c>MiscShaderData(asset, passName)</c>) e escreve
///      parametros no <see cref="Effect"/> gerenciado (uColor, uOpacity, uImage0...);
///   2. <c>Apply</c> chega aqui com a chave do asset e o <see cref="DrawData"/>;
///   3. <see cref="FxRegistry"/> acha o <c>.fx</c> do mod, traduz para GLSL ES
///      (<see cref="FxTranslator"/>) e registra o programa no <c>ShaderRegistry</c> nativo;
///   4. este arquivo monta um <see cref="ScreenDraw"/> (programa, caixa na tela,
///      uniforms e texturas) e o <see cref="MiscShaderFrame"/> entrega o quadro
///      inteiro ao <c>screenSubmit</c> no fim de <c>Main.DoDraw</c>.
///
/// Limitacoes conhecidas (documentadas no handoff):
///   - a geometria do sprite nao existe aqui: o efeito desenha um quad no
///     retangulo do DrawData, nao o sprite recortado;
///   - uniforms do tipo Matrix/array e parametros de textura sem nome de asset
///     nao sobem para o GL;
///   - samplers sem textura do mod (caso classico de ArmorShaderData, onde o
///     uImage0 e a textura do jogador) leem a tela.
/// </summary>
public static class GlesShaderBridge
{
    sealed class Entry
    {
        internal string Key = "";

        internal string Pass = "";

        internal FxShaderBinding? Binding;

        internal bool Warned;

        internal int Submitted;

        internal int Skipped;
    }

    static readonly object Gate = new();

    static readonly Dictionary<string, Entry> Registered = new(StringComparer.Ordinal);

    static readonly HashSet<string> ReportedMissing = new(StringComparer.Ordinal);

    static bool frameInstalled;

    static string Id(string key, string? pass) => key + "|" + (pass ?? "");

    /// <summary>Quantos shaders de mod ja' foram vistos.</summary>
    public static int Count
    {
        get
        {
            lock (Gate) return Registered.Count;
        }
    }

    /// <summary>Quantos desenhos o bridge ja' entregou a' pipeline (diagnostico).</summary>
    public static int Draws
    {
        get
        {
            lock (Gate)
            {
                int total = 0;
                foreach (Entry entry in Registered.Values) total += entry.Submitted;
                return total;
            }
        }
    }

    internal static void Register(MiscShaderData data, string? key, string? technique)
    {
        if (string.IsNullOrEmpty(key))
        {
            Missing(technique ?? "?");
            return;
        }

        EnsureFrame();
        lock (Gate)
        {
            string id = Id(key!, technique);
            if (Registered.ContainsKey(id)) return;
            Registered[id] = new Entry { Key = key!, Pass = technique ?? string.Empty };
        }
    }

    /// <summary>
    /// Chamado a cada <c>Apply</c>: monta o desenho deste shader para o quadro.
    /// O programa GLSL e' traduzido no primeiro apply (a leitura do .fx custa
    /// uma vez por asset) e depois so' os uniforms mudam.
    /// </summary>
    internal static void Submit(MiscShaderData data, string key, string? technique, DrawData? drawData)
    {
        EnsureFrame();

        Entry entry;
        lock (Gate)
        {
            string id = Id(key, technique);
            if (!Registered.TryGetValue(id, out entry!))
                Registered[id] = entry = new Entry { Key = key, Pass = technique ?? string.Empty };
        }

        FxShaderBinding? binding = entry.Binding ??= FxRegistry.Find(key, technique);
        if (binding == null || !binding.Ready)
        {
            if (!entry.Warned)
            {
                entry.Warned = true;
                Log.Warn($"shader {key}{(string.IsNullOrEmpty(technique) ? "" : "#" + technique)}: "
                    + $"sem programa GLSL ({(binding == null || binding.Error.Length == 0 ? "asset nao encontrado" : binding.Error)}); o efeito nao desenha");
            }
            entry.Skipped++;
            return;
        }

        try
        {
            ScreenDraw draw = Build(data, binding, drawData);
            MiscShaderFrame.Add(draw);
            entry.Submitted++;
        }
        catch (Exception e)
        {
            if (!entry.Warned)
            {
                entry.Warned = true;
                Log.Warn($"shader {key}: falhou ao montar o desenho: {e.GetType().Name}: {e.Message}");
            }
            entry.Skipped++;
        }
    }

    internal static void Missing(string key)
    {
        lock (Gate)
        {
            if (!ReportedMissing.Add(key)) return;
        }

        Log.Warn($"shader {key}: nenhum .fx deste shader foi traduzido para GLSL ES — o efeito nao vai desenhar no Android");
    }

    static void EnsureFrame()
    {
        if (frameInstalled) return;
        lock (Gate)
        {
            if (frameInstalled) return;
            frameInstalled = true;
        }

        try { MiscShaderFrame.Install(); }
        catch (Exception e) { Log.Warn("shader: MiscShaderFrame.Install falhou: " + e.GetType().Name); }
    }

    static ScreenDraw Build(MiscShaderData data, FxShaderBinding binding, DrawData? drawData)
    {
        FxProgram program = binding.Program;
        var draw = new ScreenDraw
        {
            Shader = binding.Id,
            // Com DrawData o efeito pertence ao mundo (desenha antes da UI);
            // sem ele e' um filtro de tela inteira.
            Layer = drawData is null ? ScreenEffects.Layer.Screen : ScreenEffects.Layer.World,
            Source = -1,
        };

        float width = Math.Max(1, Main.screenWidth);
        float height = Math.Max(1, Main.screenHeight);

        Vector4 drawColor = drawData is { } dd ? dd.color.ToVector4() : new Vector4(1f, 1f, 1f, 1f);
        draw.Color = new[] { drawColor.X, drawColor.Y, drawColor.Z, drawColor.W };

        float[] box = { 0f, 0f, 1f, 1f };
        if (drawData is { } sprite)
        {
            float w = Math.Abs(sprite.sourceRect.Width * sprite.scale.X);
            float h = Math.Abs(sprite.sourceRect.Height * sprite.scale.Y);
            float x = sprite.position.X - sprite.origin.X * sprite.scale.X;
            float y = sprite.position.Y - sprite.origin.Y * sprite.scale.Y;
            if (w > 0.5f && h > 0.5f)
            {
                // GL: a origem do viewport e' embaixo; y cresce para cima.
                box[0] = x / width;
                box[1] = 1f - (y + h) / height;
                box[2] = (x + w) / width;
                box[3] = 1f - y / height;
            }
        }

        draw.Box = box;
        draw.Uniforms.Add(("blDrawColor", 4, draw.Color));
        draw.Uniforms.Add(("blUvRect", 4, UvRect(data, program, drawData)));
        draw.Uniforms.Add(("uStretch", 1, new[] { 1f }));

        EffectParameterCollection parameters = data.Shader.Parameters;
        foreach (FxUniform uniform in program.Uniforms) AddUniform(parameters, uniform, draw);
        foreach (FxUniform uniform in program.Booleans) AddUniform(parameters, uniform, draw);

        foreach (FxSampler sampler in program.Samplers)
        {
            int texture = SamplerTexture(data, program, sampler);
            draw.Textures.Add((sampler.Glsl, texture));
        }

        return draw;
    }

    // Onde o quad desenhado cai dentro da textura amostrada. Sem isso
    // `coords.y * uImageSize0.y` (armaduras) leria o quadro errado do sheet.
    static float[] UvRect(MiscShaderData data, FxProgram program, DrawData? drawData)
    {
        float[] identity = { 0f, 0f, 1f, 1f };
        if (drawData is not { } dd) return identity;

        EffectParameterCollection parameters = data.Shader.Parameters;
        object? source = parameters["uSourceRect"].Value;
        object? image = parameters["uImageSize0"].Value;
        if (source is Vector4 rect && image is Vector2 sheet && sheet.X > 0f && sheet.Y > 0f)
            return new[] { rect.X / sheet.X, rect.Y / sheet.Y, rect.Z / sheet.X, rect.W / sheet.Y };

        Texture2D? texture = dd.texture;
        if (texture != null && texture.Width > 0 && texture.Height > 0 && dd.sourceRect.Width > 0 && dd.sourceRect.Height > 0)
        {
            return new[]
            {
                dd.sourceRect.X / (float)texture.Width,
                dd.sourceRect.Y / (float)texture.Height,
                dd.sourceRect.Width / (float)texture.Width,
                dd.sourceRect.Height / (float)texture.Height,
            };
        }

        return identity;
    }

    static int SamplerTexture(MiscShaderData data, FxProgram program, FxSampler sampler)
    {
        int image = sampler.Register >= 0 && sampler.Register <= 2 ? sampler.Register : -1;
        if (image < 0 && sampler.Name.Length > 6 && sampler.Name.StartsWith("uImage", StringComparison.Ordinal))
        {
            int.TryParse(sampler.Name[6..], out image);
        }

        if (image >= 0 && image < data.ImagePaths.Length)
        {
            string? path = data.ImagePaths[image];
            if (path != null)
            {
                int id = FxRegistry.Texture(path);
                if (id != 0) return id;
            }
        }

        // Sem textura do mod: le' a tela (distorcao/overlay) em vez de preto.
        return -1;
    }

    static void AddUniform(EffectParameterCollection parameters, FxUniform uniform, ScreenDraw draw)
    {
        if (uniform.Components <= 0) return;
        object? value = parameters[uniform.Name].Value;
        if (value == null) return;

        var values = new List<float>(4);
        if (!Pack(value, uniform.Components, values)) return;
        if (values.Count == 0 || values.Count > 256) return;
        draw.Uniforms.Add((uniform.Glsl, uniform.Components, values.ToArray()));
    }

    /// <summary>
    /// Converte o valor guardado por <c>SetValue</c> no par (componentes, floats)
    /// que o protocolo do nativo espera. Usado aqui e pelo
    /// <see cref="EffectShaderBridge"/> (caminho do <c>Effect</c> do Luminance).
    /// </summary>
    internal static bool Pack(object? value, int components, List<float> into)
    {
        switch (value)
        {
            case float f when components == 1:
                into.Add(f);
                return true;
            case bool b when components == 1:
                into.Add(b ? 1f : 0f);
                return true;
            case int i when components == 1:
                into.Add(i);
                return true;
            case Vector2 v when components == 2:
                into.Add(v.X);
                into.Add(v.Y);
                return true;
            case Vector3 v when components == 3:
                into.Add(v.X);
                into.Add(v.Y);
                into.Add(v.Z);
                return true;
            case Vector4 v when components == 4:
                into.Add(v.X);
                into.Add(v.Y);
                into.Add(v.Z);
                into.Add(v.W);
                return true;
            case Color c when components == 4:
                Vector4 colour = c.ToVector4();
                into.Add(colour.X);
                into.Add(colour.Y);
                into.Add(colour.Z);
                into.Add(colour.W);
                return true;
            case float[] array:
                foreach (float f in array) into.Add(f);
                return into.Count % components == 0;
            default:
                return false;
        }
    }
}
