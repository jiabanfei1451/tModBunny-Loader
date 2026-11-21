namespace Terraria;

public unsafe partial class Player
{
    /// <summary>
    /// PC 1.4.5 mudou <c>Player.statDefense</c> de <c>int</c> para esta struct
    /// (soma com <c>op_Addition</c>, comparação, formatação). O dump do Android
    /// continua com <c>int</c>, então o mod compilado no PC tinha
    /// <c>initobj Player/DefenseStat</c> + <c>op_Addition(DefenseStat, int)</c>
    /// e o método inteiro virava InvalidProgram no primeiro buff que mexe em
    /// defesa (<c>Fargowiltas.Content.Buffs.Omnistation.Update</c>, IL_0169) —
    /// era por isso que o buff não fazia efeito nenhum.
    ///
    /// O IlPatcher converte nas duas direções quando o campo do dump e o campo
    /// do mod discordam de tipo (ver InsertValueConv em IlPatcher.cs).
    /// </summary>
    public readonly partial struct DefenseStat : System.IEquatable<DefenseStat>
    {
        public readonly int Value;

        public DefenseStat(int value) => Value = value;

        public static implicit operator DefenseStat(int value) => new(value);

        public static implicit operator int(DefenseStat stat) => stat.Value;

        public static DefenseStat operator +(DefenseStat left, DefenseStat right) => new(left.Value + right.Value);

        public static DefenseStat operator +(DefenseStat left, int right) => new(left.Value + right);

        public static DefenseStat operator +(int left, DefenseStat right) => new(left + right.Value);

        public static DefenseStat operator -(DefenseStat left, DefenseStat right) => new(left.Value - right.Value);

        public static DefenseStat operator -(DefenseStat left, int right) => new(left.Value - right);

        public static DefenseStat operator *(DefenseStat left, int right) => new(left.Value * right);

        public static DefenseStat operator *(DefenseStat left, float right) => new((int)(left.Value * right));

        public static bool operator <(DefenseStat left, DefenseStat right) => left.Value < right.Value;

        public static bool operator >(DefenseStat left, DefenseStat right) => left.Value > right.Value;

        public static bool operator <=(DefenseStat left, DefenseStat right) => left.Value <= right.Value;

        public static bool operator >=(DefenseStat left, DefenseStat right) => left.Value >= right.Value;

        public static bool operator ==(DefenseStat left, DefenseStat right) => left.Value == right.Value;

        public static bool operator !=(DefenseStat left, DefenseStat right) => left.Value != right.Value;

        public bool Equals(DefenseStat other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is DefenseStat other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString();
    }
}
