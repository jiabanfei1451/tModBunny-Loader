using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CecilBody = Mono.Cecil.Cil.MethodBody;

namespace MonoMod.Cil;

/// <summary>
/// Contexto de edição de IL — a peça que o MonoMod do PC entrega ao
/// <see cref="Manipulator"/> de um <c>ILHook</c>.
///
/// Por que existe: o corpo do jogo está compilado em ARM64 pelo il2cpp, não há
/// IL para editar. Mas os mods (Luminance, FargowiltasSouls) chamam
/// <c>ILHook.Apply()</c> no construtor estático e um único
/// <c>MissingMethodException</c> ali derruba o tipo inteiro — foi assim que
/// <c>ScreenShaderFixer</c> morreu com "VTable setup of type ... failed" e
/// levou o Luminance (e portanto o pacote de almas) junto.
///
/// Estratégia: este contexto é um corpo REAL em Mono.Cecil (esqueleto com a
/// assinatura do alvo), o cursor edita de verdade, e o resultado vai para um
/// ledger em disco. Quando o corpo do jogo não existe, o esqueleto é
/// <c>ret</c> — os padrões não casam, a edição fica registrada como "sem
/// âncora" e o mod segue vivo. Se um dia houver como aplicar (detour nativo a
/// partir das instruções gravadas), é <see cref="ILContext.Applier"/> que
/// muda; nada mais no mod precisa saber.
/// </summary>
public class ILContext : IDisposable
{
    /// <summary>A assinatura que o MonoMod de verdade usa.</summary>
    public delegate void Manipulator(ILContext il);

    static readonly object ScratchGate = new();
    static ModuleDefinition? scratch;

    /// <summary>
    /// Módulo Cecil em memória só para segurar os TypeRefs das assinaturas.
    /// Compartilhado: criar um por hook custaria memória e não trazia nada.
    /// </summary>
    public static ModuleDefinition Scratch
    {
        get
        {
            lock (ScratchGate)
            {
                scratch ??= ModuleDefinition.CreateModule("BunnyLoader.ILContext", ModuleKind.Dll);
                return scratch;
            }
        }
    }

    /// <summary>
    /// Gancho de aplicação. Hoje devolve sempre <c>false</c> (o corpo editado
    /// é só registro). Quando existir um emissor — DynamicMethod + detour
    /// nativo por cima do ponteiro do il2cpp — ele entra aqui.
    /// </summary>
    internal static Func<MethodBase, CecilBody, bool>? Applier;

    readonly List<object?> references = new();

    public MethodBase Method { get; }

    public ModuleDefinition Module => Scratch;

    /// <summary>O corpo Cecil sob edição.</summary>
    public CecilBody Body { get; }

    /// <summary>Fachada do MonoMod real; o Cecil 0.11 expõe via <see cref="MethodBody.GetILProcessor"/>.</summary>
    public ILProcessor IL { get; }

    /// <summary>Atalho do MonoMod real para <c>Body.Instructions</c>.</summary>
    public IList<Instruction> Instrs => Body.Instructions;

    /// <summary>MonoMod real: quando true, o corpo não pode mais ser editado.</summary>
    public bool Readonly { get; set; }

    internal ILContext(MethodBase method, CecilBody body)
    {
        Method = method;
        Body = body;
        IL = body.GetILProcessor();
    }

    /// <summary>
    /// Monta o esqueleto: mesma assinatura do alvo (para <c>MatchLdarg</c> e
    /// contagem de argumentos fazerem sentido), corpo <c>ret</c>.
    /// </summary>
    internal static ILContext Create(MethodBase method, out string note)
    {
        note = "esqueleto";
        MethodDefinition def = new(
            Sanitize(method.Name),
            Mono.Cecil.MethodAttributes.Public |
            (method.IsStatic ? Mono.Cecil.MethodAttributes.Static : 0),
            StaticImport(typeof(void)));

        try
        {
            if (method is MethodInfo info && info.ReturnType != typeof(void))
            {
                def.ReturnType = StaticImport(info.ReturnType);
                note = "esqueleto com assinatura";
            }
            foreach (ParameterInfo p in method.GetParameters())
                def.Parameters.Add(new ParameterDefinition(Sanitize(p.Name ?? "arg"), Mono.Cecil.ParameterAttributes.None, StaticImport(p.ParameterType)));
        }
        catch (Exception e)
        {
            // Assinatura genérica/aberta não importa: segue com o esqueleto
            // mínimo. O que importa é o manipulador ter um corpo para editar.
            note = $"esqueleto parcial ({e.GetType().Name})";
        }

        CecilBody body = def.Body;
        bool hasReturn = def.ReturnType.MetadataType != MetadataType.Void && !ContainsGeneric(def.ReturnType);
        if (hasReturn)
        {
            body.InitLocals = true;
            VariableDefinition local = new(def.ReturnType);
            body.Variables.Add(local);
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloca_S, local));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
        else
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
        return new ILContext(method, body);
    }

    /// <summary>MonoMod real: roda o manipulador sobre este contexto.</summary>
    public void Invoke(Manipulator manipulator) => manipulator(this);

    public TypeReference Import(Type type) => Import(type, Scratch);

    /// <summary>Importa sem instância (o esqueleto é montado antes do contexto existir).</summary>
    static TypeReference StaticImport(Type type)
    {
        try { return Scratch.ImportReference(type); }
        catch { return Scratch.ImportReference(typeof(object)); }
    }

    public TypeReference Import(Type type, ModuleDefinition? module)
    {
        try { return (module ?? Scratch).ImportReference(type); }
        catch { return (module ?? Scratch).ImportReference(typeof(object)); }
    }

    public MethodReference Import(MethodBase method) => Scratch.ImportReference(method);

    public FieldReference Import(FieldInfo field) => Scratch.ImportReference(field);

    /// <summary>MonoMod real: guarda um delegate para o IL editado referenciar.</summary>
    public int AddReference(object? value)
    {
        references.Add(value);
        return references.Count - 1;
    }

    /// <summary>
    /// Alvo do <c>call</c> emitido por <c>ILCursor.EmitDelegate</c>. No MonoMod
    /// real isto produz um trampolim por delegate; aqui devolve o próprio valor
    /// guardado — o corpo nunca é executado, ele é registro.
    /// </summary>
    public static T? GetReference<T>(int id) => default;

    /// <summary>Assinatura do alvo, no formato que o ledger e o log usam.</summary>
    internal string Signature()
    {
        try
        {
            var sb = new StringBuilder();
            if (Method.DeclaringType != null) sb.Append(TypeName(Method.DeclaringType)).Append('.');
            sb.Append(Method.Name).Append('(');
            ParameterInfo[] ps = Method.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(TypeName(ps[i].ParameterType));
            }
            sb.Append(')');
            return sb.ToString();
        }
        catch { return Method.Name; }
    }

    static string TypeName(Type t)
    {
        try { return t.FullName ?? t.Name; } catch { return t.Name; }
    }

    /// <summary>Corpo atual em texto — é o que fica no ledger.</summary>
    internal string Dump()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Body.Instructions.Count; i++)
        {
            Instruction ins = Body.Instructions[i];
            sb.Append("  IL_").Append(i.ToString("X4")).Append(": ").Append(ins.OpCode);
            if (ins.Operand != null) sb.Append(' ').Append(Describe(ins.Operand));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static string Describe(object operand) => operand switch
    {
        string s => "\"" + s + "\"",
        Instruction i => $"IL_{i.Offset:X4}",
        MethodReference m => m.FullName,
        FieldReference f => f.FullName,
        TypeReference t => t.FullName,
        VariableDefinition v => v.ToString(),
        ParameterDefinition p => p.Name,
        _ => operand.ToString() ?? "",
    };

    static string Sanitize(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "il";
        var sb = new StringBuilder(name.Length);
        foreach (char c in name) sb.Append(c == '.' || c == '<' || c == '>' || c == '`' ? '_' : c);
        return sb.ToString();
    }

    static bool ContainsGeneric(TypeReference t) => t switch
    {
        GenericParameter => true,
        ArrayType a => ContainsGeneric(a.ElementType),
        ByReferenceType b => ContainsGeneric(b.ElementType),
        GenericInstanceType g => ContainsGeneric(g.ElementType),
        _ => false,
    };

    public void Dispose()
    {
        // Nada para soltar: o corpo vive no módulo em memória e o manipulador
        // do mod já terminou. Mantido para o `using` do mod compilar.
    }
}
