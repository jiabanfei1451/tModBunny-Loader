using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BunnyLoader.Interop;

namespace Terraria.GameContent.Creative;

/// <summary>
/// O tML expõe <c>SacrificeCountNeededByItemId</c> como um
/// <c>Dictionary&lt;int,int&gt;</c> GERENCIADO; aqui o proxy devolveria
/// <c>GameObject</c> e o mod (que faz <c>ContainsKey</c>) quebrava com
/// MissingMethodException. Esta propriedade chama o getter nativo e copia o
/// conteúdo para um dicionário gerenciado.
/// </summary>
public sealed partial class CreativeItemSacrificesCatalog
{
    static MethodRef __mSacrifice;

    /// <summary>Ponteiro nativo do dicionário (uso interno do loader).</summary>
    internal unsafe nint SacrificeTablePointer()
    {
        if (Pointer == 0) return 0;
        ref readonly var method = ref GameInterop.Method(ref __mSacrifice, Class,
            "Dictionary<int, int> get_SacrificeCountNeededByItemId()", "\0");
        CallFrame frame;
        Unsafe.SkipInit(out frame);
        frame.X[0] = (long)Pointer;
        frame.X[1] = (long)method.Info;
        frame.Ret = RetKind.Int;
        GameInterop.CallFast(method.Fn, &frame);
        return (nint)((long)frame.Out[0]);
    }

    public Dictionary<int, int> SacrificeCountNeededByItemId
    {
        get
        {
            var result = new Dictionary<int, int>();
            var native = new GameDictionary(SacrificeTablePointer());
            if (native.IsNull) return result;
            foreach (KeyValuePair<int, int> entry in native.IntEntries()) result[entry.Key] = entry.Value;
            return result;
        }
    }
}
