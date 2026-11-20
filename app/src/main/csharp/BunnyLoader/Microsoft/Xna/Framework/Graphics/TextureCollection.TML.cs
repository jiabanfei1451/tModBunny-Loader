using System;
using System.Collections.Generic;
using BunnyLoader.Interop;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// FNA: colecao de texturas ligadas ao GraphicsDevice (slots de sampler). O IL
/// dos mods so usa o indexador (get_Item/set_Item); o dump de referencia nao tem
/// o tipo, entao ele e escrito a mao aqui. A instancia nativa vem de
/// GraphicsDevice.Textures; quando o jogo nao expoe esse metodo, cai num
/// dicionario gerenciado para o mod nao quebrar no meio do desenho.
/// </summary>
public unsafe class TextureCollection : IGameObject
{
    /// <summary>Instancia de recurso usada quando nao existe colecao nativa.</summary>
    internal static readonly TextureCollection Managed = new(0);

    static readonly Texture Empty = new(0);

    readonly Dictionary<int, Texture> local = new();
    readonly bool useNative;

    public readonly nint Pointer;

    public TextureCollection(nint pointer)
    {
        Pointer = pointer;
        useNative = pointer != 0;
    }

    nint IGameObject.Pointer => Pointer;

    public bool IsNull => Pointer == 0;

    public Texture this[int index]
    {
        get
        {
            if (useNative)
            {
                try
                {
                    nint value = GetNative(index);
                    return value == 0 ? Empty : new Texture(value);
                }
                catch (Exception e) { GraphicsObjectCalls.Break("TextureCollection.get_Item", e); }
            }
            return local.TryGetValue(index, out Texture? stored) ? stored : Empty;
        }
        set
        {
            if (useNative)
            {
                try
                {
                    SetNative(index, value is null ? 0 : value.Pointer);
                    return;
                }
                catch (Exception e) { GraphicsObjectCalls.Break("TextureCollection.set_Item", e); }
            }
            if (value is null) local.Remove(index);
            else local[index] = value;
        }
    }

    nint GetNative(int index)
    {
        nint self = Pointer;
        var (info, fn) = GraphicsObjectCalls.Resolve(*(nint*)self, "get_Item", 1);
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg((long)index);
        call.Arg(info);
        return (nint)call.Invoke(fn);
    }

    void SetNative(int index, nint texture)
    {
        nint self = Pointer;
        var (info, fn) = GraphicsObjectCalls.Resolve(*(nint*)self, "set_Item", 2);
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg((long)index);
        call.Arg(texture);
        call.Arg(info);
        call.Invoke(fn);
    }
}
