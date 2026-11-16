namespace Terraria;

public sealed partial class Player
{
    /// <summary>Estrutura aninhada do vanilla usada só como tipo de campo/local.</summary>
    public struct DefenseStat
    {
        public int value;

        public float mult;

        public DefenseStat(int value, float mult = 1f)
        {
            this.value = value;
            this.mult = mult;
        }

        public override string ToString() => value + " x" + mult;
    }
}
