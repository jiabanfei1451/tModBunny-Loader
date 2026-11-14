using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BunnyLoader.Interop;

public interface IGameObject
{
    IntPtr Pointer { get; }
}

public readonly struct GameObject : IGameObject, IEquatable<GameObject>
{
    public readonly IntPtr Pointer;

    public GameObject(IntPtr pointer) => Pointer = pointer;

    IntPtr IGameObject.Pointer => Pointer;

    public bool IsNull => Pointer == IntPtr.Zero;

    public string ClassName => GameInterop.ClassNameOf(Pointer);

    public bool Equals(GameObject other) => Pointer == other.Pointer;

    public override bool Equals(object? obj) => obj is GameObject o && Equals(o);

    public override int GetHashCode() => Pointer.GetHashCode();

    public static bool operator ==(GameObject a, GameObject b) => a.Pointer == b.Pointer;

    public static bool operator !=(GameObject a, GameObject b) => a.Pointer != b.Pointer;

    public override string ToString() => IsNull ? "null" : $"{ClassName}@{Pointer:x}";
}

public readonly unsafe struct GameString : IGameObject
{
    public readonly IntPtr Pointer;

    public GameString(IntPtr pointer) => Pointer = pointer;

    IntPtr IGameObject.Pointer => Pointer;

    public bool IsNull => Pointer == IntPtr.Zero;

    public int Length => Pointer == IntPtr.Zero ? 0 : *(int*)(Pointer + 0x10);

    public override string? ToString() => Read(Pointer);

    public static string? Read(IntPtr str)
    {
        if (str == IntPtr.Zero) return null;
        return new string((char*)(str + 0x14), 0, *(int*)(str + 0x10));
    }

    public static IntPtr New(string? value)
    {
        if (value == null) return IntPtr.Zero;
        fixed (char* chars = value) return Host.Native()->NewString(chars, value.Length);
    }

    public static implicit operator string?(GameString s) => Read(s.Pointer);

    public static implicit operator GameString(string? value) => From(value);

    public static GameString From(string? value) => new GameString(New(value));
}

[CollectionBuilder(typeof(GameArrayBuilder), nameof(GameArrayBuilder.Create))]
public readonly unsafe struct GameArray<T> : IGameObject, System.Collections.Generic.IEnumerable<T> where T : unmanaged
{
    System.Collections.Generic.IEnumerator<T> System.Collections.Generic.IEnumerable<T>.GetEnumerator() => ((System.Collections.Generic.IEnumerable<T>)(T[])this).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => ((T[])this).GetEnumerator();

    public readonly IntPtr Pointer;

    public GameArray(IntPtr pointer) => Pointer = pointer;

    public static GameArray<T> New(GameClass element, int length) => new(Host.Native()->NewArray(element.Handle, length));

    public GameArray<T> Resized(int length) => new(Host.Native()->ResizeArray(Pointer, length));

    IntPtr IGameObject.Pointer => Pointer;

    public bool IsNull => Pointer == IntPtr.Zero;

    public int Length => (int)*(long*)(Pointer + 0x18);

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)*(long*)(Pointer + 0x18)) ThrowIndex(index);
            return ref *(T*)(Pointer + 0x20 + (nint)index * sizeof(T));
        }
    }

    public Span<T> AsSpan() => new Span<T>((void*)(Pointer + 0x20), Length);

    public static implicit operator T[](GameArray<T> array) => array.IsNull ? Array.Empty<T>() : array.AsSpan().ToArray();

    public static implicit operator GameArray<T>(T[] values)
    {
        string name = typeof(T) == typeof(int) ? "Int32" : typeof(T) == typeof(ushort) ? "UInt16" : typeof(T) == typeof(short) ? "Int16" : typeof(T) == typeof(bool) ? "Boolean" : typeof(T) == typeof(byte) ? "Byte" : typeof(T) == typeof(float) ? "Single" : throw new NotSupportedException(typeof(T).Name);
        GameArray<T> array = New(new GameClass("System", name), values.Length);
        values.AsSpan().CopyTo(array.AsSpan());
        return array;
    }

    public Span<T>.Enumerator GetEnumerator() => AsSpan().GetEnumerator();

    // ---- acessadores "IL-friendly" -------------------------------------
    // O IL de um .tmod compilado no PC acessa `Main.debuff[i]` com
    // `ldelem.u1` / `stelem.i1`, que so funcionam em ARRAY gerenciado. Depois
    // que o IlPatcher troca o `ldsfld` por esta struct, `stelem` vira
    // InvalidProgramException. Estes metodos sao o alvo da reescrita.
    //
    // STATIC de proposito: chamar um metodo de INSTANCIA numa struct exige o
    // endereco (`&`) na pilha, e o que a reescrita tem ali e o VALOR — gerava
    // "Invalid IL code ... call 0x0a00025f" no aparelho. Passando a struct por
    // valor o empilhamento fica identico ao do `stelem`.

    /// <summary>Equivalente a <c>array[index]</c> (leitura).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Get(GameArray<T> array, int index) => array[index];

    /// <summary>Equivalente a <c>array[index] = value</c> (escrita no array nativo).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Set(GameArray<T> array, int index, T value) => array[index] = value;

    /// <summary>Equivalente a <c>array.Length</c> (o <c>ldlen</c> do mod).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetLength(GameArray<T> array) => array.Length;

    static void ThrowIndex(int index) => throw new IndexOutOfRangeException($"index {index} out of the game array");
}

/// <summary>
/// Array do jogo cujos elementos sao <b>referencias</b> (<c>Item[]</c>,
/// <c>Player[]</c>, <c>Texture2D[]</c>...).
///
/// No IL2CPP um array de referencias guarda <b>ponteiros</b> para os objetos,
/// nao os objetos em si — por isso o elemento e lido como <c>IntPtr</c> e
/// virou um proxy class pelo <see cref="GameObjectFactory"/>.
///
/// <see cref="GameArray{T}"/> continua sendo o array de <b>valores</b>
/// (<c>int[]</c>, <c>bool[]</c>, <c>Vector2[]</c>...), onde o elemento esta
/// embutido na memoria do array e por isso o indice pode devolver
/// <c>ref T</c> (escrita direta no array).
/// </summary>
public readonly unsafe struct GameRefArray<T> : IGameObject, System.Collections.Generic.IEnumerable<T> where T : class, IGameObject
{
    public readonly IntPtr Pointer;

    public GameRefArray(IntPtr pointer) => Pointer = pointer;

    IntPtr IGameObject.Pointer => Pointer;

    public bool IsNull => Pointer == IntPtr.Zero;

    public int Length => (int)*(long*)(Pointer + 0x18);

    /// <summary>
    /// Cria um array novo no jogo. O array de REFERENCIAS e alocado como
    /// `Il2CppObject*[]` (ponteiros), e cada slot e preenchido com o proxy
    /// do objeto — por isso o setter escreve o ponteiro, nao o proxy.
    /// </summary>
    public static GameRefArray<T> New(GameClass element, int length)
    {
        IntPtr array = Host.Native()->NewArray(element.Handle, length);
        if (array == IntPtr.Zero) throw new OutOfMemoryException($"could not allocate {element.FullName}[{length}]");
        return new GameRefArray<T>(array);
    }

    public GameRefArray<T> Resized(int length) => new(Host.Native()->ResizeArray(Pointer, length));

    /// <summary>
    /// O elemento do array. Como o array guarda um ponteiro, cada acesso cria
    /// um proxy novo — o proxy e so um endereco, entao comparar dois proxies
    /// do mesmo elemento usa <see cref="IGameObject.Pointer"/>.
    /// </summary>
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Length) throw new IndexOutOfRangeException($"index [{index}] out of the game array");
            IntPtr element = *(IntPtr*)(Pointer + 0x20 + (nint)index * sizeof(IntPtr));
            if (element == IntPtr.Zero) return null!;
            return (T)GameObjectFactory.Create(typeof(T), element);
        }
        set
        {
            if ((uint)index >= (uint)Length) throw new IndexOutOfRangeException($"index [{index}] out of the game array");
            *(IntPtr*)(Pointer + 0x20 + (nint)index * sizeof(IntPtr)) = value.Pointer;
        }
    }

    public static implicit operator GameRefArray<T>(GameArray<GameObject> array) => new(array.Pointer);

    /// <summary>
    /// Converte o array do jogo num array gerenciável. O código do tModLoader
    /// usa muito <c>T[] x = Language.FindAll(...)</c> / <c>NPC[] y = ...</c>:
    /// sem esta conversão o mod nem compila.
    /// </summary>
    public static implicit operator T[](GameRefArray<T> array)
    {
        if (array.IsNull) return Array.Empty<T>();
        int length = array.Length;
        var result = new T[length];
        for (int i = 0; i < length; i++) result[i] = array[i];
        return result;
    }

    public System.Collections.Generic.IEnumerator<T> GetEnumerator()
    {
        int length = Length;
        for (int i = 0; i < length; i++) yield return this[i];
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Leitura para o IL reescrito (o <c>ldelem.ref</c> do mod).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Get(GameRefArray<T> array, int index) => array[index];

    /// <summary>Escrita no array nativo (o <c>stelem.ref</c> do mod).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Set(GameRefArray<T> array, int index, T value) => array[index] = value;

    /// <summary>O <c>ldlen</c> do mod.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetLength(GameRefArray<T> array) => array.Length;
}

public static class GameArrayBuilder
{
    public static GameArray<T> Create<T>(ReadOnlySpan<T> items) where T : unmanaged
    {
        GameArray<T> array = GameArray<T>.New(ElementClass(typeof(T)), items.Length);
        items.CopyTo(array.AsSpan());
        return array;
    }

    static GameClass ElementClass(Type type) => Type.GetTypeCode(type) switch
    {
        TypeCode.Boolean => new GameClass("System", "Boolean"),
        TypeCode.Byte => new GameClass("System", "Byte"),
        TypeCode.SByte => new GameClass("System", "SByte"),
        TypeCode.Int16 => new GameClass("System", "Int16"),
        TypeCode.UInt16 => new GameClass("System", "UInt16"),
        TypeCode.Int32 => new GameClass("System", "Int32"),
        TypeCode.UInt32 => new GameClass("System", "UInt32"),
        TypeCode.Int64 => new GameClass("System", "Int64"),
        TypeCode.UInt64 => new GameClass("System", "UInt64"),
        TypeCode.Single => new GameClass("System", "Single"),
        TypeCode.Double => new GameClass("System", "Double"),
        TypeCode.Char => new GameClass("System", "Char"),
        _ => type.IsValueType && type.Namespace != null ? new GameClass(type.Namespace, type.Name) : throw new NotSupportedException($"a collection expression can only make a game array of numbers, bools or game structs, not {type.Name}"),
    };
}

public readonly unsafe struct GameArray2D<T> : IGameObject where T : unmanaged
{
    public readonly IntPtr Pointer;

    public GameArray2D(IntPtr pointer) => Pointer = pointer;

    IntPtr IGameObject.Pointer => Pointer;

    public bool IsNull => Pointer == IntPtr.Zero;

    public int GetLength(int dimension) => (int)*(long*)(*(IntPtr*)(Pointer + 0x10) + dimension * 16);

    public ref T this[int i, int j]
    {
        get
        {
            long* bounds = *(long**)(Pointer + 0x10);
            long rows = bounds[0], cols = bounds[2];
            if ((ulong)i >= (ulong)rows || (ulong)j >= (ulong)cols) throw new IndexOutOfRangeException($"index [{i}, {j}] out of the game array");
            return ref *(T*)(Pointer + 0x20 + (nint)(i * cols + j) * sizeof(T));
        }
    }
}

/// <summary>
/// Array 2D do jogo cujos elementos sao <b>referencias</b>. Mesmo principio
/// do <see cref="GameRefArray{T}"/>: cada celula guarda um ponteiro para o
/// objeto, e o proxy class e montado na leitura.
/// </summary>
public readonly unsafe struct GameRefArray2D<T> : IGameObject where T : class, IGameObject
{
    public readonly IntPtr Pointer;

    public GameRefArray2D(IntPtr pointer) => Pointer = pointer;

    IntPtr IGameObject.Pointer => Pointer;

    public bool IsNull => Pointer == IntPtr.Zero;

    public int GetLength(int dimension) => (int)*(long*)(*(IntPtr*)(Pointer + 0x10) + dimension * 16);

    public T this[int i, int j]
    {
        get
        {
            long* bounds = *(long**)(Pointer + 0x10);
            long rows = bounds[0], cols = bounds[2];
            if ((ulong)i >= (ulong)rows || (ulong)j >= (ulong)cols) throw new IndexOutOfRangeException($"index [{i}, {j}] out of the game array");
            IntPtr element = *(IntPtr*)(Pointer + 0x20 + (nint)(i * cols + j) * sizeof(IntPtr));
            if (element == IntPtr.Zero) return null!;
            return (T)GameObjectFactory.Create(typeof(T), element);
        }
        set
        {
            long* bounds = *(long**)(Pointer + 0x10);
            long rows = bounds[0], cols = bounds[2];
            if ((ulong)i >= (ulong)rows || (ulong)j >= (ulong)cols) throw new IndexOutOfRangeException($"index [{i}, {j}] out of the game array");
            *(IntPtr*)(Pointer + 0x20 + (nint)(i * cols + j) * sizeof(IntPtr)) = value.Pointer;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct GameNullable<T> where T : unmanaged
{

    bool hasValue;
    T value;

    public GameNullable(T value)
    {
        hasValue = true;
        this.value = value;
    }

    public readonly bool HasValue => hasValue;

    public readonly T Value => hasValue ? value : throw new InvalidOperationException("game nullable has no value");

    public readonly T GetValueOrDefault(T fallback = default) => hasValue ? value : fallback;

    public static implicit operator T?(GameNullable<T> v) => v.hasValue ? v.value : null;

    public static implicit operator GameNullable<T>(T? v) => v.HasValue ? new GameNullable<T>(v.Value) : default;

    public static implicit operator GameNullable<T>(T v) => new GameNullable<T>(v);

    public override readonly string ToString() => hasValue ? value.ToString() ?? "" : "null";
}

public sealed unsafe class GameHandle : IDisposable
{
    uint handle;

    public GameHandle(IntPtr obj) => handle = Host.Native()->GcHandleNew(obj, 0);

    public IntPtr Target => handle == 0 ? IntPtr.Zero : Host.Native()->GcHandleTarget(handle);

    public void Dispose()
    {
        if (handle != 0) Host.Native()->GcHandleFree(handle);
        handle = 0;
        GC.SuppressFinalize(this);
    }

    ~GameHandle()
    {
        if (handle != 0) Host.Native()->GcHandleFree(handle);
    }
}
