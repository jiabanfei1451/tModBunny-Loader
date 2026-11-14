package dev.bunnyloader.game

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.net.Uri

/**
 * Pode jogar neste aparelho? O único lugar que decide: o JOGAR do launcher e o
 * GameActivity perguntam aqui, e qualquer [Status] diferente de [Status.OK]
 * bloqueia o jogo.
 *
 * Hoje só exige o Terraria instalado (o pacote da Play Store). Conferir se a
 * cópia é original entra em [check], devolvendo um status novo com as chaves
 * de tradução dele; quem chama já mostra título e texto de qualquer status.
 */
object Eligibility {
    /** [titleKey] e [messageKey]: chaves de assets/localization/<cultura>.json. */
    enum class Status(val titleKey: String, val messageKey: String) {
        OK("", ""),
        NOT_INSTALLED("TerrariaRequiredTitle", "TerrariaRequiredDescription"),
    }

    /**
     * O runtime do jogo vem DENTRO deste APK (BundledRuntime): ter o app da
     * loja instalado nao e mais obrigatorio. Ainda aceitamos qualquer pacote
     * Terraria conhecido, para nao barrar quem tem o original no aparelho.
     * Sem isso, um pacote com nome diferente fazia o JOGAR fechar na hora
     * (parecia um crash).
     */
    fun check(ctx: Context): Status =
        if (BundledRuntime.isPresent(ctx) || isInstalled(ctx)) Status.OK else Status.NOT_INSTALLED

    /** Pacote do binario integrado + o pacote da Play; qualquer um serve. */
    private val KNOWN_PACKAGES = listOf(
        GameInstall.PACKAGE,
        "com.and.games505.TerrariaPaid",
    )

    private fun isInstalled(ctx: Context): Boolean = KNOWN_PACKAGES.any { pkg ->
        runCatching { ctx.packageManager.getPackageInfo(pkg, 0) }.isSuccess
    }

    /** A página do Terraria na Play Store; sem o app da loja, no navegador. */
    fun openStore(ctx: Context) {
        val market = Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=${GameInstall.PACKAGE}"))
        val web = Intent(Intent.ACTION_VIEW, Uri.parse("https://play.google.com/store/apps/details?id=${GameInstall.PACKAGE}"))
        for (intent in listOf(market, web)) {
            try {
                ctx.startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                return
            } catch (_: ActivityNotFoundException) {
            }
        }
    }
}
