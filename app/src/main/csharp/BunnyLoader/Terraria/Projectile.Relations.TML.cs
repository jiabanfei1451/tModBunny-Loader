using Terraria.ID;

namespace Terraria;

public sealed partial class Projectile
{
    public bool IsMinionOrSentryRelated => minion || sentry || ProjectileID.Sets.MinionShot[type] || ProjectileID.Sets.SentryShot[type];
}
