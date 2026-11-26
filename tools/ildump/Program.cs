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

        if (args[1] == "--missing" && args.Length >= 3)
        {
            // uso: ildump <mod.dll> --missing <shim.dll>
            //
            // Lista os TypeRefs deste mod, escopados nos assemblies do jogo/tML,
            // que o shim NAO define. Cada linha e' um TypeLoadException em
            // potencial: o runtime so' descobre na hora de carregar o tipo que
            // usa o membro, e um unico tipo quebrado derruba o assembly inteiro
            // dentro de Mod.Autoload.
            var shim = ModuleDefinition.ReadModule(args[2], new ReaderParameters { ReadSymbols = false });
            var have = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var t in shim.GetTypes()) have.Add(t.FullName.Replace('/', '+'));
            int missing = 0;
            foreach (var t in module.GetTypeReferences())
            {
                string scope = t.Scope switch
                {
                    AssemblyNameReference a => a.Name,
                    _ => ""
                };
                if (scope != "tModLoader" && scope != "Terraria" && scope != "FNA" && scope != "ReLogic") continue;
                string flat = t.FullName.Replace('/', '+');
                if (have.Contains(flat)) continue;
                Console.WriteLine($"{scope,-10} {flat}");
                missing++;
            }
            Console.WriteLine($"faltando: {missing}");
            return 0;
        }

        if (args[1] == "--missing-members" && args.Length >= 3)
        {
            // uso: ildump <mod.dll> --missing-members <shim.dll>
            //
            // O --missing acima so' olha tipos. Um tipo existir no shim nao quer dizer
            // que o MEMBRO existe: mods chamam `new EntitySource_TileUpdate(x, y, "ctx")`
            // e o shim so' tinha o ctor de dois argumentos, o que vira
            // MissingMethodException em tempo de JIT (log v157, dentro de
            // TileLoader.DrawEffects). Este modo compara nome + quantidade de parametros
            // e avisa duas coisas:
            //   SEM    = o nome nao existe no tipo do shim -> estoura sempre
            //   ASSIN  = existe com outro(s) tipo(s) -> pode ser o caso int/float do PC
            var shim = ModuleDefinition.ReadModule(args[2], new ReaderParameters { ReadSymbols = false });
            var types = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<TypeDefinition>>(StringComparer.Ordinal);
            foreach (var t in shim.GetTypes())
            {
                string flat = t.FullName.Replace('/', '+');
                if (!types.TryGetValue(flat, out var list))
                    types[flat] = list = new System.Collections.Generic.List<TypeDefinition>();
                list.Add(t);
            }

            static string Kind(TypeReference t) => t switch
            {
                GenericParameter g => "!" + g.Position,
                ByReferenceType b => Kind(b.ElementType) + "&",
                ArrayType a => Kind(a.ElementType) + "[]",
                GenericInstanceType gi => gi.ElementType.FullName + "<" + string.Join(",", gi.GenericArguments.Select(Kind)) + ">",
                _ => t.FullName
            };

            int noName = 0, diffSig = 0, ok = 0;
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var r in module.GetMemberReferences())
            {
                string scope = r.DeclaringType.Scope switch
                {
                    AssemblyNameReference a => a.Name,
                    _ => ""
                };
                if (scope != "tModLoader" && scope != "Terraria" && scope != "FNA" && scope != "ReLogic") continue;
                string flat = r.DeclaringType.FullName.Replace('/', '+');
                if (!types.TryGetValue(flat, out var candidates)) continue;

                string key = flat + "::" + r.FullName;
                if (!seen.Add(key)) continue;

                int arity = r switch
                {
                    MethodReference m => m.Parameters.Count,
                    _ => -1
                };
                string sig = r switch
                {
                    MethodReference m => "(" + string.Join(",", m.Parameters.Select(p => Kind(p.ParameterType))) + ")",
                    _ => ""
                };

                bool anyName = false, exact = false;
                foreach (var type in candidates)
                {
                    if (r is MethodReference mr)
                    {
                        foreach (var m in type.Methods)
                        {
                            if (m.Name != mr.Name || m.Parameters.Count != arity) continue;
                            anyName = true;
                            if (m.Parameters.Select(p => Kind(p.ParameterType)).SequenceEqual(mr.Parameters.Select(p => Kind(p.ParameterType))))
                                exact = true;
                        }
                    }
                    else if (r is FieldReference fr)
                    {
                        foreach (var f in type.Fields)
                        {
                            if (f.Name != fr.Name) continue;
                            anyName = true;
                            if (Kind(f.FieldType) == Kind(fr.FieldType)) exact = true;
                        }
                        foreach (var p in type.Properties)
                        {
                            if (p.Name != fr.Name) continue;
                            anyName = true;
                            if (p.PropertyType != null && Kind(p.PropertyType) == Kind(fr.FieldType)) exact = true;
                        }
                    }
                }

                if (exact) { ok++; continue; }
                if (anyName)
                {
                    diffSig++;
                    Console.WriteLine($"ASSIN {flat}::{r.Name}{sig}");
                }
                else
                {
                    noName++;
                    Console.WriteLine($"SEM   {flat}::{r.Name}{sig}");
                }
            }
            Console.WriteLine($"ok: {ok}; sem nome: {noName}; assinatura diferente: {diffSig}");
            return 0;
        }

        if (args[1] == "--hard" && args.Length >= 3)
        {
            // uso: ildump <mod.dll> --hard <shim.dll>
            //
            // Dos tipos ausentes, so' os que aparecem em posicao de CARREGAMENTO:
            // tipo-base, interface implementada, tipo de campo (e argumento de
            // generico). Sao esses que fazem GetTypes() estourar
            // TypeLoadException e derrubar o assembly inteiro. Tipo ausente que
            // so' aparece dentro de um corpo de metodo nao bloqueia a carga —
            // aquele metodo e' quarentenado pelo reescritor.
            var shim = ModuleDefinition.ReadModule(args[2], new ReaderParameters { ReadSymbols = false });
            var have = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var t in shim.GetTypes()) have.Add(t.FullName.Replace('/', '+'));
            var seen = new System.Collections.Generic.SortedSet<string>(StringComparer.Ordinal);

            bool Scoped(TypeReference tr) => tr.Scope switch
            {
                AssemblyNameReference a => a.Name is "tModLoader" or "Terraria" or "FNA" or "ReLogic",
                _ => false
            };

            // Devolve o primeiro tipo ausente dentro de `tr` (recursivo em
            // arrays, byref e genericos), ou null.
            string? Probe(TypeReference tr)
            {
                switch (tr)
                {
                    case ArrayType at: return Probe(at.ElementType);
                    case ByReferenceType br: return Probe(br.ElementType);
                    case PointerType pt: return Probe(pt.ElementType);
                    case GenericInstanceType gi:
                        for (int i = 0; i < gi.GenericArguments.Count; i++)
                        {
                            string? inner = Probe(gi.GenericArguments[i]);
                            if (inner != null) return inner;
                        }
                        // Compara a DEFINICAO (`Asset`1`), nao a instancia
                        // (`Asset`1<Texture2D>`): o shim tem a definicao.
                        tr = gi.ElementType;
                        goto default;
                    default:
                        if (!Scoped(tr)) return null;
                        string flat = tr.FullName.Replace('/', '+');
                        return have.Contains(flat) ? null : flat;
                }
            }

            foreach (var type in module.GetTypes())
            {
                string? b = type.BaseType == null ? null : Probe(type.BaseType);
                if (b != null) seen.Add($"{b}\t<- base de {type.FullName}");
                foreach (var iface in type.Interfaces)
                {
                    string? i = Probe(iface.InterfaceType);
                    if (i != null) seen.Add($"{i}\t<- interface de {type.FullName}");
                }
                foreach (var f in type.Fields)
                {
                    string? ft = Probe(f.FieldType);
                    if (ft != null) seen.Add($"{ft}\t<- campo {type.FullName}.{f.Name}");
                }
            }
            foreach (string line in seen) Console.WriteLine(line);
            Console.WriteLine($"bloqueantes: {seen.Count}");
            return 0;
        }

        if (args[1] == "--verify" && args.Length >= 3)
        {
            // uso: ildump <mod.dll> --verify <shim.dll>
            //
            // Confere MEMBRO a MEMBRO se o shim tem o que o mod chama. O
            // `--missing` olha so tipos: um tipo presente com um metodo faltando
            // continua sendo MissingMethodException em tempo de execucao — e
            // descobrir isso so' no aparelho custa um ciclo de build+install.
            var shim = ModuleDefinition.ReadModule(args[2], new ReaderParameters { ReadSymbols = false });
            var types = new System.Collections.Generic.Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
            foreach (var t in shim.GetTypes()) types[t.FullName.Replace('/', '+')] = t;

            static string Norm(TypeReference tr) => tr.FullName.Replace("&", "");

            int missing = 0, checkedCount = 0;
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var mr in module.GetMemberReferences())
            {
                if (mr.DeclaringType == null) continue;
                string typeName = mr.DeclaringType.FullName.Replace('/', '+');
                if (!types.TryGetValue(typeName, out var def)) continue; // tipo ausente: --missing cobre
                if (mr is MethodReference call)
                {
                    checkedCount++;
                    bool ok = false;
                    foreach (var candidate in def.Methods)
                    {
                        if (candidate.Name != call.Name) continue;
                        if (candidate.Parameters.Count != call.Parameters.Count) continue;
                        bool same = true;
                        for (int i = 0; i < candidate.Parameters.Count && same; i++)
                            same = Norm(candidate.Parameters[i].ParameterType) == Norm(call.Parameters[i].ParameterType);
                        if (same) { ok = true; break; }
                    }
                    if (!ok && seen.Add("M " + call.FullName))
                    {
                        // Nome existe mas os parametros nao batem: e' o caso do
                        // proxy (GameObject/GameArray<T> no lugar do tipo do PC).
                        bool sameName = def.Methods.Any(c => c.Name == call.Name);
                        Console.WriteLine($"{(sameName ? "ASSINATURA " : "FALTA METODO")}  {call.FullName}");
                        missing++;
                    }
                }
                else if (mr is FieldReference slot)
                {
                    checkedCount++;
                    if (def.Fields.All(f => f.Name != slot.Name) && seen.Add("F " + slot.FullName))
                    {
                        Console.WriteLine($"FALTA CAMPO   {slot.FullName}");
                        missing++;
                    }
                }
            }
            Console.WriteLine($"conferidos: {checkedCount}, faltando: {missing}");
            return 0;
        }

        if (args[1] == "--uses" && args.Length >= 3)
        {
            // uso: ildump <dll> --uses <TypeFullName|substring>
            //
            // Onde exatamente o tipo e' citado: base, interface, campo, parametro,
            // retorno, local, ou instrucao (newobj/ldtoken/castclass...). Existe
            // porque "faltando: 1" nao diz se e' um campo (bloqueia) ou um
            // `typeof` dentro de um corpo (nao bloqueia).
            string want = args[2];
            bool Hit(TypeReference? tr)
            {
                if (tr == null) return false;
                try { return tr.FullName.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0; }
                catch { return false; }
            }
            foreach (var t in module.GetTypes())
            {
                if (Hit(t.BaseType)) Console.WriteLine($"BASE     {t.FullName} : {t.BaseType!.FullName}");
                foreach (var i in t.Interfaces) if (Hit(i.InterfaceType)) Console.WriteLine($"IFACE    {t.FullName} : {i.InterfaceType.FullName}");
                foreach (var f in t.Fields) if (Hit(f.FieldType)) Console.WriteLine($"CAMPO    {t.FullName}.{f.Name} : {f.FieldType.FullName} ({f.FieldType.Scope})");
                foreach (var m in t.Methods)
                {
                    if (Hit(m.ReturnType)) Console.WriteLine($"RET      {t.FullName}.{m.Name} : {m.ReturnType.FullName}");
                    foreach (var p in m.Parameters) if (Hit(p.ParameterType)) Console.WriteLine($"PARAM    {t.FullName}.{m.Name}({p.Name}) : {p.ParameterType.FullName}");
                    if (!m.HasBody) continue;
                    foreach (var v in m.Body.Variables) if (Hit(v.VariableType)) Console.WriteLine($"LOCAL    {t.FullName}.{m.Name} : {v.VariableType.FullName}");
                    foreach (var ins in m.Body.Instructions)
                    {
                        bool hit = ins.Operand switch
                        {
                            TypeReference tr => Hit(tr),
                            MethodReference mr => Hit(mr.DeclaringType),
                            FieldReference fr => Hit(fr.DeclaringType),
                            _ => false
                        };
                        if (hit) Console.WriteLine($"IL       {t.FullName}.{m.Name} @IL_{ins.Offset:X4}: {ins.OpCode} {Format(ins.Operand)}");
                    }
                }
            }
            return 0;
        }

        if (args[1] == "--refs")
        {
            // uso: ildump <dll> --refs <substring>
            //
            // Lista as referencias de MEMBRO (metodo/campo) cujo tipo declarante
            // casa com o filtro, com a assinatura completa. Serve para montar um
            // stub: o TypeRef diz "falta o tipo", o MemberRef diz "e falta este
            // metodo, com estes parametros".
            string filtro = args.Length > 2 ? args[2] : "";
            var seen = new System.Collections.Generic.SortedSet<string>(StringComparer.Ordinal);
            foreach (var mr in module.GetMemberReferences())
            {
                if (mr.DeclaringType == null) continue;
                if (mr.DeclaringType.FullName.IndexOf(filtro, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string kind = mr is MethodReference m2 ? (m2.HasThis ? "inst" : "stat") : "campo";
                seen.Add($"[{kind}] {mr.FullName}");
            }
            foreach (string line in seen) Console.WriteLine(line);
            Console.WriteLine($"total: {seen.Count}");
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

        if (args[1] == "--ldftn")
        {
            // uso: ildump <dll> --ldftn <substring>
            //
            // Pontos em que um metodo e' transformado em delegate (`ldftn`).
            // E o que responde "quem instala este callback": um hook do MonoMod
            // ou um IL edit nao aparece no `--calls`, que so' olha
            // call/callvirt/newobj.
            string filtro = args.Length > 2 ? args[2] : "";
            int total = 0;
            foreach (var t in module.GetTypes())
                foreach (var m in t.Methods)
                {
                    if (!m.HasBody) continue;
                    foreach (var ins in m.Body.Instructions)
                    {
                        if (ins.OpCode.Code is not (Code.Ldftn or Code.Ldvirtftn)) continue;
                        if (ins.Operand is not MethodReference mr) continue;
                        if (filtro.Length > 0 && mr.FullName.IndexOf(filtro, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        Console.WriteLine($"{m.FullName}\n     {ins}");
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
