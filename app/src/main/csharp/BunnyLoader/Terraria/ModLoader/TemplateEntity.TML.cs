using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BunnyLoader;
using BunnyLoader.Interop;

namespace Terraria.ModLoader;

/// <summary>
/// No tModLoader do PC, <c>CreateTemplateEntity()</c> devolve <c>new Item()</c>
/// (ou <c>new NPC()</c>...) — um objeto gerenciável de verdade, onde o
/// <c>SetStaticDefaults()</c> do mod escreve os valores padrão.
///
/// Aqui <c>Item</c>/<c>NPC</c>/<c>Player</c> são PROXIES: toda leitura e
/// escrita vai para o ponteiro nativo. Com <c>default</c> o proxy virava
/// <b>null</b> (antes era struct, e <c>default</c> era um wrapper válido) e
/// qualquer <c>Item.width = 24</c> dentro do SetStaticDefaults estourava
/// <c>NullReferenceException</c> — exatamente o que apareceu no aparelho.
///
/// O template precisa então de um objeto nativo alocado. Ele nunca entra no
/// jogo: serve só de rascunho para o mod escrever os padrões.
/// </summary>
internal static class TemplateEntity
{
    /// <summary>
    /// Os templates são objetos <b>il2cpp</b> alocados por <c>object_new</c>. O
    /// proxy gerenciado só guarda o <c>IntPtr</c>: para o GC do jogo não existe
    /// NENHUMA referência a eles.
    ///
    /// O <c>ProjectileLoader/NPCLoader/ItemLoader.Installed</c> só "pinava"
    /// quando <c>Entity.IsNull</c> — mas <see cref="ModTypeOfEntity{TEntity,TModType}.InitTemplateInstance"/>
    /// já criou o template no <c>Load()</c>, então esse ramo NUNCA rodava.
    /// Resultado no aparelho: o GC reutilizava a memória e o
    /// <c>SetStaticDefaults</c> lia lixo no lugar do <c>Projectile.type</c>
    /// (<c>IndexOutOfRangeException: index -1275068292</c>), além de escritas
    /// em memória liberada — corrupção nativa que fecha o jogo.
    ///
    /// O pin tem de ser feito AQUI, no único lugar onde o objeto nasce.
    /// </summary>
    static readonly List<GameHandle> Pins = new();

    internal static T Create<T>(GameClass cls) where T : class, IGameObject
    {
        try
        {
            IntPtr pointer = GameInterop.NewObject(cls);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("object_new devolveu null");
            lock (Pins) Pins.Add(new GameHandle(pointer));
            // `object_new` SÓ ALOCA — o construtor tem de ser chamado à parte
            // (o próprio comentário do native diz isso). Sem ele os campos do
            // template ficam com o LIXO que estava naquela memória, e aí:
            //   * `Projectile.type` lia 16843009 (0x01010101) ->
            //     `Main.projFrames[16843009] = 8` -> IndexOutOfRangeException
            //     em CoolCrab.SetStaticDefaults;
            //   * `ModNPC.Type` (getter usa `entity.type`) devolvia o mesmo
            //     lixo para todo mundo -> `IdDictionary.Add(name, 1)` colidia
            //     ("An item with the same key has already been added. Key: 1")
            //     e os NPCs de mod ficavam sem tipo/textura/interação;
            //   * o template de Item nascia sem `type`/largura -> item em
            //     branco no inventário e inutilizável.
            RunCtor(cls, pointer);
            return (T)GameObjectFactory.Create(typeof(T), pointer);
        }
        catch (Exception e)
        {
            // Sem nativo (ou classe não registrada): melhor um proxy nulo do
            // que derrubar o carregamento inteiro do mod.
            Log.Warn($"template de {typeof(T).Name}: sem objeto nativo ({e.GetType().Name}: {e.Message})");
            return null!;
        }
    }

    /// <summary>
    /// Chama o <c>.ctor()</c> no objeto recém-alocado, que é o que zera os
    /// campos (equivalente ao <c>new T()</c> do PC).
    /// </summary>
    static unsafe void RunCtor(GameClass cls, IntPtr self)
    {
        try
        {
            // Sem cache: o template é criado uma vez por tipo de mod (algumas
            // centenas), não é caminho quente. Um cache por `cls` evitaria a
            // resolução repetida, mas o `Method` só guarda um slot global —
            // reusar entre classes devolveria o .ctor da PRIMEIRA.
            MethodRef ctor = default;
            ref readonly MethodRef resolved = ref GameInterop.Method(ref ctor, cls, "void .ctor()", "\0");
            CallFrame frame;
            Unsafe.SkipInit(out frame);
            // X[0] = this. X[1] = MethodInfo* — inofensivo para método não
            // genérico e OBRIGATÓRIO quando o código é compartilhado/genérico
            // (é assim que ConstantSearch chama o `.ctor()` do Dictionary).
            frame.X[0] = (long)self;
            frame.X[1] = (long)resolved.Info;
            // Ret fica no default (Int): `.ctor` é void, o valor não é lido.
            GameInterop.Call(resolved.Fn, &frame);
        }
        catch (Exception e)
        {
            // Sem `.ctor()` ou sem corpo executável: segue com o objeto
            // alocado. Continua servindo de molde — só não nasce zerado.
            Log.Warn($"template de {cls.FullName}: .ctor nao executou ({e.GetType().Name}: {e.Message})");
        }
    }
}
