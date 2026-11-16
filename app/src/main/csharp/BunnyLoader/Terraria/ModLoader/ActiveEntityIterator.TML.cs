using BunnyLoader.Interop;

namespace Terraria.ModLoader;

/// <summary>
/// v101: marca as entidades que o <see cref="ActiveEntityIterator{T}"/> pode pular.
/// No proxy o campo e <c>ref bool active</c> (Player/NPC/Projectile) ou um
/// bool simples (WorldItem); os proxies gerados NAO herdam de <c>Terraria.Entity</c>,
/// entao a constraint do tML (<c>where T : Entity</c>) nao pode ser usada aqui.
/// </summary>
public interface IActiveEntity
{
    bool IsActive { get; }
}

/// <summary>
/// v101: equivalente de <c>Terraria.ModLoader.ActiveEntityIterator</c> do tML 1.4.4.
/// O mod compila <c>Terraria.Main::get_ActivePlayers()</c> com este tipo de retorno e
/// em seguida faz <c>ldloca</c> + <c>call ActiveEntityIterator`1&lt;Player&gt;::GetEnumerator()</c>
/// + <c>call Enumerator&lt;Player&gt;::MoveNext()/get_Current()</c> (ver
/// FargowiltasSouls.Core.Globals.FargoSoulsGlobalNPC.OnSpawn IL_0016..IL_0050).
/// No tML e <c>readonly ref struct</c>; aqui e struct normal (o IL nao diferencia).
/// </summary>
public readonly struct ActiveEntityIterator<T> where T : class, IGameObject
{
    readonly GameRefArray<T> array;
    readonly int count;

    public ActiveEntityIterator(GameRefArray<T> array, int count)
    {
        this.array = array;
        int length = array.IsNull ? 0 : array.Length;
        if (count < 0) count = 0;
        if (count > length) count = length;
        this.count = count;
    }

    public Enumerator GetEnumerator() => new(array, count);

    public struct Enumerator
    {
        readonly GameRefArray<T> array;
        readonly int count;
        int index;
        T current;

        internal Enumerator(GameRefArray<T> array, int count)
        {
            this.array = array;
            this.count = count;
            index = -1;
            current = null;
        }

        public readonly T Current => current;

        public bool MoveNext()
        {
            while (++index < count)
            {
                T candidate = array[index];
                if (candidate is IActiveEntity entity && entity.IsActive)
                {
                    current = candidate;
                    return true;
                }
            }

            current = null;
            return false;
        }
    }
}
