using System.Collections.Generic;

namespace Terraria.UI;

/// <summary>
/// Ponte de <c>InGameNotificationsTracker.AddNotification</c>.
///
/// O tML 1.4.4.9 tem este método em <c>InGameNotificationsTracker.TML.cs</c>:
/// <code>
/// public static void AddNotification(IInGameNotification notification)
///     => _notifications.Add(notification);
/// </code>
/// No jogo 1.4.5.8 (il2cpp) <c>_notifications</c> é um <c>List&lt;IInGameNotification&gt;</c>
/// nativo — o mod passa uma instância gerenciada dele mesmo (NoDLCNotification /
/// NoMusicNotification), que não é objeto il2cpp e portanto não pode entrar nessa
/// lista (o <c>GameList.Add</c> gravaria um ponteiro inválido e o próximo
/// <c>DrawInGame</c> derrubaria o processo).
///
/// Então a notificação não é exibida: guardamos na lista gerenciada abaixo (para
/// quem quiser inspecionar) e avisamos uma vez. O importante é o método existir —
/// sem ele o JIT falha em <c>FargoSoulsPlayer.OnEnterWorld</c> e o jogador perde
/// o mundo inteiro do mod por causa de um aviso cosmético.
/// </summary>
public unsafe sealed partial class InGameNotificationsTracker
{
    /// <summary>Notificações que o jogo não consegue desenhar (ver comentário do tipo).</summary>
    public static readonly List<IInGameNotification> ManagedNotifications = new();

    static bool warned;

    public static void AddNotification(IInGameNotification notification)
    {
        if (notification == null) return;

        ManagedNotifications.Add(notification);

        if (warned) return;

        warned = true;

        global::BunnyLoader.Log.Warn($"InGameNotificationsTracker.AddNotification: '{notification.GetType().FullName}' fica so na lista gerenciada "
            + "(a lista do jogo e il2cpp e nao aceita instancia gerenciada); a notificacao nao sera desenhada.");
    }
}
