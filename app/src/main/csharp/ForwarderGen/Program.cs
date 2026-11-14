using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

/// <summary>
/// Gera os TypeForwardedTo para o `tModLoader.dll` de fachada.
///
/// Um `.tmod` compilado no PC tem um AssemblyRef para
/// `tModLoader, Version=1.4.4.9` — o nome do JOGO no tModLoader 1.4.5
/// (veja patches/tModLoader/Terraria/release_extras/tMLMod.targets). No
/// Bunny Loader só existe `BunnyLoader.dll`. Devolver o shim por um evento de
/// resolução NÃO funciona: o runtime valida o nome simples do assembly
/// devolvido (`AssemblyLoadContext.ValidateAssemblyNameWithSimpleName`, o
/// mesmo método que o tML 1.4.5 engancha em Core/AssemblyManager.cs) e recusa
/// `BunnyLoader` quando pediram `tModLoader`.
///
/// A saída correta é um assembly que REALMENTE se chama tModLoader e cujos
/// tipos são todos encaminhados para o shim — assim a identidade dos tipos
/// (`Terraria.ModLoader.Mod`) continua sendo uma só.
/// </summary>
internal static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("uso: ForwarderGen <BunnyLoader.dll> <Forwarders.g.cs>");
            return 2;
        }
        Assembly shim = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Type[] types;
        try
        {
            types = shim.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = Array.FindAll(e.Types, t => t != null);
        }

        var lines = new List<string>
        {
            "// Gerado por ForwarderGen a partir de BunnyLoader.dll — nao editar.",
            "using System.Runtime.CompilerServices;",
        };
        int skipped = 0;
        foreach (Type type in types)
        {
            // So tipos de primeiro nivel: o C# encaminha os aninhados junto
            // com o pai e CS0730 proibe TypeForwardedTo de tipo aninhado.
            if (type.DeclaringType != null) continue;
            string name = type.FullName ?? "";
            if (name.Length == 0 || name.Contains('<') || name.Contains('['))
            {
                skipped++;
                continue;
            }
            int tick = name.IndexOf('`');
            if (tick >= 0)
            {
                if (!int.TryParse(name.Substring(tick + 1), out int arity) || arity < 1)
                {
                    skipped++;
                    continue;
                }
                // Tipo generico aberto: Foo`1 -> Foo<>
                name = name.Substring(0, tick) + "<" + new string(',', arity - 1) + ">";
            }
            lines.Add($"[assembly: TypeForwardedTo(typeof(global::{name}))]");
        }

        string output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllLines(output, lines);
        Console.WriteLine($"ForwarderGen: {lines.Count - 2} tipos encaminhados, {skipped} ignorados -> {output}");
        return 0;
    }
}
