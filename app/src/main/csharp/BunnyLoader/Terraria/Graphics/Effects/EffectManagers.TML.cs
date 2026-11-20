using System;
using System.Collections.Concurrent;
using BunnyLoader;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework;

namespace Terraria.Graphics.Effects;

internal static unsafe class EffectManagerCalls
{
    static readonly ConcurrentDictionary<(IntPtr, string, int), IntPtr> Methods = new();
    static GameArray<GameObject> noArgs;

    static IntPtr Method(IntPtr self, string name, int argc, out IntPtr info)
    {
        if (self == IntPtr.Zero) throw new NullReferenceException($"EffectManager.{name}: game object is null");
        IntPtr cls = *(IntPtr*)self;
        info = Methods.GetOrAdd((cls, name, argc), key =>
        {
            byte* n = Host.Utf8(key.Item2);
            try
            {
                IntPtr method = Host.Native()->FindMethod(key.Item1, n, key.Item3);
                if (method == IntPtr.Zero) throw new MissingMethodException($"EffectManager: no {key.Item2} with {key.Item3} argument(s)");
                return method;
            }
            finally { Host.FreeUtf8(n); }
        });
        return Host.Native()->MethodPointer(info);
    }

    static IntPtr NoArgs(object[] args)
    {
        if (args.Length > 0) throw new NotSupportedException("EffectManager: extra activation arguments are not supported on mobile");
        if (noArgs.IsNull) noArgs = GameArray<GameObject>.New(new GameClass("System", "Object"), 0);
        return noArgs.Pointer;
    }

    internal static IntPtr Get(IntPtr self, string key)
    {
        IntPtr fn = Method(self, "get_Item", 1, out IntPtr info);
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg(((GameString)key).Pointer);
        call.Arg(info);
        return (IntPtr)call.Invoke(fn);
    }

    internal static void Set(IntPtr self, string key, IntPtr value)
    {
        IntPtr fn = Method(self, "set_Item", 2, out IntPtr info);
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg(((GameString)key).Pointer);
        call.Arg(value);
        call.Arg(info);
        call.Invoke(fn);
    }

    internal static IntPtr Activate(IntPtr self, string name, Vector2 position, object[] args)
    {
        IntPtr fn = Method(self, "Activate", 3, out IntPtr info);
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg(((GameString)name).Pointer);
        call.Arg(position.X);
        call.Arg(position.Y);
        call.Arg(NoArgs(args));
        call.Arg(info);
        return (IntPtr)call.Invoke(fn);
    }

    internal static void Deactivate(IntPtr self, string name, object[] args)
    {
        IntPtr fn = Method(self, "Deactivate", 2, out IntPtr info);
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg(((GameString)name).Pointer);
        call.Arg(NoArgs(args));
        call.Arg(info);
        call.Invoke(fn);
    }
}

/// <summary>
/// O tModLoader expoe FilterManager/SkyManager/OverlayManager como subclasses
/// de `EffectManager&lt;T&gt;`, e o IL dos mods faz
/// `callvirt EffectManager`1&lt;CustomSky&gt;::set_Item(string, !0)` com receiver
/// SkyManager. Sem este tipo base o metodo do mod nao JITa. A implementacao e
/// a mesma que ja existia nos tres managers, mais um dicionario managed para
/// os efeitos registrados por mod (que nao tem objeto nativo).
/// </summary>
public abstract class EffectManager<T> where T : class
{
    static readonly ConcurrentDictionary<string, T> Local = new();

    protected abstract IntPtr ManagerPointer { get; }

    protected abstract T Wrap(IntPtr pointer);

    static IntPtr PointerOf(T value) => value is IGameObject o ? o.Pointer : IntPtr.Zero;

    static bool IsLocal(T value)
    {
        IntPtr pointer = PointerOf(value);
        return pointer == IntPtr.Zero || ManagedEffects.IsManaged(pointer);
    }

    public T this[string key]
    {
        get
        {
            if (key != null && Local.TryGetValue(key, out T local)) return local;
            return Wrap(EffectManagerCalls.Get(ManagerPointer, key));
        }
        set
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null)
            {
                Local.TryRemove(key, out _);
                EffectManagerCalls.Set(ManagerPointer, key, IntPtr.Zero);
                return;
            }
            // Efeito de mod: fica so no dicionario managed. Mandar o ponteiro
            // falso (bloco do ManagedEffects) para o Dictionary nativo faria o
            // jogo ler lixo como CustomSky.
            if (IsLocal(value))
            {
                Local[key] = value;
                return;
            }
            Local.TryRemove(key, out _);
            EffectManagerCalls.Set(ManagerPointer, key, PointerOf(value));
        }
    }

    public T Activate(string name, Vector2 position = default, params object[] args)
    {
        if (name != null && Local.TryGetValue(name, out T local))
        {
            (local as IManagedEffect)?.ManagedActivate(position, args ?? Array.Empty<object>());
            return local;
        }
        return Wrap(EffectManagerCalls.Activate(ManagerPointer, name, position, args ?? Array.Empty<object>()));
    }

    public void Deactivate(string name, params object[] args)
    {
        if (name != null && Local.TryGetValue(name, out T local))
        {
            (local as IManagedEffect)?.ManagedDeactivate(args ?? Array.Empty<object>());
            return;
        }
        EffectManagerCalls.Deactivate(ManagerPointer, name, args ?? Array.Empty<object>());
    }
}

public sealed partial class FilterManager : EffectManager<Filter>
{
    protected override IntPtr ManagerPointer => Pointer;

    protected override Filter Wrap(IntPtr pointer) => new(pointer);
}

public sealed partial class SkyManager : EffectManager<CustomSky>
{
    protected override IntPtr ManagerPointer => Pointer;

    protected override CustomSky Wrap(IntPtr pointer) => new(pointer);
}

public sealed partial class OverlayManager : EffectManager<Overlay>
{
    protected override IntPtr ManagerPointer => Pointer;

    protected override Overlay Wrap(IntPtr pointer) => new(pointer);
}
