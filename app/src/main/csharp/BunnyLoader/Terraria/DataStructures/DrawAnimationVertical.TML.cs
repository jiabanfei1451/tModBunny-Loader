using System.Collections.Generic;
using BunnyLoader.Interop;

namespace Terraria.DataStructures;

public sealed partial class DrawAnimationVertical
{
    static readonly List<GameHandle> Kept = new();

    public DrawAnimationVertical(int ticksperframe, int frameCount, bool pingPong = false)
    {
        DrawAnimationVertical animation = New(ticksperframe, frameCount, pingPong);
        lock (Kept) Kept.Add(new GameHandle(animation.Pointer));
        Pointer = animation.Pointer;
    }
}
