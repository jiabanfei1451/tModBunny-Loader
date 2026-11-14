using System;

namespace Terraria.ModLoader;

/// <summary>
/// Interface de marca do tModLoader (ver
/// <c>patches/tModLoader/Terraria/ModLoader/IAutoload.cs</c>): quem implementa
/// <c>IAutoload&lt;X&gt;</c> é carregado automaticamente por <c>X.Autoload</c>.
/// Não existe no dump — sem ela o mod nem carrega os tipos.
/// </summary>
public interface IAutoload<TImpl> where TImpl : IAutoloader
{
}

/// <summary>
/// O carregador referenciado por <see cref="IAutoload{TImpl}"/>. O tML usa
/// <c>static abstract</c> (C# 11): a implementação fica no tipo do mod.
/// </summary>
public interface IAutoloader
{
    static abstract void Autoload(Mod mod, Type type);
}
