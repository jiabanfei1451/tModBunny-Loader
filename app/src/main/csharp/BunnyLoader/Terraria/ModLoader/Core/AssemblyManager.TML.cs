using System;
using System.Reflection;

namespace Terraria.ModLoader.Core;

/// <summary>
/// v101: <c>AssemblyManager.GetLoadableTypes</c> (Core/AssemblyManager.cs:350 do tML).
/// Devolve os tipos que carregam, tolerando <see cref="ReflectionTypeLoadException"/>.
/// </summary>
public static class AssemblyManager
{
    public static Type[] GetLoadableTypes(Assembly assembly)
    {
        if (assembly == null) return Array.Empty<Type>();

        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            Type[] types = e.Types;
            int count = 0;
            for (int i = 0; i < types.Length; i++)
                if (types[i] != null) count++;

            Type[] result = new Type[count];
            int next = 0;
            for (int i = 0; i < types.Length; i++)
                if (types[i] != null) result[next++] = types[i];

            return result;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}
