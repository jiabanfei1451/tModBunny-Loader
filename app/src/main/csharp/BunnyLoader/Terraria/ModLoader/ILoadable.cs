using System;
using System.Reflection;

namespace Terraria.ModLoader;

/// <summary>
/// <see cref="ILoadable"/> do tML: herda <c>IAutoload&lt;ILoadable.AutoloadImpl&gt;</c>
/// e traz a classe aninhada <see cref="AutoloadImpl"/>. Mods do PC referenciam
/// esse tipo aninhado ao implementar <c>ILoadable</c>, então ele precisa
/// existir aqui também.
/// </summary>
public interface ILoadable : IAutoload<ILoadable.AutoloadImpl>
{
    bool IsLoadingEnabled(Mod mod) => true;

    void Load(Mod mod);

    void Unload();

    public class AutoloadImpl : IAutoloader
    {
        public static void Autoload(Mod mod, Type type)
        {
            if (type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    Type.EmptyTypes) is { } ctor)
                mod.AddContent((ILoadable)ctor.Invoke(null));
        }
    }
}
