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

        // v54: shader por-vertice (declara matriz de mundo) nao e' filtro de
        // tela. Traduzimos so' o fragmento e desenhamos um quad de tela inteira
        // com blDrawColor branco — para esses, isso nao e' o efeito, e' uma
        // mascara branca por cima de tudo (Luminance: StandardPrimitiveShader,
        // cujo pixel e' `return input.Color`). Melhor nao desenhar nada: o
        // mundo continua visivel e a falta fica visivel no log.
        if (binding.Program.NeedsGeometry)
        {
            Warn(key + "#geom", $"shader {asset}: e' primitivo (por-vertice) — {binding.Program.GeometryReason}; "
                + "nao da' para desenhar como filtro de tela inteira, entao nao desenha");
            PrimeVertex(binding.Program, asset, owner!);
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

    static readonly Dictionary<string, int> primPrograms = new(StringComparer.Ordinal);

    /// <summary>
    /// v55: um shader por-vertice nao vira filtro de tela: o programa dele
    /// (VS do tradutor + FS do mod) e' montado uma vez e armado no Apply, com a
    /// matriz de mundo que o mod escreveu. No desenho, o nativo troca o
    /// programa do jogo pelo nosso e desenha a geometria que o mod mandou
    /// (mesmo vertex buffer, mesmos slots de atributo).
    /// </summary>
    static unsafe void PrimeVertex(FxProgram program, string asset, Effect effect)
    {
        if (program.VertexGlsl.Length == 0) return;
        int handle;
        lock (Gate) primPrograms.TryGetValue(asset, out handle);

        if (handle == 0)
        {
            byte* vs = Host.Utf8(program.VertexGlsl);
            byte* fs = Host.Utf8(program.Glsl);
            byte* report = stackalloc byte[1024];
            report[0] = 0;
            try
            {
                handle = Host.Native()->PrimBuild(vs, fs, report, 1024);
                Log.Info($"[fx] vertice {asset}: {new string((sbyte*)report)}");
            }
            catch (Exception e)
            {
                Log.Info($"[fx] vertice {asset}: build falhou {e.GetType().Name} {e.Message}");
                return;
            }
            finally
            {
                Host.FreeUtf8(vs);
                Host.FreeUtf8(fs);
            }
            if (handle == 0) return;
            lock (Gate) primPrograms[asset] = handle;
        }

        if (!TryWorldMatrix(effect, program, out Matrix mvp)) return;
        float* m = stackalloc float[16];
        m[0] = mvp.M11; m[1] = mvp.M12; m[2] = mvp.M13; m[3] = mvp.M14;
        m[4] = mvp.M21; m[5] = mvp.M22; m[6] = mvp.M23; m[7] = mvp.M24;
        m[8] = mvp.M31; m[9] = mvp.M32; m[10] = mvp.M33; m[11] = mvp.M34;
        m[12] = mvp.M41; m[13] = mvp.M42; m[14] = mvp.M43; m[15] = mvp.M44;
        try
        {
            int host = Host.Native()->PrimBind(handle, m);
            Warn(asset + "#prim", $"shader {asset}: primitivo montado p={handle}, armado sobre o programa {host}");
        }
        catch (Exception e)
        {
            Log.Info($"[fx] vertice {asset}: bind falhou {e.GetType().Name} {e.Message}");
        }
    }

    /// <summary>
    /// A matriz que o mod escreveu em <c>uWorldViewProjection</c> (ou no global
    /// de matriz do .fx). Sem ela o quad sairia na escala errada, entao' sem
    /// matriz nao armamos nada.
    /// </summary>
    static bool TryWorldMatrix(Effect effect, FxProgram program, out Matrix value)
    {
        value = Matrix.Identity;
        EffectParameterCollection? parameters = effect.Parameters;
        if (parameters == null) return false;
        // Os globais de matriz que o .fx declara (uWorldViewProjection no
        // Luminance), mais os nomes que o TML usa no mesmo papel.
        string found = null!;
        string[] candidates = { "uWorldViewProjection", "WorldViewProjection", "uMatrixTransform", "uProjection" };
        foreach (FxUniform uniform in program.Uniforms)
            if (uniform.Components == 0)
                foreach (string name in candidates)
                    if (uniform.Name == name) { found = name; break; }
        if (found == null)
            foreach (string name in candidates)
                if (parameters[name]?.Value is Matrix) { found = name; break; }
        if (found == null) return false;
        if (parameters[found]?.Value is Matrix m)
        {
            value = m;
            return true;
        }
        return false;
    }
}
