using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// uso: ildump <dll> <TypeFullName> [<MethodName>]
//      ildump <dll> --typeref <substring>   (lista TypeRefs: namespace/scope/aninhamento)
internal static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("uso: ildump <dll> <TypeFullName> [<MethodName>]");
            Console.Error.WriteLine("     ildump <dll> --typeref <substring>");
            return 2;
        }
        var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { ReadSymbols = false });
        Console.WriteLine($"assembly: {module.Assembly?.Name.FullName}");
        Console.WriteLine($"encaminhados: {module.ExportedTypes.Count(t => t.IsForwarder)}");

        if (args[1] == "--ucocalls")
        {
            // Procura `call`/`callvirt`/`newobj` que apontem para um metodo
            // marcado [UnmanagedCallersOnly]. O Mono do aparelho aceita; o CLR
            // do PC derruba o processo ("attempted to call a UnmanagedCallersOnly
            // method from managed code"). Como isso mata o processo sem stack,
            // achar a chamada na mao e o unico caminho.
            var uco = new System.Collections.Generic.HashSet<MethodDefinition>();
            foreach (var t in module.GetTypes())
                foreach (var m in t.Methods)
                    if (m.CustomAttributes.Any(a => a.AttributeType.Name == "UnmanagedCallersOnlyAttribute"))
                        uco.Add(m);
            Console.WriteLine($"metodos [UnmanagedCallersOnly]: {uco.Count}");

            int found = 0;
            foreach (var t in module.GetTypes())
                foreach (var caller in t.Methods)
                {
                    if (!caller.HasBody) continue;
                    foreach (var ins in caller.Body.Instructions)
                    {
                        if (ins.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj)) continue;
                        if (ins.Operand is not MethodReference mr) continue;
                        MethodDefinition? target;
                        try { target = mr.Resolve(); } catch { continue; }
                        if (target == null || !uco.Contains(target)) continue;
                        Console.WriteLine($"  CHAMADA  {caller.FullName}\n        ->  {ins.OpCode.Code} {mr.FullName}");
                        found++;
                    }
                }
            Console.WriteLine($"total: {found}");
            return 0;
        }

        if (args[1] == "--typeref")
        {
            string filter = args.Length > 2 ? args[2] : "";
            foreach (var t in module.GetTypeReferences())
            {
                if (filter.Length > 0 && t.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                // O que o runtime REALMENTE procura: Namespace + Name, mais o
                // aninhamento (DeclaringType). Um TypeRef com Namespace vazio e
                // Name "orig_X" e um tipo GLOBAL — nao adianta criar o tipo
                // aninhado em On_X se for esse o caso.
                string scope = t.Scope switch
                {
                    AssemblyNameReference a => a.Name,
                    ModuleDefinition m => m.Name,
                    ModuleReference mr => mr.Name,
                    _ => t.Scope?.GetType().Name ?? "?"
                };
                Console.WriteLine(
                    $"{t.FullName,-70} ns='{t.Namespace}' name='{t.Name}' " +
                    $"decl='{t.DeclaringType?.FullName ?? "-"}' nested={t.IsNested} scope='{scope}'");
            }
            return 0;
        }

        if (args[1] == "--calls")
        {
            // Lista todos os pontos de chamada cujo alvo contem `filtro`,
            // com 3 instrucoes de contexto. Serve para responder "quem chama
            // Dictionary<int,Item>.get_Item e de onde vem o receiver".
            string filtro = args.Length > 2 ? args[2] : "";
            int total = 0;
            foreach (var t in module.GetTypes())
                foreach (var m in t.Methods)
                {
                    if (!m.HasBody) continue;
                    var list = m.Body.Instructions;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var ins = list[i];
                        if (ins.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj)) continue;
                        if (ins.Operand is not MethodReference mr) continue;
                        if (filtro.Length > 0 && mr.FullName.IndexOf(filtro, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        Console.WriteLine($"-- {m.FullName}");
                        for (int j = Math.Max(0, i - 3); j < i; j++)
                            Console.WriteLine($"     {list[j]}");
                        Console.WriteLine($"  >  {ins}");
                        total++;
                    }
                }
            Console.WriteLine($"total: {total}");
            return 0;
        }

        string wanted = args[1];
        string method = args.Length > 2 ? args[2] : null;
        foreach (var type in module.GetTypes())
        {
            if (!string.Equals(type.FullName, wanted, StringComparison.Ordinal) &&
                !type.FullName.EndsWith(wanted, StringComparison.Ordinal)) continue;
            Console.WriteLine($"=== {type.FullName} (base {type.BaseType?.FullName}) ===");
            if (method == null)
                foreach (var f in type.Fields)
                    Console.WriteLine($"   campo {f.FieldType.FullName} {f.Name} ({(f.IsStatic ? "static" : "inst")}{(f.IsPublic ? "" : ", nonpublic")})");
            foreach (var m in type.Methods)
            {
                if (method != null && !string.Equals(m.Name, method, StringComparison.Ordinal)) continue;
                if (!m.HasBody) { if (method != null) Console.WriteLine($"-- {m.Name}: sem corpo"); continue; }
                Console.WriteLine($"-- {m.Name} {m.FullName}");
                foreach (var ins in m.Body.Instructions)
                    Console.WriteLine($"   IL_{ins.Offset:X4}: {ins.OpCode} {Format(ins.Operand)}");
                if (method != null)
                {
                    Console.WriteLine("   locais: " + string.Join(", ", m.Body.Variables.Select(v => v.VariableType.FullName)));
                    Console.WriteLine($"   maxstack {m.Body.MaxStackSize} / init {m.Body.InitLocals}");
                }
            }
        }
        return 0;
    }

    static string Format(object operand) => operand switch
    {
        null => "",
        string s => "\"" + s + "\"",
        Instruction i => $"IL_{i.Offset:X4}",
        Instruction[] arr => string.Join(", ", arr.Select(i => $"IL_{i.Offset:X4}")),
        // `inst` = metodo de instancia (precisa do `this` na pilha); `static`
        // = sem `this`. E o que separa IL valido de crash.
        MethodReference m => $"{(m.HasThis ? "[inst]" : "[static]")} {m.FullName}",
        _ => operand.ToString()
    };
}
