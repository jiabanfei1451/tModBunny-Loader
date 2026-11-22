using System;
using System.Runtime.InteropServices;

namespace BunnyLoader.Interop;

// O codigo do tML 1.4.5 descreve os gates de hook com este struct. O nosso
// nativo nao tem InstallGateHook (a libbunny.so deste fork so expoe InstallHook),
// entao o struct so precisa existir para compilar: quem chama acaba no caminho
// classico de instalacao (ver Hook.InstallGated/HookGates.Install).
[StructLayout(LayoutKind.Sequential)]
internal struct GateSpec
{
    public int Arg;
    public int PointerOffset;
    public int FieldOffset;
    public int FieldSize;
    public IntPtr Bitmap;
    public int BitmapBits;
    public int Flags;
    public IntPtr Address;
}
