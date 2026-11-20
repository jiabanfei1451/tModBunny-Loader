using System;
using Microsoft.Xna.Framework;

namespace Terraria.Graphics.Effects;

/// <summary>
/// Um efeito registrado por mod (CustomSky derivado). O EffectManager chama
/// estes dois membros quando a chave aponta para uma instancia so-managed;
/// para instancias nativas nada muda (quem chama Activate/Deactivate e o
/// proprio jogo).
/// </summary>
public interface IManagedEffect
{
    void ManagedActivate(Vector2 position, object[] args);
    void ManagedDeactivate(object[] args);
}

public unsafe partial class CustomSky : IManagedEffect
{
    // O tModLoader declara `Activate(Vector2, params object[] args)` e
    // `Deactivate(params object[] args)`. O forwarder gerado usa
    // GameArray<GameObject> (a forma il2cpp do array), entao o override do mod
    // (AbomSky::Activate(Vector2, System.Object[])) precisa destes overloads
    // com virtual no base.
    public virtual void Activate(Vector2 position, params object[] args) { }

    public virtual void Deactivate(params object[] args) { }

    void IManagedEffect.ManagedActivate(Vector2 position, object[] args)
        => Activate(position, args ?? Array.Empty<object>());

    void IManagedEffect.ManagedDeactivate(object[] args)
        => Deactivate(args ?? Array.Empty<object>());
}

public sealed partial class Filter : IManagedEffect
{
    public void Activate(Vector2 position, params object[] args) { }

    public void Deactivate(params object[] args) { }

    void IManagedEffect.ManagedActivate(Vector2 position, object[] args)
        => Activate(position, args ?? Array.Empty<object>());

    void IManagedEffect.ManagedDeactivate(object[] args)
        => Deactivate(args ?? Array.Empty<object>());
}

public sealed partial class Overlay : IManagedEffect
{
    public void Activate(Vector2 position, params object[] args) { }

    public void Deactivate(params object[] args) { }

    void IManagedEffect.ManagedActivate(Vector2 position, object[] args)
        => Activate(position, args ?? Array.Empty<object>());

    void IManagedEffect.ManagedDeactivate(object[] args)
        => Deactivate(args ?? Array.Empty<object>());
}
