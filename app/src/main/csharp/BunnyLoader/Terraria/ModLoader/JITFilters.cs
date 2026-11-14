using System;
using System.Linq;
using System.Reflection;

namespace Terraria.ModLoader;

/// <summary>
/// Roda no lugar do JIT de carregamento do tModLoader de PC.
///
/// O jogo de PC JIT-a tudo o que o mod tem antes de ligar o conteudo, e usa
/// estes atributos para decidir o que pular. Aqui nada e JIT-ado por filtro
/// (quem decide o que pode ser compilado e o <see cref="IlPatcher"/>, que
/// reescreve o que nao existe neste jogo), mas os TIPOS precisam existir:
/// um mod anotado com <c>[NoJIT]</c> referencia o tipo no metadata e o Mono
/// recusa o assembly inteiro com
///
/// <code>
/// TypeLoadException: Could not resolve type with token 0100000d from typeref
///   (expected class 'Terraria.ModLoader.MemberJitAttribute')
/// </code>
///
/// Foi assim que a Luminance parou de carregar na v47: 155 tipos do mod
/// carregaram, mas o assembly nao entrava. A regra e a mesma do contrato
/// 1.4.4.9 (<c>patches/tModLoader/Terraria/ModLoader/JITFilters.cs</c>),
/// sem alteracao de comportamento.
/// </summary>
public class PreJITFilter
{
    public virtual bool ShouldJIT(MemberInfo member) =>
        member.GetCustomAttributes<MemberJitAttribute>().All(a => a.ShouldJIT(member));
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property, Inherited = true)]
public abstract class MemberJitAttribute : Attribute
{
    public virtual bool ShouldJIT(MemberInfo member) => true;
}

/// <summary>Exclui a classe/metodo/propriedade do JIT de carregamento.</summary>
public class NoJITAttribute : MemberJitAttribute
{
    public override bool ShouldJIT(MemberInfo member) => false;
}

/// <summary>
/// Exclui do JIT de carregamento a menos que os mods citados estejam ligados.
/// Usado em membro que referencia tipo de outro mod (weak reference).
/// </summary>
public sealed class JITWhenModsEnabledAttribute : MemberJitAttribute
{
    public readonly string[] Names;

    public JITWhenModsEnabledAttribute(params string[] names)
    {
        Names = names ?? throw new ArgumentNullException(nameof(names));
    }

    public override bool ShouldJIT(MemberInfo member) => Names.All(ModLoader.HasMod);
}
