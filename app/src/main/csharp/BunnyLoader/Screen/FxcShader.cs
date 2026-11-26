using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BunnyLoader.Interop;

namespace BunnyLoader.Screen;

/// <summary>
/// Ponte gerenciada para o tradutor .fxc do upstream 0.5.13 (MojoShader, em
/// <c>graphics/FxcEffect.cpp</c>). O <c>Effect</c> desta engine ainda e' um stub
/// (ver Effect.TML.cs), entao este arquivo faz duas coisas e nada mais:
/// carrega o binario do efeito no nativo, monta a lista de passes e puxa o
/// GLSL de cada passagem. Servir de prova de que a traducao funciona com
/// efeitos de mod de verdade; a ligacao com o desenho vem depois.
///
/// Formato do <c>FxcInfo</c> (ver FxcEffect.cpp): uma linha por item, campos
/// separados por TAB -- <c>A</c> parametro, <c>T</c> tecnica, <c>P</c> passagem,
/// <c>S</c> sampler da passagem.
/// </summary>
internal static unsafe class FxcShader
{
    internal sealed class Pass
    {
        internal int Technique;
        internal int Index;
        internal string Name = "";
        internal string TechniqueName = "";
        internal Dictionary<int, string> Samplers = new();
        internal string? Source;
        internal string? Error;
        internal int Registers;
        internal bool Sprite;
        public override string ToString() => $"{TechniqueName}.{Name}";
    }

    internal sealed class Shader
    {
        internal int Id;
        internal string Name = "";
        internal readonly List<Pass> Passes = new();
        internal readonly Dictionary<string, string> Parameters = new();
        internal string Info = "";
    }

    // Quantas linhas ainda escrevem. Sao poucas de proposito: o objetivo e'
    // ver o resultado no primeiro mod que pedir efeito, nao encher o log.
    static int logLeft = 16;

    /// <summary>Le o arquivo e traduz. Nunca lanca: devolve null e loga.</summary>
    internal static Shader? LoadFile(string fullPath, string name, bool forceLog = false)
    {
        try
        {
            byte[] data = File.ReadAllBytes(fullPath);
            return LoadBytes(data, name, forceLog);
        }
        catch (Exception e)
        {
            Log.Warn($"[fxc] {name}: nao deu para ler {fullPath}: {e.GetType().Name} {e.Message}");
            return null;
        }
    }

    internal static Shader? LoadBytes(byte[] data, string name, bool forceLog = false)
    {
        NativeApi* api = Host.Native();
        if (api == null || api->FxcLoad == null)
        {
            Log.Warn("[fxc] tabela nativa sem FxcLoad (libbunny velha?)");
            return null;
        }

        byte* error = stackalloc byte[1024];
        int id;
        fixed (byte* bytes = data)
        {
            id = api->FxcLoad(bytes, data.Length, error, 1024);
        }
        if (id <= 0)
        {
            Log.Warn($"[fxc] {name}: {Ascii(error, 1024)}");
            return null;
        }

        var shader = new Shader { Id = id, Name = name };
        shader.Info = Text(api->FxcInfo, id);

        foreach (string line in shader.Info.Split('\n'))
        {
            if (line.Length < 2) continue;
            string[] parts = line.Split('\t');
            switch (parts[0])
            {
                case "A" when parts.Length >= 7:
                    shader.Parameters[parts[1]] = $"{parts[4]}x{parts[5]}x{parts[6]} t={parts[3]}";
                    break;
                case "T" when parts.Length >= 3:
                    break; // nome da tecnica econto no primeiro P que chegar
                case "P" when parts.Length >= 4:
                {
                    var added = new Pass
                    {
                        Technique = ParseInt(parts[1]),
                        Index = ParseInt(parts[2]),
                        Name = parts[3],
                    };
                    shader.Passes.Add(added);
                    break;
                }
                case "S" when parts.Length >= 5:
                    if (Find(shader, ParseInt(parts[1]), ParseInt(parts[2])) is Pass pass)
                        pass.Samplers[ParseInt(parts[3])] = parts[4];
                    break;
            }
        }

        foreach (Pass pass in shader.Passes)
        {
            byte* source = stackalloc byte[64 * 1024];
            int result = api->FxcPassSource(id, pass.Technique, pass.Index, source, 64 * 1024);
            if (result > 0)
            {
                pass.Source = Ascii(source, Math.Min(result, 64 * 1024));
                pass.Registers = CountRegisters(pass.Source);
            }
            else
            {
                pass.Error = Ascii(source, 64 * 1024);
            }
        }

        if (forceLog || System.Threading.Interlocked.Decrement(ref logLeft) >= 0)
        {
            Log.Info($"[fxc] {name}: {shader.Passes.Count} pass(es), {shader.Parameters.Count} param(s)");
            foreach (Pass pass in shader.Passes)
            {
                if (pass.Source != null)
                    Log.Info($"[fxc]   {pass}: GLSL {pass.Source.Length} B, ~{pass.Registers} registro(s), {pass.Samplers.Count} sampler(s)");
                else
                    Log.Warn($"[fxc]   {pass}: sem GLSL -- {pass.Error}");
            }
        }

        return shader;
    }

    internal static void Free(Shader shader)
    {
        NativeApi* api = Host.Native();
        if (shader.Id > 0 && api != null && api->FxcFree != null) api->FxcFree(shader.Id);
        shader.Id = 0;
    }

    static Pass? Find(Shader shader, int technique, int index)
    {
        foreach (Pass pass in shader.Passes)
            if (pass.Technique == technique && pass.Index == index) return pass;
        return null;
    }

    // Quantos vec4 de uniform o GLSL pede (o nativo recusa acima de 256). So'
    // para o log: e' um numero grosseiro, "uniform" do que aparece no texto.
    static int CountRegisters(string source)
    {
        int count = 0, index = 0;
        while ((index = source.IndexOf("uniform ", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += 8;
        }
        return count;
    }

    static string Text(delegate* unmanaged<int, byte*, int, int> call, int id)
    {
        byte* probe = stackalloc byte[1];
        int size = call(id, probe, 0);          // capacidade 0: so' devolve o tamanho
        if (size <= 1) return "";
        byte* buffer = stackalloc byte[size];
        call(id, buffer, size);
        return Ascii(buffer, size);
    }

    static string Ascii(byte* text, int capacity)
    {
        if (text == null || capacity <= 0) return "";
        int length = 0;
        while (length < capacity && text[length] != 0) length++;
        return Encoding.UTF8.GetString(text, length);
    }

    static int ParseInt(string text) => int.TryParse(text, out int value) ? value : 0;
}
