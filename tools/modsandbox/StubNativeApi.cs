using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace BunnyLoader.Sandbox;

/// <summary>
/// Um `NativeApi` falso, montado por reflexao sobre a MESMA struct que o
/// BunnyLoader.dll compila. Nao ha Unity/IL2CPP aqui: cada slot de funcao
/// aponta para um no-op que devolve um ponteiro para um bloco de memoria
/// "de faz de conta" (1 MB zerado). `FieldOffset` distribui offsets estaveis
/// dentro dele, entao `Item.damage = 100` (que e uma propriedade `ref` sobre
/// `Pointer + offset`) ESCREVE DE VERDADE nesse bloco e o sandbox consegue
/// ler de volta o que o SetDefaults fez.
///
/// Tudo roda num processo filho: se algum slot nao coberto derrubar o
/// processo, o pai sobrevive e reporta onde parou.
/// </summary>
internal static unsafe class StubNativeApi
{
    const int FakeSize = 1024 * 1024;

    static byte* api;
    static byte* fakeObject;
    static byte* staticSlot; // 8 bytes: guarda o ponteiro de um "objeto" falso
    static byte* trampoline; // "ret" de arm64: o hook instalado aponta para ca
    static readonly List<IntPtr> hookSlots = new();
    static int nextOffset = 8;
    static int itemIdCount = 6196; // lido do shim em Install()
    static int nextItemType;

    internal static readonly Dictionary<string, int> Offsets = new(StringComparer.Ordinal);
    internal static readonly Dictionary<int, string> RegisteredItems = new();
    internal static readonly List<string> NativeLog = new();
    internal static int LayoutNoise;

    static string manifestId = "mod";
    static string displayName = "Sandbox Mod";
    static string version = "0.0";
    static string[] dependencies = Array.Empty<string>();

    internal static nint Api => (nint)api;
    internal static nint FakeObject => (nint)fakeObject;

    internal static void ConfigureManifest(string id, string name, string ver, string[] deps)
    {
        manifestId = id;
        displayName = name;
        version = ver;
        dependencies = deps;
    }

    /// <summary>Preenche a struct e publica o ponteiro em BunnyLoader.Host.Api.</summary>
    internal static void Install(Assembly shim)
    {
        Type native = shim.GetType("BunnyLoader.Interop.NativeApi", throwOnError: true)!;
        int size = Marshal.SizeOf(native);
        api = (byte*)NativeMemory.AllocZeroed((nuint)size);
        fakeObject = (byte*)NativeMemory.AllocZeroed(FakeSize);
        staticSlot = (byte*)NativeMemory.AllocZeroed(8);
        *(nint*)staticSlot = (nint)fakeObject;
        trampoline = MakeTrampoline();

        // O primeiro campo e `uint Size`, nao um ponteiro de funcao.
        *(uint*)api = (uint)size;

        // Primeiro TODOS os slots recebem o no-op generico. Assim nenhuma
        // chamada imprevista cai em nullptr (o que seria um crash nativo).
        nint nop = (nint)(delegate* unmanaged<IntPtr>)&Nop;
        foreach (FieldInfo f in native.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.Name == "Size") continue;
            Set(native, f.Name, nop);
        }

        // Depois os slots que precisam de comportamento real.
        Set(native, "Log", (nint)(delegate* unmanaged<int, byte*, void>)&LogStub);
        Set(native, "FieldOffset", (nint)(delegate* unmanaged<IntPtr, byte*, int>)&FieldOffsetStub);
        Set(native, "CheckLayout", (nint)(delegate* unmanaged<IntPtr, int*, int, int>)&CheckLayoutStub);
        Set(native, "IsGameThread", (nint)(delegate* unmanaged<int>)&GameThreadStub);
        Set(native, "ModManifestField", (nint)(delegate* unmanaged<byte*, int, byte*, int, int>)&ManifestStub);
        Set(native, "RegisterItem",
            (nint)(delegate* unmanaged<byte*, byte*, byte*, byte*, byte*, int>)&RegisterItemStub);
        Set(native, "LastCallError", (nint)(delegate* unmanaged<byte*>)&NullNop);
        // Sem IL2CPP nao ha tamanho real de struct: devolver -1 e tratar as
        // linhas de "struct layout differs" como ruido do sandbox.
        Set(native, "ClassValueSize", (nint)(delegate* unmanaged<IntPtr, int>)&ClassSizeStub);
        // Metodo "encontrado" precisa apontar para codigo executavel, senao
        // um Call posterior saltaria para dentro da memoria de faz de conta.
        Set(native, "MethodPointer", (nint)(delegate* unmanaged<IntPtr, IntPtr>)&MethodPointerStub);
        Set(native, "FindMethodBySignature", (nint)(delegate* unmanaged<IntPtr, byte*, IntPtr>)&FindMethodStub);
        Set(native, "FindMethod", (nint)(delegate* unmanaged<IntPtr, byte*, int, IntPtr>)&FindMethodStub3);
        // GameShaders.Armor etc.: o proxy le `*(T*)StaticFieldAddress(...)`,
        // onde T so contem um ponteiro. Devolvemos um slot que CONTEM o
        // ponteiro do objeto falso, em vez do objeto falso em si.
        Set(native, "StaticFieldAddress", (nint)(delegate* unmanaged<IntPtr, IntPtr>)&StaticFieldAddressStub);
        Set(native, "StaticFieldGet", (nint)(delegate* unmanaged<IntPtr, void*, void>)&StaticFieldGetStub);
        Set(native, "InstallHook", (nint)(delegate* unmanaged<IntPtr, IntPtr, IntPtr*>)&InstallHookStub);

        // ItemLoader.Register faz `index = type - ItemID.Count`; se o type
        // falso ficar abaixo de Count o indice vira negativo e estoura a
        // lista. No aparelho registerModItem numera a partir de Count.
        Type? itemId = shim.GetType("Terraria.ID.ItemID");
        FieldInfo? count = itemId?.GetField("Count", BindingFlags.Public | BindingFlags.Static);
        // `ItemID.Count` virou `static readonly` (v32: um `ldsfld` de const
        // nao liga no Mono). `GetRawConstantValue()` so vale para `const`;
        // chama-lo num campo readonly lanca InvalidOperationException e
        // derruba o --run inteiro antes de carregar o mod.
        if (count != null)
            itemIdCount = Convert.ToInt32(count.IsLiteral
                ? count.GetRawConstantValue() ?? count.GetValue(null)
                : count.GetValue(null));

        Type host = shim.GetType("BunnyLoader.Host", throwOnError: true)!;
        FieldInfo apiField = host.GetField("Api", BindingFlags.NonPublic | BindingFlags.Static)!;
        apiField.SetValue(null, Pointer.Box((void*)api, apiField.FieldType));
    }

    static void Set(Type native, string name, nint fn)
    {
        FieldInfo? f = native.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (f == null) return;
        *(nint*)(api + Marshal.OffsetOf(native, name).ToInt32()) = fn;
    }

    internal static int ReadInt32(string field) =>
        Offsets.TryGetValue(field, out int off) ? Marshal.ReadInt32((nint)fakeObject + off) : 0;

    // ---- slots -----------------------------------------------------------

    [UnmanagedCallersOnly]
    static IntPtr Nop() => (nint)fakeObject;

    [UnmanagedCallersOnly]
    static byte* NullNop() => null;

    [UnmanagedCallersOnly]
    static void LogStub(int level, byte* text)
    {
        string line = Marshal.PtrToStringUTF8((nint)text) ?? "";
        // Layouts.Verify() compara Marshal.SizeOf com ClassValueSize; sem o
        // IL2CPP o segundo e sempre falso. Nao e um erro do mod nem do loader.
        if (line.Contains("struct layout differs from the game", StringComparison.Ordinal))
        {
            LayoutNoise++;
            return;
        }
        string formatted = (level >= 2 ? "[mod] E " : level == 1 ? "[mod] W " : "[mod] D ") + line;
        NativeLog.Add(formatted);
        Console.WriteLine(formatted);
    }

    [UnmanagedCallersOnly]
    static int FieldOffsetStub(IntPtr cls, byte* field)
    {
        string name = Marshal.PtrToStringUTF8((nint)field) ?? "?";
        if (!Offsets.TryGetValue(name, out int off))
        {
            off = nextOffset;
            nextOffset += 8;
            Offsets[name] = off;
        }
        return off;
    }

    // Convencao em GameInterop.ResolveMethod: `bad >= 0` lanca. -1 = tudo OK.
    [UnmanagedCallersOnly]
    static int CheckLayoutStub(IntPtr cls, int* expected, int count) => -1;

    [UnmanagedCallersOnly]
    static int ClassSizeStub(IntPtr cls) => -1;

    [UnmanagedCallersOnly]
    static IntPtr MethodPointerStub(IntPtr info) => (nint)(delegate* unmanaged<IntPtr>)&Nop;

    [UnmanagedCallersOnly]
    static IntPtr FindMethodStub(IntPtr cls, byte* signature) => (nint)fakeObject;

    [UnmanagedCallersOnly]
    static IntPtr FindMethodStub3(IntPtr cls, byte* name, int paramCount) => (nint)fakeObject;

    [UnmanagedCallersOnly]
    static IntPtr StaticFieldAddressStub(IntPtr field) => (nint)staticSlot;

    [UnmanagedCallersOnly]
    static void StaticFieldGetStub(IntPtr field, void* value) => *(nint*)value = (nint)fakeObject;

    [System.Runtime.InteropServices.DllImport("libc.so", SetLastError = true)]
    static extern IntPtr mmap(IntPtr addr, nuint length, int prot, int flags, int fd, nint offset);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint allocationType, uint protect);

    /// <summary>
    /// Uma pagina executavel com um unico `ret`.
    ///
    /// O InstallHook de verdade devolve um slot que aponta para o trampoline do
    /// hook. Sem trampoline, o shim guardaria o endereco de um DADO e o
    /// primeiro Call pularia para dentro da memoria de faz de conta — death.
    /// Com um `ret`, o hook "existe" e simply nao faz nada.
    ///
    /// Windows (a maquina de desenvolvimento) usa VirtualAlloc; Linux/Android
    /// usaria mmap. O `ret` depende da arquitetura: C3 em x64, D65F03C0 em arm64.
    /// </summary>
    static byte* MakeTrampoline()
    {
        const uint memCommitReserve = 0x1000 | 0x2000;
        const uint pageExecuteReadWrite = 0x40;
        IntPtr page;
        try
        {
            page = VirtualAlloc(IntPtr.Zero, 4096, memCommitReserve, pageExecuteReadWrite);
        }
        catch (DllNotFoundException)
        {
            page = IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            page = IntPtr.Zero;
        }
        if (page == IntPtr.Zero)
        {
            try
            {
                const int protReadWriteExec = 1 | 2 | 4;
                const int mapPrivateAnonymous = 0x02 | 0x20;
                page = mmap(IntPtr.Zero, 4096, protReadWriteExec, mapPrivateAnonymous, -1, 0);
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
        }
        if (page == IntPtr.Zero || page == new IntPtr(-1)) return null;

        bool arm64 = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            == System.Runtime.InteropServices.Architecture.Arm64;
        if (arm64)
        {
            // arm64: RET
            Marshal.WriteInt32(page, unchecked((int)0xD65F03C0u));
        }
        else
        {
            // x64/x86: RET
            Marshal.WriteByte(page, 0xC3);
        }
        return (byte*)page;
    }

    /// <summary>
    /// O slot do hook: o shim le o ponteiro de la e chama. Aqui aponta para o
    /// `ret` acima.
    /// </summary>
    [UnmanagedCallersOnly]
    static IntPtr* InstallHookStub(IntPtr method, IntPtr replacement)
    {
        IntPtr slot = (IntPtr)NativeMemory.Alloc(8);
        *(IntPtr*)slot = (IntPtr)(void*)trampoline;
        hookSlots.Add(slot);
        return (IntPtr*)slot;
    }

    [UnmanagedCallersOnly]
    static int GameThreadStub() => 1;

    [UnmanagedCallersOnly]
    static int ManifestStub(byte* uid, int field, byte* output, int size)
    {
        string value = field switch
        {
            0 => manifestId,
            1 => displayName,
            2 => version,
            3 => string.Join('\n', dependencies),
            _ => "",
        };
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (output != null && size > 0)
        {
            int n = Math.Min(bytes.Length, size - 1);
            Marshal.Copy(bytes, 0, (nint)output, n);
            output[n] = 0;
        }
        return bytes.Length;
    }

    [UnmanagedCallersOnly]
    static int RegisterItemStub(byte* mod, byte* name, byte* texture, byte* display, byte* tooltip)
    {
        int type = itemIdCount + nextItemType++;
        RegisteredItems[type] = Marshal.PtrToStringUTF8((nint)name) ?? "?";
        return type;
    }
}
