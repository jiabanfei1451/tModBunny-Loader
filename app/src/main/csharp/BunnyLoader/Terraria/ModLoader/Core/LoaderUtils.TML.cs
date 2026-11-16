using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Terraria.ModLoader.Core;

/// <summary>
/// v101: so a parte de <c>LoaderUtils</c> que os mods chamam de fato.
/// FargowiltasSouls.AccessoryEffectPlayer.SetStaticDefaults IL_002B chama
/// <c>LoaderUtils::WhereMethodIsOverridden&lt;AccessoryEffect&gt;(IEnumerable`1&lt;!!0&gt;, Expression`1&lt;Func`2&lt;!!0,Delegate&gt;&gt;)</c>,
/// isto e, a versao com F == Delegate. O resto (ToOverrideQuery, cache
/// ConcurrentDictionary) nao e usado por nenhum mod carregado.
/// </summary>
public static class LoaderUtils
{
    public static IEnumerable<T> WhereMethodIsOverridden<T, F>(this IEnumerable<T> providers, Expression<Func<T, F>> expr)
        where F : Delegate
    {
        MethodInfo method = ExtractMethod(expr);
        return providers.Where(provider => provider is not null && Overrides(provider.GetType(), method));
    }

    public static bool HasOverride(this Type type, MethodInfo baseMethod) => Overrides(type, baseMethod);

    public static MethodInfo GetDerivedDefinition(this Type type, MethodInfo baseMethod)
    {
        if (baseMethod == null) return null;
        MethodInfo definition = baseMethod.GetBaseDefinition();
        foreach (MethodInfo candidate in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (candidate.GetBaseDefinition() == definition && candidate.DeclaringType != baseMethod.DeclaringType)
                return candidate;
        }

        return baseMethod;
    }

    static MethodInfo ExtractMethod(LambdaExpression expr)
    {
        if (expr == null) return null;
        Expression body = expr.Body;
        while (body is UnaryExpression unary && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked || unary.NodeType == ExpressionType.Quote))
            body = unary.Operand;

        switch (body)
        {
            case MethodCallExpression call:
                return call.Method;
            case MemberExpression member when member.Member is MethodInfo method:
                return method;
            case MemberExpression member when member.Member is PropertyInfo property:
                return property.GetGetMethod(true);
            default:
                return null;
        }
    }

    static bool Overrides(Type type, MethodInfo method)
    {
        if (type == null) return false;
        if (method == null) return true;

        Type declaring = method.DeclaringType;
        if (declaring == null) return true;
        if (declaring.IsInterface) return type.IsAssignableTo(declaring);

        MethodInfo definition = method.GetBaseDefinition();
        foreach (MethodInfo candidate in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (candidate.GetBaseDefinition() == definition && candidate.DeclaringType != declaring)
                return true;
        }

        return false;
    }
}
