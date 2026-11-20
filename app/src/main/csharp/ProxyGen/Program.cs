using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ProxyGen;

static class Program
{
    const string RT = TypeMap.RT;
    static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                       BindingFlags.Static | BindingFlags.DeclaredOnly;

    static Assembly gameAssembly = null!;

    /// <summary>
    /// Tipos que o tML declara como RECORD: o código do mod usa `with`
    /// (`((SoundStyle)SoundID.Shatter) with { Pitch = 0.5f }`) e isso só
    /// compila para record. Marcados em types.txt com <c>@record Tipo.Completo</c>.
    /// </summary>
    static readonly HashSet<string> RecordTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Tipos que NÃO devem ser gerados: existem escritos à mão no shim.
    /// Marcados em types.txt com <c>- Tipo.Completo</c>.
    /// </summary>
    static readonly HashSet<string> SkipTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Tipos que são STRUCT no tModLoader mas CLASSE no dump (o jogo foi
    /// compilado assim). O proxy tem de ser struct para o mod compilar/ligar
    /// ("Method Draw does not have an implementation" acontece porque o
    /// <c>ref PlayerDrawSet</c> do mod não bate com a classe).
    /// Marcados em types.txt com <c>% Tipo.Completo</c>.
    /// </summary>
    static readonly HashSet<string> PointerStructs = new(StringComparer.Ordinal);

    /// <summary>Tipos cujo proxy aceita herança (`@open` em types.txt).</summary>
    static readonly HashSet<string> OpenTypes = new(StringComparer.Ordinal);

    static int Main(string[] args)
    {
        string? dummy = null, outDir = null;
        var typesFiles = new List<string>();
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--dummy": dummy = args[i + 1]; break;
                case "--types": typesFiles.Add(args[i + 1]); break;
                case "--out": outDir = args[i + 1]; break;
            }
        }
        if (dummy == null || typesFiles.Count == 0 || outDir == null)
        {
            Console.Error.WriteLine("usage: ProxyGen --dummy <DummyDll> --types <types.txt> [--types <more.txt>...] --out <dir>");
            return 2;
        }
        if (!File.Exists(Path.Combine(dummy, "Assembly-CSharp.dll")))
        {
            Console.Error.WriteLine($"ProxyGen: {dummy}/Assembly-CSharp.dll not found (run tools/dump.sh, see refs/README.md)");
            return 3;
        }

        var resolver = new NameResolver(dummy);
        using var mlc = new MetadataLoadContext(resolver, "mscorlib");
        gameAssembly = mlc.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(dummy, "Assembly-CSharp.dll")));
        Type[] types;
        try { types = gameAssembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
        var byName = new Dictionary<string, Type>();
        foreach (Type t in types) byName[TypeMap.Key(t)] = t;

        var listed = new List<Type>();
        var missing = new List<string>();
        foreach (string raw in typesFiles.SelectMany(File.ReadAllLines))
        {
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('='))
            {
                string[] parts = line[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int dot = parts[0].LastIndexOf('.');
                string owner = parts[0][..dot];
                if (!ExtraEnumValues.TryGetValue(owner, out List<(string Name, string Value)>? values)) ExtraEnumValues[owner] = values = new List<(string Name, string Value)>();
                values.Add((parts[0][(dot + 1)..], parts[1]));
                continue;
            }
            if (line.StartsWith('%'))
            {
                PointerStructs.Add(line[1..].Trim());
                continue;
            }
            if (line.StartsWith('-'))
            {
                SkipTypes.Add(line[1..].Trim());
                continue;
            }
            if (line.StartsWith('@'))
            {
                // @record Tipo.Completo — o primeiro token é a diretiva.
                string[] directive = line[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (directive.Length >= 2 && directive[0].Equals("record", StringComparison.OrdinalIgnoreCase))
                    RecordTypes.Add(directive[1]);
                else if (directive.Length >= 2 && directive[0].Equals("open", StringComparison.OrdinalIgnoreCase))
                    OpenTypes.Add(directive[1]);
                continue;
            }
            if (line.StartsWith("!!"))
            {
                // `!!Tipo.Membro`: escrito à mão inclusive a dupla orig_/hook_.
                // O `!` simples só reserva o nome no proxy e continua gerando o
                // gancho (há código escrito à mão que usa esses delegates).
                int dot = line.LastIndexOf('.');
                string owner = line[2..dot];
                string member = line[(dot + 1)..];
                if (!HandWrittenHooks.TryGetValue(owner, out HashSet<string>? hookNames)) HandWrittenHooks[owner] = hookNames = new HashSet<string>();
                hookNames.Add(member);
                if (!HandWritten.TryGetValue(owner, out HashSet<string>? allNames)) HandWritten[owner] = allNames = new HashSet<string>();
                allNames.Add(member);
                continue;
            }
            if (line.StartsWith('!'))
            {
                int dot = line.LastIndexOf('.');
                string owner = line[1..dot];
                if (!HandWritten.TryGetValue(owner, out HashSet<string>? names)) HandWritten[owner] = names = new HashSet<string>();
                names.Add(line[(dot + 1)..]);
                continue;
            }
            Type? t = Lookup(byName, line);
            if (t == null)
            {
                missing.Add($"{line}: not found in Assembly-CSharp");
                continue;
            }
            if (t.IsGenericTypeDefinition)
            {
                missing.Add($"{line}: generic types are not generated yet");
                continue;
            }
            if (!listed.Contains(t)) listed.Add(t);
        }
        foreach (string m in missing) Console.WriteLine($"ProxyGen: skipped type {m}");
        if (listed.Count == 0)
        {
            Console.Error.WriteLine($"ProxyGen: none of the listed types is in {dummy}/Assembly-CSharp.dll (see refs/README.md)");
            return 4;
        }

        var map = new TypeMap(listed.Select(TypeMap.Key).ToHashSet(), t => t.Assembly == gameAssembly);
        foreach (Type t in listed) map.RegisterFull(t);
        var emitter = new Emitter(map);

        var plans = new Dictionary<string, TypePlan>();
        var laidOut = new HashSet<string>();
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (key, (type, mode)) in map.Generated.ToList())
            {
                if (type.IsValueType && !type.IsEnum && laidOut.Add(key))
                {
                    map.Layout(type);
                    grew = true;
                }
                if (mode != GenMode.Full || plans.ContainsKey(key)) continue;
                plans[key] = BuildPlan(type, map, emitter);
                grew = true;
            }
        }
        foreach (var (key, plan) in plans) Prune(map.Generated[key].Type, plan, map, emitter);

        Directory.CreateDirectory(outDir);
        foreach (string old in Directory.GetFiles(outDir, "*.g.cs")) File.Delete(old);
        var outer = map.Generated.Values.Select(v => TypeMap.Outermost(v.Type)).Distinct().ToList();
        var layoutChecks = new List<string>();
        int files = 0;
        foreach (Type top in outer.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var sb = new StringBuilder();
            sb.Append("// <auto-generated> ProxyGen: nao editar. </auto-generated>\n");
            sb.Append("#nullable enable\n#pragma warning disable CS0108, CS0114, CS0169, CS0414, CS0649, CS0660, CS0661, CS8981, CS1998\n");
            string ns = TypeMap.ProxyNamespace(top);
            if (ns.Length > 0) sb.Append($"namespace {ns}\n{{\n");
            var hooks = new StringBuilder();
            EmitType(sb, ns.Length > 0 ? "    " : "", top, map, plans, emitter, hooks, layoutChecks);
            sb.Append(hooks);
            if (ns.Length > 0) sb.Append("}\n");
            string file = (ns.Length > 0 ? ns + "." : "") + top.Name.Split('`')[0] + ".g.cs";
            File.WriteAllText(Path.Combine(outDir, file), sb.ToString());
            files++;
        }

        var support = new StringBuilder();
        support.Append("// <auto-generated> ProxyGen: nao editar. </auto-generated>\n#nullable enable\n");
        support.Append("namespace BunnyLoader.Generated\n{\n");
        foreach (int size in emitter.RetBufSizes)
        {
            support.Append($"    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential, Size = {size})]\n");
            support.Append($"    public struct RetBuf{size} {{ long _0; }}\n");
        }
        support.Append("    public static class StructLayouts\n    {\n");
        support.Append("        public static readonly (global::BunnyLoader.Interop.GameClass Class, int Size)[] All =\n        {\n");
        foreach (string c in layoutChecks) support.Append("            ").Append(c).Append(",\n");
        support.Append("        };\n    }\n}\n");
        File.WriteAllText(Path.Combine(outDir, "BunnyLoader.Generated.g.cs"), support.ToString());
        File.WriteAllText(Path.Combine(outDir, "GameDelegates.g.cs"),
            "// <auto-generated> ProxyGen: nao editar. </auto-generated>\n#nullable enable\n#pragma warning disable CS8981\nnamespace BunnyLoader.Interop.Delegates\n{\n" + emitter.Bridges + "}\n");

        File.WriteAllLines(Path.Combine(outDir, "skipped.txt"), missing.Select(m => "type " + m).Concat(emitter.Skipped));
        Console.WriteLine($"ProxyGen: {map.Generated.Count} types ({plans.Count} full) in {files} files; " +
                          $"{emitter.Methods} methods, {emitter.Hooks} hooks, {emitter.Fields} fields, {emitter.Properties} properties, {emitter.Events} events, {emitter.Overloads} delegate overloads; " +
                          $"{missing.Count} types and {emitter.Skipped.Count} members skipped (skipped.txt)");
        return 0;
    }

    static Type? Lookup(Dictionary<string, Type> byName, string line)
    {
        string key = line.Replace('/', '+');
        if (byName.TryGetValue(key, out Type? t)) return t;
        for (int dot = key.LastIndexOf('.'); dot > 0; dot = key.LastIndexOf('.', dot - 1))
        {
            string nested = key[..dot] + "+" + key[(dot + 1)..].Replace('.', '+');
            if (byName.TryGetValue(nested, out t)) return t;
        }
        return null;
    }

    sealed class NameResolver : MetadataAssemblyResolver
    {
        readonly string dir;

        public NameResolver(string dir) => this.dir = dir;

        public override Assembly? Resolve(MetadataLoadContext context, AssemblyName name)
        {
            string path = Path.GetFullPath(Path.Combine(dir, name.Name + ".dll"));
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        }
    }

    sealed class FieldModel
    {
        public FieldInfo Field = null!;
        public string Name = "";
        public TypeUse Use = null!;
        public bool IsStatic, IsConst;
    }

    sealed class PropModel
    {
        public string Name = "";
        public bool IsStatic;
        public TypeUse Use = null!;
        public MethodModel? Get, Set;
    }

    sealed class TypePlan
    {
        public List<FieldModel> Fields = new();
        public List<PropModel> Props = new();
        public List<MethodModel> Methods = new();
        public List<MethodModel> Ctors = new();
        public List<(MethodModel Model, string Event)> Hooks = new();
        public List<EventModel> Events = new();
    }

    sealed class EventModel
    {
        public string Name = "", Key = "";
        public bool IsStatic;
        public MethodModel Add = null!, Remove = null!, Invoke = null!;
        public Type Delegate = null!;
    }

    static IEnumerable<Type> SelfAndAncestors(Type t, TypeMap map)
    {
        yield return t;
        if (t.IsValueType) yield break;
        for (Type? b = t.BaseType; b != null && map.IsGame(b) && !b.IsGenericType; b = b.BaseType) yield return b;
    }

    static TypePlan BuildPlan(Type t, TypeMap map, Emitter emitter)
    {
        var plan = new TypePlan();
        bool isStatic = t.IsAbstract && t.IsSealed;
        var used = new HashSet<string>(Reserved(t));

        string Claim(string name)
        {
            string id = TypeMap.Ident(name);
            while (!used.Add(id)) id += "_";
            return id;
        }

        if (!t.IsValueType && !t.IsInterface)
        {
            foreach (EventInfo e in t.GetEvents(All | BindingFlags.DeclaredOnly))
            {
                if (!TypeMap.ValidName(e.Name) || e.EventHandlerType is not Type handler) continue;
                MethodInfo? add = e.GetAddMethod(true), remove = e.GetRemoveMethod(true);
                if (add == null || remove == null || add.IsStatic != remove.IsStatic || (isStatic && !add.IsStatic)) continue;
                MethodInfo? invoke = handler.GetMethod("Invoke");
                MethodModel? am = emitter.Model(add, t, false), rm = emitter.Model(remove, t, false);
                MethodModel? im = invoke != null ? emitter.Model(invoke, handler, true) : null;
                if (am == null || rm == null || im == null || am.Params.Count != 1 || rm.Params.Count != 1) continue;
                if (Emitter.ManagedDelegate(im) == null)
                {
                    emitter.Skip(t, e.Name, $"event: {TypeMap.DumpName(handler)} has no managed delegate mapping");
                    continue;
                }
                plan.Events.Add(new EventModel
                {
                    Name = Claim(e.Name), Key = $"{t.FullName}.{e.Name}", IsStatic = add.IsStatic,
                    Add = am, Remove = rm, Invoke = im, Delegate = handler,
                });
            }
        }

        var seenFields = new HashSet<string>();
        foreach (Type owner in SelfAndAncestors(t, map))
        {
            foreach (FieldInfo f in owner.GetFields(All))
            {
                if (!TypeMap.ValidName(f.Name) || !seenFields.Add(f.Name)) continue;
                bool own = owner == t;
                if ((f.IsStatic || f.IsLiteral) && !own) continue;
                if (t.IsValueType && !f.IsStatic && !f.IsLiteral) continue;
                if (isStatic && !f.IsStatic && !f.IsLiteral) continue;
                TypeUse? use = map.Map(f.FieldType, true);
                // `SoundID.Item1` & cia: no dump são LegacySoundStyle (classe),
                // no tML são `SoundStyle` (struct). O mod empilha
                // `newobj Nullable<SoundStyle>` — se aqui voltar uma referência
                // o IL fica inválido no aparelho (`newobj 0x0a...`).
                if (f.FieldType.FullName == "Terraria.Audio.LegacySoundStyle" && owner.FullName == "Terraria.ID.SoundID")
                    use = TypeMap.SoundStyleUse(TypeMap.DumpName(f.FieldType));
                if (use == null || use.Abi == AbiClass.Void)
                {
                    emitter.Skip(owner, f.Name, $"field type {TypeMap.DumpName(f.FieldType)} has no C# mapping");
                    continue;
                }
                if (f.IsLiteral)
                {
                    object? v;
                    try { v = f.GetRawConstantValue(); }
                    catch { continue; }
                    if (v == null || Emitter.Literal(v) == null) continue;
                }
                plan.Fields.Add(new FieldModel
                {
                    Field = f, Name = Claim(f.Name), IsStatic = f.IsStatic, IsConst = f.IsLiteral,
                    Use = (f.FieldType.FullName == "System.String" || map.AssetOf(f.FieldType) != null) && !f.IsLiteral ? map.Map(f.FieldType, false)! : use,
                });
            }
        }

        var accessors = new HashSet<MethodInfo>();
        var seenProps = new HashSet<string>();
        foreach (Type owner in SelfAndAncestors(t, map))
        {
            foreach (PropertyInfo p in owner.GetProperties(All))
            {
                if (!TypeMap.ValidName(p.Name) || p.GetIndexParameters().Length > 0) continue;
                MethodInfo? g = p.GetGetMethod(true), s = p.GetSetMethod(true);
                bool st = (g ?? s)!.IsStatic;
                if (st && owner != t) continue;
                if (isStatic && !st) continue;
                if (!seenProps.Add(p.Name)) continue;
                if (g != null) accessors.Add(g);
                if (s != null) accessors.Add(s);
                MethodModel? gm = g != null && (!g.IsAbstract || t.IsInterface) ? emitter.Model(g, t, false) : null;
                MethodModel? sm = s != null && (!s.IsAbstract || t.IsInterface) ? emitter.Model(s, t, false) : null;
                if (gm != null && gm.Plan.Ret.ByRef) gm = null;
                if (gm == null && sm == null) continue;
                TypeUse use = gm != null ? gm.Plan.Ret.Use : sm!.Params[0].Use;
                plan.Props.Add(new PropModel { Name = Claim(p.Name), IsStatic = st, Use = use, Get = gm, Set = sm });
            }
        }

        var groups = new Dictionary<string, string>();
        var seenSigs = new HashSet<string>();
        foreach (Type owner in SelfAndAncestors(t, map))
        {
            foreach (MethodInfo m in owner.GetMethods(All))
            {
                if (accessors.Contains(m) || m.IsSpecialName && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")) continue;
                if (m.IsSpecialName) continue;
                if (m.IsStatic && owner != t) continue;
                if (isStatic && !m.IsStatic) continue;
                MethodModel? model = emitter.Model(m, t, false);
                if (model == null) continue;
                string key = m.Name + "(" + string.Join(",", model.Params.Select(a => a.Mode + " " + a.Use.Cs)) + ")";
                if (!seenSigs.Add(key)) continue;
                if (!groups.TryGetValue(m.Name, out string? name))
                {
                    name = Claim(m.Name);
                    groups[m.Name] = name;
                }
                model.Name = name;
                plan.Methods.Add(model);
            }
        }

        if (!isStatic && !t.IsAbstract && !t.IsInterface)
        {
            var seenCtors = new HashSet<string>();
            foreach (ConstructorInfo c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                MethodModel? model = emitter.Model(c, t, false);
                if (model == null) continue;
                if (!seenCtors.Add(string.Join(",", model.Params.Select(a => a.Mode + " " + a.Use.Cs)))) continue;
                model.Name = "New";
                plan.Ctors.Add(model);
            }
        }

        var hookable = new List<MethodModel>();
        bool IsHandWrittenHook(MethodInfo m) =>
            HandWrittenHooks.TryGetValue(t.FullName ?? t.Name, out HashSet<string>? hand) && hand.Contains(m.Name);
        foreach (MethodInfo m in t.GetMethods(All))
        {
            if (m.IsAbstract || !TypeMap.ValidName(m.Name)) continue;
            if (m.IsSpecialName && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")) continue;
            if (t.IsInterface) continue;
            if (IsHandWrittenHook(m)) continue;
            MethodModel? model = emitter.Model(m, t, true);
            if (model != null) hookable.Add(model);
        }
        var byName = hookable.GroupBy(h => h.Method.Name).ToDictionary(g => g.Key, g => g.Count());
        var events = new HashSet<string>();
        foreach (MethodModel h in hookable)
        {
            string ev = byName[h.Method.Name] == 1 ? h.Method.Name : h.Method.Name + "_" + string.Join("_", h.Params.Select(Token));
            if (h.Params.Count == 0 && byName[h.Method.Name] > 1) ev = h.Method.Name;
            string id = TypeMap.Ident(ev);
            while (!events.Add(id)) id += "_";
            plan.Hooks.Add((h, id));
        }
        return plan;
    }

    static string? MissingIn(MethodModel m, TypeMap map)
    {
        foreach (TypeUse use in m.Params.Select(a => a.Use).Prepend(m.Plan.Ret.Use))
            if (map.Missing(use) is Type t) return TypeMap.DumpName(t);
        return null;
    }

    static void Prune(Type t, TypePlan plan, TypeMap map, Emitter emitter)
    {
        string Reason(string type) => $"type {type} is not generated";
        plan.Events.RemoveAll(e =>
        {
            string? missing = MissingIn(e.Add, map) ?? MissingIn(e.Remove, map) ?? MissingIn(e.Invoke, map);
            if (missing == null) return false;
            emitter.Skip(t, e.Name, Reason(missing));
            return true;
        });
        plan.Fields.RemoveAll(f =>
        {
            if (map.Missing(f.Use) is not Type missing) return false;
            emitter.Skip(t, f.Field.Name, Reason(TypeMap.DumpName(missing)));
            return true;
        });
        plan.Props.RemoveAll(p =>
        {
            string? missing = null;
            if (p.Get != null && MissingIn(p.Get, map) is string g) { missing = g; p.Get = null; }
            if (p.Set != null && MissingIn(p.Set, map) is string s) { missing ??= s; p.Set = null; }
            if (p.Get == null && p.Set == null)
            {
                emitter.Skip(t, p.Name, Reason(missing ?? "?"));
                return true;
            }
            p.Use = p.Get != null ? p.Get.Plan.Ret.Use : p.Set!.Params[0].Use;
            return false;
        });
        bool Drop(MethodModel m, string what)
        {
            if (MissingIn(m, map) is not string missing) return false;
            emitter.Skip(t, what, Reason(missing));
            return true;
        }
        plan.Methods.RemoveAll(m => Drop(m, m.Method.Name));
        plan.Ctors.RemoveAll(m => Drop(m, ".ctor"));
        plan.Hooks.RemoveAll(h => Drop(h.Model, "On_" + h.Event));
    }

    static string Token(ArgPlan a)
    {
        string mod = a.Mode switch { ArgMode.Ref => "ref", ArgMode.Out => "out", ArgMode.In => "in", _ => "" };
        string s = a.Use.Sig;
        s = s.Replace("[]", "Array").Replace("[,]", "Array2D");
        int lt = s.IndexOf('<');
        if (lt >= 0) s = s[..lt];
        s = s.Split('.').Last();
        return mod + new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
    }

    static readonly Dictionary<string, HashSet<string>> HandWritten = new();

    /// <summary>`!!Tipo.Membro` em types.txt: a dupla <c>orig_/hook_</c> também é escrita à mão.</summary>
    static readonly Dictionary<string, HashSet<string>> HandWrittenHooks = new();

    static readonly Dictionary<string, List<(string Name, string Value)>> ExtraEnumValues = new();

    static IEnumerable<string> Reserved(Type t)
    {
        yield return TypeMap.Ident(t.Name);
        if (HandWritten.TryGetValue(t.FullName ?? t.Name, out HashSet<string>? hand))
            foreach (string name in hand) yield return name;
        foreach (string r in new[] { "Pointer", "IsNull", "Class", "New", "Equals", "GetHashCode", "ToString", "GetType", "From", "Is", "Finalize", "MemberwiseClone" })
            yield return r;
        foreach (Type n in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) yield return TypeMap.Ident(n.Name);
    }

    static string ClassExpr(Type t) => TypeMap.ProxyName(t) + ".Class";

    static string NewClass(Type t)
    {
        Type top = TypeMap.Outermost(t);
        var path = new List<string>();
        for (Type? c = t; c != null && c != top; c = c.DeclaringType) path.Insert(0, c.Name);
        return $"new {RT}.GameClass(\"{top.Namespace ?? ""}\", \"{top.Name}\", \"{string.Join("/", path)}\")";
    }

    static void EmitType(StringBuilder sb, string ind, Type t, TypeMap map, Dictionary<string, TypePlan> plans,
                         Emitter emitter, StringBuilder hooks, List<string> layoutChecks)
    {
        string key = TypeMap.Key(t);
        if (!map.Generated.TryGetValue(key, out var entry)) return;
        GenMode mode = entry.Mode;
        string name = TypeMap.Ident(t.Name);
        string self = TypeMap.ProxyName(t);
        string i1 = ind + "    ";

        if (mode == GenMode.Enum)
        {
            TypeUse under = map.Map(t.GetEnumUnderlyingType(), true)!;
            sb.Append(ind).Append($"public enum {name} : {under.Cs}\n").Append(ind).Append("{\n");
            var names = new HashSet<string>();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!f.IsLiteral || !TypeMap.ValidName(f.Name)) continue;
                object? v;
                try { v = f.GetRawConstantValue(); }
                catch { continue; }
                string? lit = v != null ? Emitter.Literal(Convert.ChangeType(v, Type.GetType("System." + t.GetEnumUnderlyingType().Name)!)) : null;
                string id = TypeMap.Ident(f.Name);
                if (lit == null || !names.Add(id)) continue;
                sb.Append(i1).Append($"{id} = unchecked(({under.Cs})({lit})),\n");
            }
            if (ExtraEnumValues.TryGetValue(t.FullName ?? t.Name, out List<(string Name, string Value)>? extra))
                foreach (var (extraName, extraValue) in extra)
                    if (names.Add(extraName))
                        sb.Append(i1).Append($"{extraName} = unchecked(({under.Cs})({extraValue})),\n");
            sb.Append(ind).Append("}\n");
            return;
        }

        bool isStruct = t.IsValueType;
        if (SkipTypes.Contains(TypeMap.Key(t)) || SkipTypes.Contains(t.FullName ?? t.Name))
        {
            Console.WriteLine($"ProxyGen: {t.FullName}: gerado à mão (types.txt '-'), pulando");
            return;
        }
        bool isStaticClass = t.IsAbstract && t.IsSealed && !isStruct;
        plans.TryGetValue(key, out TypePlan? plan);
        StructLayout? layout = isStruct ? map.Layout(t) : null;

        if (isStruct)
        {
            if (layout == null) return;
            sb.Append(ind).Append($"[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit, Size = {layout.Size})]\n");
            sb.Append(ind).Append($"public unsafe partial struct {name}\n").Append(ind).Append("{\n");
            sb.Append(i1).Append($"public static readonly {RT}.GameClass Class = {NewClass(t)};\n");
              var used = new HashSet<string>(Reserved(t));
              foreach (var (f, offset, use) in layout.Fields)
              {
                  if (map.Missing(use) is Type missing)
                  {
                      emitter.Skip(t, f.Name, $"type {TypeMap.DumpName(missing)} is not generated");
                      continue;
                  }
                  string id = TypeMap.Ident(f.Name);
                  if (!TypeMap.ValidName(f.Name)) id = $"__field{offset}";
                  while (!used.Add(id)) id += "_";
                  // Campo de REFERENCIA (Item, Texture2D...): um struct nao
                  // pode segurar uma referencia gerenciada (o compilador
                  // recusa: CS8377). Guarda-se o ponteiro e a propriedade
                  // devolve o proxy.
                  bool refField = use.Cs != $"{RT}.GameString"
                      && use.Cs != "nint"
                      && !f.FieldType.IsValueType
                      && !(f.FieldType.IsAbstract && f.FieldType.IsSealed);
                  if (refField)
                  {
                      string backing = "__ptr" + id;
                      while (!used.Add(backing)) backing += "_";
                      sb.Append(i1).Append($"[global::System.Runtime.InteropServices.FieldOffset({offset})] private nint {backing};\n");
                      if (use.IsClassProxy)
                          sb.Append(i1).Append($"public {use.Cs} {id} {{ get => {backing} == 0 ? null! : new {use.Cs}({backing}); set => {backing} = (nint)(value?.Pointer ?? 0); }}\n");
                      else
                          sb.Append(i1).Append($"public {use.Cs} {id} {{ get => new {use.Cs}({backing}); set => {backing} = value.Pointer; }}\n");
                  }
                  else
                  {
                      sb.Append(i1).Append($"[global::System.Runtime.InteropServices.FieldOffset({offset})] public {FieldCs(use)} {id};\n");
                  }
                  emitter.Fields++;
              }
            if (!t.IsGenericType) layoutChecks.Add($"({ClassExpr(t)}, {layout.Size})");
        }
        else if (isStaticClass)
        {
            sb.Append(ind).Append($"public static unsafe partial class {name}\n").Append(ind).Append("{\n");
            sb.Append(i1).Append($"public static readonly {RT}.GameClass Class = {NewClass(t)};\n");
        }
          else
          {
            // Referencia de verdade (Item, NPC, Player...): o tModLoader ve
            // CLASSE, e o IL do mod faz `callvirt get_Item()` + `stfld
            // Item::damage`. Com um proxy struct esse IL nao resolve (o
            // MemberRef pede uma referencia e o struct e um valor) e o
            // SetDefaults do mod nao JITa. Entao o proxy e uma class.
            //
            // `@record` em types.txt: alguns tipos são RECORD no tML e o mod
            // usa a expressão `with` (`soundStyle with { Pitch = 0.5f }`).
            // Record só existe com `record class` — e aí o compilador já gera
            // Equals/GetHashCode/ToString/==, que não podemos emitir de novo.
            bool asRecord = RecordTypes.Contains(t.FullName ?? t.Name);
            bool asStruct = PointerStructs.Contains(t.FullName ?? t.Name);
            // `sealed` por padrão; `@open` em types.txt libera a herança.
            // Motivo: todo proxy de classe saía sealed, e mods que DERIVAM de
            // classe do jogo não carregavam — `TypeLoadException: Could not
            // load type 'FargowiltasSouls.Content.Sky.AbomSky' ... because the
            // parent type is sealed` (pai = Terraria.Graphics.Effects.CustomSky).
            // Não dá para simplesmente seguir o flag do dump: vários tipos têm
            // `sealed partial class` escrito à mão (Chest, Collision, Cursor...)
            // e o C# recusa modificadores conflitantes entre partes. Então a
            // lista é explícita e cresce só quando um mod precisa derivar.
            bool sealType = !OpenTypes.Contains(t.FullName ?? t.Name);
            string kind = asStruct ? "readonly partial struct"
                : asRecord ? (sealType ? "sealed partial record class" : "partial record class")
                : (sealType ? "sealed partial class" : "partial class");
            // struct proxy não implementa IEquatable<T> aqui: o `Equals(T?)` do
            // gerador não existe para value type e o `==` com null também não.
            string ifaces = asStruct
                ? $"{RT}.IGameObject"
                : $"{RT}.IGameObject, global::System.IEquatable<{self}>";
            sb.Append(ind).Append($"public unsafe {kind} {name} : {ifaces}\n");
            sb.Append(ind).Append("{\n");
            sb.Append(i1).Append($"public static readonly {RT}.GameClass Class = {NewClass(t)};\n");
            sb.Append(i1).Append("public readonly nint Pointer;\n");
            sb.Append(i1).Append($"public {name}(nint pointer) => Pointer = pointer;\n");
            // @open: o mod faz newobj do tipo dele e o ctor chama base..ctor().
            // Sem ctor sem parametros o metodo do mod nao JITa (MissingMethod:
            // Terraria.Graphics.Effects.CustomSky::.ctor). O Pointer e um bloco
            // zerado alugado (Interop/ManagedEffects): os campos-proxy
            // (Opacity, _priority) nao estouram e o EffectManager reconhece a
            // instancia como managed.
            if (!sealType && !asStruct)
                sb.Append(i1).Append($"public {name}() : this({RT}.ManagedEffects.New()) {{ }}\n");
            sb.Append(i1).Append($"nint {RT}.IGameObject.Pointer => Pointer;\n");
            sb.Append(i1).Append("public bool IsNull => Pointer == 0;\n");
            if (!asRecord && !asStruct)
            {
                sb.Append(i1).Append($"public bool Equals({self}? other) => other is not null && Pointer == other.Pointer;\n");
                sb.Append(i1).Append($"public override bool Equals(object? obj) => obj is {self} o && o.Pointer == Pointer;\n");
                sb.Append(i1).Append("public override int GetHashCode() => Pointer.GetHashCode();\n");
                sb.Append(i1).Append($"public static bool operator ==({self} a, {self} b) => a is null ? b is null : b is not null && a.Pointer == b.Pointer;\n");
                sb.Append(i1).Append($"public static bool operator !=({self} a, {self} b) => !(a == b);\n");
                if (!(HandWritten.TryGetValue(t.FullName ?? t.Name, out HashSet<string>? own) && own.Contains("ToString")))
                    sb.Append(i1).Append($"public override string ToString() => new {RT}.GameObject(Pointer).ToString();\n");
            }
            sb.Append(i1).Append($"public static implicit operator {RT}.GameObject({self} v) => new {RT}.GameObject(v.Pointer);\n");
            sb.Append(i1).Append($"public static {self} From({RT}.GameObject o) => new {self}(o.Pointer);\n");
            sb.Append(i1).Append($"public static bool Is({RT}.GameObject o) => {RT}.GameInterop.IsInstance(o.Pointer, Class);\n");
            for (Type? b = t.BaseType; b != null; b = b.BaseType)
            {
                if (!map.Generated.TryGetValue(TypeMap.Key(b), out var be) || be.Mode == GenMode.Enum) continue;
                string bn = TypeMap.ProxyName(b);
                sb.Append(i1).Append($"public static implicit operator {bn}({self} v) => new {bn}(v.Pointer);\n");
                sb.Append(i1).Append($"public static explicit operator {self}({bn} v) => new {self}(v.Pointer);\n");
            }
            foreach (Type it in t.GetInterfaces().OrderBy(x => x.FullName, StringComparer.Ordinal))
            {
                if (!map.Generated.TryGetValue(TypeMap.Key(it), out var ie) || ie.Mode == GenMode.Enum) continue;
                string iname = TypeMap.ProxyName(it);
                sb.Append(i1).Append($"public static implicit operator {iname}({self} v) => new {iname}(v.Pointer);\n");
                sb.Append(i1).Append($"public static explicit operator {self}({iname} v) => new {self}(v.Pointer);\n");
            }
        }

        if (plan != null) EmitMembers(sb, i1, t, plan, emitter);

        foreach (Type n in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).OrderBy(n => n.Name, StringComparer.Ordinal))
            EmitType(sb, i1, n, map, plans, emitter, hooks, layoutChecks);
        sb.Append(ind).Append("}\n");

        if (plan != null && plan.Hooks.Count > 0)
        {
            string hookClass = "On_" + string.Join("_", NestPath(t).Select(x => x.Name.Split('`')[0]));
            string hi = ind;
            hooks.Append(hi).Append($"public static unsafe partial class {hookClass}\n").Append(hi).Append("{\n");
            foreach (var (model, ev) in plan.Hooks)
                emitter.EmitHook(hooks, hi + "    ", model, ClassExpr(t), ev, $"{t.Name}.{model.Method.Name}");
            hooks.Append(hi).Append("}\n");
        }
    }

    static IEnumerable<Type> NestPath(Type t)
    {
        var list = new List<Type>();
        for (Type? c = t; c != null; c = c.IsNested ? c.DeclaringType : null) list.Insert(0, c);
        return list;
    }

    // ---- campos nullable ------------------------------------------------
    // O Mono resolve `ldfld/stfld` pelo NOME **e pela assinatura** do campo.
    // O proxy usava `GameNullable<T>` em todo lugar, então
    // `NPCID.Sets.NPCBestiaryDrawModifiers.Direction` (que o mod chama de
    // `int?`) não batia: o JIT morria com MissingFieldException e, no
    // aparelho, a referência quebrada entrava em libmonosgen (+0xC8758,
    // si_addr 0) matando o processo sem exceção nenhuma — foi o que derrubou
    // `Fargowiltas/Abominationn` antes mesmo do primeiro SetStaticDefaults.
    // O layout é idêntico (bool + valor), então basta o tipo ter o nome certo
    // nos slots de MEMÓRIA (campos). Em parâmetro/retorno continua
    // GameNullable: ali o valor atravessa a fronteira nativa.
    static bool IsNullableField(TypeUse use) => use.Cs.StartsWith(RT + ".GameNullable<", StringComparison.Ordinal);

    static string FieldCs(TypeUse use)
        => IsNullableField(use) ? "global::System.Nullable<" + use.Cs[(RT + ".GameNullable<").Length..] : use.Cs;

    static void EmitMembers(StringBuilder sb, string ind, Type t, TypePlan plan, Emitter emitter)
    {
        string cls = ClassExpr(t);
        int n = 0;
        foreach (FieldModel f in plan.Fields)
        {
            emitter.Fields++;
            string id = f.Name;
            string fname = Emitter.Literal(f.Field.Name)!;
            if (f.IsConst)
            {
                object v = f.Field.GetRawConstantValue()!;
                string lit = Emitter.Literal(v)!;
                string cs = f.Use.Cs == $"{RT}.GameString" ? "string" : f.Use.Cs;
                sb.Append(ind).Append($"public const {cs} {id} = unchecked(({cs})({lit}));\n");
                continue;
            }
            if (f.IsStatic)
            {
                sb.Append(ind).Append($"static {RT}.StaticRef __s{n};\n");
                // As cores nomeadas (`Color.White`, `Color.Red`...) sao CAMPOS
                // neste dump, mas no FNA do PC sao PROPRIEDADES: o mod chama
                // `get_White()`. Uma ref property nao serve (a assinatura de
                // retorno seria `Color&`). Emitindo propriedade de valor, o
                // getter tem exatamente a assinatura que o mod espera — e o
                // IlPatcher continua achando `White` para `ldsfld`.
                if (t.FullName == "Microsoft.Xna.Framework.Graphics.Color")
                {
                    string access = $"ref __s{n}, {cls}, {fname}";
                    sb.Append(ind).Append($"public static {f.Use.Cs} {id}\n").Append(ind).Append("{\n");
                    sb.Append(ind).Append($"    get => {RT}.GameInterop.ReadStatic<{f.Use.Cs}>({access});\n");
                    sb.Append(ind).Append($"    set => {RT}.GameInterop.WriteStatic<{f.Use.Cs}>({access}, value, false);\n");
                    sb.Append(ind).Append("}\n");
                    n++;
                    continue;
                }
                if (f.Field.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.ThreadStaticAttribute"))
                {
                    string access = $"ref __s{n}, {cls}, {fname}";
                    bool raw = f.Use.IsString || f.Use.Converted || f.Use.IsClassProxy;
                    string type = raw ? "nint" : f.Use.Cs;
                    string read = $"{RT}.GameInterop.ReadStatic<{type}>({access})";
                    string value = raw ? $"(nint)({f.Use.ToRaw("value")})" : "value";
                    string reference = !f.Field.FieldType.IsValueType && !f.Field.FieldType.IsPointer ? "true" : "false";
                    sb.Append(ind).Append($"public static {f.Use.Cs} {id}\n").Append(ind).Append("{\n");
                    sb.Append(ind).Append($"    get => {(raw ? f.Use.FromRaw(read) : read)};\n");
                    sb.Append(ind).Append($"    set => {RT}.GameInterop.WriteStatic<{type}>({access}, {value}, {reference});\n");
                    sb.Append(ind).Append("}\n");
                    n++;
                    continue;
                }
                string addr = $"{RT}.GameInterop.StaticAddress(ref __s{n}, {cls}, {fname})";
                if (f.Use.IsString || f.Use.Converted || f.Use.IsClassProxy)
                {
                    sb.Append(ind).Append($"public static {f.Use.Cs} {id}\n").Append(ind).Append("{\n");
                    sb.Append(ind).Append($"    get => {f.Use.FromRaw($"*(nint*){addr}")};\n");
                    sb.Append(ind).Append($"    set => *(nint*){addr} = (nint)({f.Use.ToRaw("value")});\n");
                    sb.Append(ind).Append("}\n");
                }
                else
                {
                    // ref properties nao podem ter set accessor (CS8147) e o
                    // nome set_X fica RESERVADO para a propriedade (CS0082) —
                    // por isso o metodo explicito usa __set_.
                    sb.Append(ind).Append($"public static ref {f.Use.Cs} {id} => ref *({f.Use.Cs}*){addr};\n");
                    sb.Append(ind).Append($"public static void __set_{id}({f.Use.Cs} value) => *({f.Use.Cs}*){addr} = value;\n");
                }
                n++;
                continue;
            }
            sb.Append(ind).Append($"static int __f{n} = -1;\n");
            string at = $"(Pointer + {RT}.GameInterop.Offset(ref __f{n}, {cls}, {fname}))";
            // O IL do mod faz `ldfld/stfld ENTITY::width` com o receiver sendo
            // um Item — mas no shim cada proxy e uma classe SOLTA (Item nao
            // herda Entity). Chamar `Entity.__set_width` por callvirt nesse
            // receiver estoura (o SetDefaults do mod parava no `width` e TODO
            // o resto dos campos ficava no default). Por isso os acessadores
            // que o IlPatcher usa sao STATIC e recebem IGameObject: qualquer
            // proxy do jogo serve como receiver.
            string selfAt = $"(self.Pointer + {RT}.GameInterop.Offset(ref __f{n}, {cls}, {fname}))";
            // Campo `T?`: o tipo tem de se chamar System.Nullable<T> para o
            // `ldfld/stfld` do mod resolver (ver FieldCs). Como é um valor,
            // sai por get/set comuns — nada de `ref T?` na propriedade.
            if (IsNullableField(f.Use))
            {
                string fcs = FieldCs(f.Use);
                sb.Append(ind).Append($"public {fcs} {id}\n").Append(ind).Append("{\n");
                sb.Append(ind).Append($"    get => *({fcs}*){at};\n");
                sb.Append(ind).Append($"    set => *({fcs}*){at} = value;\n");
                sb.Append(ind).Append("}\n");
                sb.Append(ind).Append($"public static {fcs} __get_{id}({RT}.IGameObject self) => *({fcs}*){selfAt};\n");
                sb.Append(ind).Append($"public static void __set_{id}({RT}.IGameObject self, {fcs} value) => *({fcs}*){selfAt} = value;\n");
                n++;
                continue;
            }
            if (f.Use.IsString || f.Use.Converted || f.Use.IsClassProxy)
            {
                sb.Append(ind).Append($"public {f.Use.Cs} {id}\n").Append(ind).Append("{\n");
                sb.Append(ind).Append($"    get => {f.Use.FromRaw($"*(nint*){at}")};\n");
                sb.Append(ind).Append($"    set => *(nint*){at} = (nint)({f.Use.ToRaw("value")});\n");
                sb.Append(ind).Append("}\n");
                sb.Append(ind).Append($"public static {f.Use.Cs} __get_{id}({RT}.IGameObject self) => {f.Use.FromRaw($"*(nint*){selfAt}")};\n");
                sb.Append(ind).Append($"public static void __set_{id}({RT}.IGameObject self, {f.Use.Cs} value) => *(nint*){selfAt} = (nint)({f.Use.ToRaw("value")});\n");
            }
            else
            {
                // ref properties nao podem ter set accessor (CS8147) e o nome
                // set_X fica reservado (CS0082) — metodo explicito __set_.
                sb.Append(ind).Append($"public ref {f.Use.Cs} {id} => ref *({f.Use.Cs}*){at};\n");
                sb.Append(ind).Append($"public static ref {f.Use.Cs} __ref_{id}({RT}.IGameObject self) => ref *({f.Use.Cs}*){selfAt};\n");
                sb.Append(ind).Append($"public static void __set_{id}({RT}.IGameObject self, {f.Use.Cs} value) => *({f.Use.Cs}*){selfAt} = value;\n");
            }
            n++;
        }

        foreach (PropModel p in plan.Props)
        {
            // `!Tipo.Membro` em types.txt: a propriedade é escrita à mão
            // (ex.: CreativeItemSacrificesCatalog.SacrificeCountNeededByItemId,
            // que o mod espera como Dictionary<int,int> gerenciado).
            if (HandWritten.TryGetValue(t.FullName ?? t.Name, out HashSet<string>? handProps) && handProps.Contains(p.Name))
            {
                emitter.Properties++;
                continue;
            }
            emitter.Properties++;
            string st = p.IsStatic ? "static " : "";
            string getName = $"__get_{p.Name.TrimStart('@')}", setName = $"__set_{p.Name.TrimStart('@')}";
            if (p.Get != null) emitter.EmitMethod(sb, ind, p.Get, cls, getName, "private");
            if (p.Set != null) emitter.EmitMethod(sb, ind, p.Set, cls, setName, "private");
            string type = p.Use.Cs;
            sb.Append(ind).Append($"public {st}{type} {p.Name}\n").Append(ind).Append("{\n");
            if (p.Get != null) sb.Append(ind).Append($"    get => {getName}();\n");
            if (p.Set != null) sb.Append(ind).Append($"    set => {setName}(value);\n");
            sb.Append(ind).Append("}\n");
        }

        bool openMethods = !t.IsValueType && OpenTypes.Contains(t.FullName ?? t.Name);
        foreach (MethodModel m in plan.Methods) emitter.EmitMethod(sb, ind, m, cls, m.Name, "public", openMethods && !m.IsStatic);
        foreach (EventModel e in plan.Events) emitter.EmitEvent(sb, ind, e.Name, e.Key, e.IsStatic, e.Add, e.Remove, e.Invoke, e.Delegate, cls);
        foreach (MethodModel m in plan.Ctors) emitter.EmitMethod(sb, ind, m, cls, "New");
    }
}
