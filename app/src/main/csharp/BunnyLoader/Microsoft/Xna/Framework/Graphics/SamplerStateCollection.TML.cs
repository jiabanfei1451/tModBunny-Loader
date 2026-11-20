using System;
using BunnyLoader.Interop;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// FNA: <c>SamplerStateCollection.get_Item(int)</c>. O ProxyGen gera este tipo,
/// mas para indexadores ele só emite os ganchos (<c>event hook_get_Item get_Item</c>),
/// nunca o acessador de instância que o IL do mod chama — o FargoChinese faz
/// <c>callvirt SamplerStateCollection::get_Item(int32)</c> em
/// <c>UIWorldListItem_DrawSelf_Shader</c> para salvar/restaurar o sampler.
///
/// Resolve o nativo por nome+argc (o dump tem SamplerStateCollection,
/// TypeDefIndex 1055, com <c>get_Item</c>), igual a <c>TextureCollection</c>;
/// qualquer falha degrada para uma instância vazia em vez de estourar dentro de
/// um desenho de UI.
/// </summary>
public sealed partial class SamplerStateCollection
{
    static readonly SamplerState Empty = new(0);

    public unsafe SamplerState this[int index]
    {
        get
        {
            if (Pointer == 0 || GraphicsObjectCalls.IsBroken("SamplerStateCollection.get_Item"))
                return Empty;
            try
            {
                (nint info, nint fn) = GraphicsObjectCalls.Resolve(*(nint*)Pointer, "get_Item", 1);
                var call = GameCall.Returning(RetKind.Int);
                call.Arg(Pointer);
                call.Arg((long)index);
                call.Arg(info);
                nint value = (nint)call.Invoke(fn);
                return value == 0 ? Empty : new SamplerState(value);
            }
            catch (Exception e)
            {
                GraphicsObjectCalls.Break("SamplerStateCollection.get_Item", e);
                return Empty;
            }
        }
        set
        {
            if (Pointer == 0 || GraphicsObjectCalls.IsBroken("SamplerStateCollection.set_Item")) return;
            try
            {
                (nint info, nint fn) = GraphicsObjectCalls.Resolve(*(nint*)Pointer, "set_Item", 2);
                var call = GameCall.Returning(RetKind.Int);
                call.Arg(Pointer);
                call.Arg((long)index);
                call.Arg(value is null ? 0 : value.Pointer);
                call.Arg(info);
                call.Invoke(fn);
            }
            catch (Exception e)
            {
                GraphicsObjectCalls.Break("SamplerStateCollection.set_Item", e);
            }
        }
    }
}
