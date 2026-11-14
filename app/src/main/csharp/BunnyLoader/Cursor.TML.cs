using BunnyLoader.Interop;
using Microsoft.Xna.Framework;

public unsafe sealed partial class Cursor
{
    static int positionOffset = -1;

    public ref Vector2 Position => ref *(Vector2*)(Pointer + GameInterop.Offset(ref positionOffset, Class, "Position"));
}
