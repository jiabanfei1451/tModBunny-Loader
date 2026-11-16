using System;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MonoMod.Cil;

/// <summary>
/// Predicados de casamento de instrução (extensões de <see cref="Instruction"/>).
///
/// O único consumido hoje é <c>MatchCallOrCallvirt</c>/<c>MatchLdfld</c>, mas o
/// conjunto vai junto: a próxima família de mods usa os mesmos nomes, e um
/// <c>MissingMethodException</c> aqui derrubaria o tipo que faz o patch inteiro
/// (mesmo padrão do <c>SoundStyle.init</c>).
///
/// A comparação por <c>string</c> é TOLERANTE de propósito. O MonoMod compara
/// o "ID" do membro (<c>RetType Namespace.Type::Name(args)</c>), e há mods que
/// passam desde o nome curto até a assinatura inteira; aceitar as três formas
/// evita falso negativo silencioso, que num ledger seria pior que um casamento
/// a mais.
/// </summary>
public static class ILPatternMatchingExt
{
    // ------------------------------------------------------------- chamadas

    public static bool MatchCall(this Instruction instr, string name) =>
        instr.OpCode.Code == Code.Call && Method(instr, name);

    public static bool MatchCall(this Instruction instr, MethodBase method) =>
        instr.OpCode.Code == Code.Call && Method(instr, method?.Name);

    public static bool MatchCall(this Instruction instr, Func<MethodReference, bool> predicate) =>
        instr.OpCode.Code == Code.Call && instr.Operand is MethodReference m && predicate(m);

    public static bool MatchCallvirt(this Instruction instr, string name) =>
        instr.OpCode.Code == Code.Callvirt && Method(instr, name);

    public static bool MatchCallvirt(this Instruction instr, MethodBase method) =>
        instr.OpCode.Code == Code.Callvirt && Method(instr, method?.Name);

    public static bool MatchCallvirt(this Instruction instr, Func<MethodReference, bool> predicate) =>
        instr.OpCode.Code == Code.Callvirt && instr.Operand is MethodReference m && predicate(m);

    public static bool MatchCallOrCallvirt(this Instruction instr, string name) =>
        (instr.OpCode.Code is Code.Call or Code.Callvirt) && Method(instr, name);

    public static bool MatchCallOrCallvirt(this Instruction instr, MethodBase method) =>
        (instr.OpCode.Code is Code.Call or Code.Callvirt) && Method(instr, method?.Name);

    public static bool MatchCallOrCallvirt(this Instruction instr, Func<MethodReference, bool> predicate) =>
        (instr.OpCode.Code is Code.Call or Code.Callvirt) && instr.Operand is MethodReference m && predicate(m);

    public static bool MatchNewobj(this Instruction instr, string name) =>
        instr.OpCode.Code == Code.Newobj && Method(instr, name);

    public static bool MatchNewobj(this Instruction instr, Func<MethodReference, bool> predicate) =>
        instr.OpCode.Code == Code.Newobj && instr.Operand is MethodReference m && predicate(m);

    // ------------------------------------------------------------- campos

    public static bool MatchLdfld(this Instruction instr, string name) =>
        instr.OpCode.Code == Code.Ldfld && Field(instr, name);

    public static bool MatchLdfld(this Instruction instr, FieldInfo field) =>
        instr.OpCode.Code == Code.Ldfld && instr.Operand is FieldReference f && SameField(f, field?.DeclaringType?.FullName, field?.Name);

    public static bool MatchLdfld(this Instruction instr, Func<FieldReference, bool> predicate) =>
        instr.OpCode.Code == Code.Ldfld && instr.Operand is FieldReference f && predicate(f);

    public static bool MatchStfld(this Instruction instr, string name) =>
        instr.OpCode.Code == Code.Stfld && Field(instr, name);

    public static bool MatchLdsfld(this Instruction instr, string name) =>
        instr.OpCode.Code == Code.Ldsfld && Field(instr, name);

    public static bool MatchStsfld(this Instruction instr, string name) =>
        instr.OpCode.Code == Code.Stsfld && Field(instr, name);

    // ------------------------------------------------------------- constantes

    public static bool MatchLdstr(this Instruction instr, string value) =>
        instr.OpCode.Code == Code.Ldstr && instr.Operand is string s && string.Equals(s, value, StringComparison.Ordinal);

    public static bool MatchLdstr(this Instruction instr, Func<string, bool> predicate) =>
        instr.OpCode.Code == Code.Ldstr && instr.Operand is string s && predicate(s);

    public static bool MatchLdc_I4(this Instruction instr, int value) =>
        instr.OpCode.Code is Code.Ldc_I4 or Code.Ldc_I4_S or Code.Ldc_I4_0 or Code.Ldc_I4_1
            or Code.Ldc_I4_2 or Code.Ldc_I4_3 or Code.Ldc_I4_4 or Code.Ldc_I4_5
            or Code.Ldc_I4_6 or Code.Ldc_I4_7 or Code.Ldc_I4_8 or Code.Ldc_I4_M1
        && Constant(instr) == value;

    static int? Constant(Instruction instr) => instr.OpCode.Code switch
    {
        Code.Ldc_I4_M1 => -1,
        Code.Ldc_I4_0 => 0,
        Code.Ldc_I4_1 => 1,
        Code.Ldc_I4_2 => 2,
        Code.Ldc_I4_3 => 3,
        Code.Ldc_I4_4 => 4,
        Code.Ldc_I4_5 => 5,
        Code.Ldc_I4_6 => 6,
        Code.Ldc_I4_7 => 7,
        Code.Ldc_I4_8 => 8,
        Code.Ldc_I4 or Code.Ldc_I4_S => instr.Operand is sbyte sb ? sb : instr.Operand as int?,
        _ => null,
    };

    // ------------------------------------------------------------- variáveis

    public static bool MatchLdloc(this Instruction instr, VariableDefinition variable) =>
        instr.OpCode.Code is Code.Ldloc or Code.Ldloc_S or Code.Ldloc_0 or Code.Ldloc_1
            or Code.Ldloc_2 or Code.Ldloc_3 && SameVariable(instr, variable);

    public static bool MatchLdloc(this Instruction instr, int index) =>
        instr.OpCode.Code is Code.Ldloc or Code.Ldloc_S or Code.Ldloc_0 or Code.Ldloc_1
            or Code.Ldloc_2 or Code.Ldloc_3 && VariableIndex(instr) == index;

    public static bool MatchStloc(this Instruction instr, VariableDefinition variable) =>
        instr.OpCode.Code is Code.Stloc or Code.Stloc_S or Code.Stloc_0 or Code.Stloc_1
            or Code.Stloc_2 or Code.Stloc_3 && SameVariable(instr, variable);

    public static bool MatchStloc(this Instruction instr, int index) =>
        instr.OpCode.Code is Code.Stloc or Code.Stloc_S or Code.Stloc_0 or Code.Stloc_1
            or Code.Stloc_2 or Code.Stloc_3 && VariableIndex(instr) == index;

    static int? VariableIndex(Instruction instr) => instr.OpCode.Code switch
    {
        Code.Ldloc_0 or Code.Stloc_0 => 0,
        Code.Ldloc_1 or Code.Stloc_1 => 1,
        Code.Ldloc_2 or Code.Stloc_2 => 2,
        Code.Ldloc_3 or Code.Stloc_3 => 3,
        _ => instr.Operand is VariableDefinition v ? v.Index : null,
    };

    static bool SameVariable(Instruction instr, VariableDefinition? variable) =>
        variable != null && instr.Operand is VariableDefinition v && ReferenceEquals(v, variable);

    public static bool MatchLdarg(this Instruction instr, int index) =>
        instr.OpCode.Code is Code.Ldarg or Code.Ldarg_S or Code.Ldarg_0 or Code.Ldarg_1
            or Code.Ldarg_2 or Code.Ldarg_3 && ArgIndex(instr) == index;

    public static bool MatchStarg(this Instruction instr, int index) =>
        instr.OpCode.Code is Code.Starg or Code.Starg_S && ArgIndex(instr) == index;

    static int? ArgIndex(Instruction instr) => instr.OpCode.Code switch
    {
        Code.Ldarg_0 => 0,
        Code.Ldarg_1 => 1,
        Code.Ldarg_2 => 2,
        Code.Ldarg_3 => 3,
        _ => instr.Operand is ParameterDefinition p ? p.Index : null,
    };

    // ------------------------------------------------------------- fluxo

    public static bool MatchRet(this Instruction instr) => instr.OpCode.Code == Code.Ret;

    public static bool MatchNop(this Instruction instr) => instr.OpCode.Code == Code.Nop;

    public static bool MatchBr(this Instruction instr, ILLabel label) =>
        instr.OpCode.Code == Code.Br || instr.OpCode.Code == Code.Br_S
            ? instr.Operand is Instruction target && ReferenceEquals(target, label.Target)
            : false;

    public static bool MatchBrtrue(this Instruction instr, ILLabel label) =>
        (instr.OpCode.Code is Code.Brtrue or Code.Brtrue_S) &&
        instr.Operand is Instruction target && ReferenceEquals(target, label.Target);

    public static bool MatchBrfalse(this Instruction instr, ILLabel label) =>
        (instr.OpCode.Code is Code.Brfalse or Code.Brfalse_S) &&
        instr.Operand is Instruction target && ReferenceEquals(target, label.Target);

    public static bool MatchOpCode(this Instruction instr, params Code[] codes)
    {
        foreach (Code code in codes) if (instr.OpCode.Code == code) return true;
        return false;
    }

    public static bool MatchLdtoken(this Instruction instr, string name) =>
        instr.OpCode.Code == Code.Ldtoken && instr.Operand is TypeReference t && Text(t.FullName, name);

    public static bool MatchAny(this Instruction instr) => true;

    // ------------------------------------------------------------- helpers

    static bool Method(Instruction instr, string? name) =>
        instr.Operand is MethodReference m && Text(Text(m.FullName) + "|" + m.Name + "|" + Text(m.DeclaringType?.FullName + "::" + m.Name), name);

    static bool Field(Instruction instr, string? name) =>
        instr.Operand is FieldReference f && Text(f.FullName + "|" + f.Name + "|" + f.DeclaringType?.FullName + "::" + f.Name, name);

    static bool SameField(FieldReference field, string? declaringType, string? name) =>
        (name == null || string.Equals(field.Name, name, StringComparison.Ordinal)) &&
        (declaringType == null || string.Equals(field.DeclaringType?.FullName, declaringType, StringComparison.Ordinal));

    static string Text(string? value) => value ?? "";

    /// <summary>
    /// "Igual" ou "contém": ver o comentário da classe. Nome vazio nunca casa,
    /// para não transformar um filtro esquecido em casamento universal.
    /// </summary>
    static bool Text(string haystack, string? needle)
    {
        if (string.IsNullOrEmpty(needle)) return false;
        if (string.Equals(haystack, needle, StringComparison.Ordinal)) return true;
        return haystack.IndexOf(needle, StringComparison.Ordinal) >= 0;
    }
}
