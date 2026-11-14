package dev.bunnyloader.game

import android.content.Context
import android.os.Process
import java.io.File

/** Localiza e valida o Terraria instalado no aparelho. */
data class GameInstall(
    val packageName: String,
    val apkPath: String,
    val nativeLibDir: String,
    val versionCode: Long,
    val abi: String,
    /** Splits do install (Play entrega o jogo dividido: base + config.<abi>). */
    val splitApks: List<String> = emptyList(),
) {
    /** base + splits, na ordem de busca. As .so vivem no split de ABI. */
    fun allApks(): List<String> = listOf(apkPath) + splitApks

    /** Caminho da .so; cobre APKs com extractNativeLibs=false. */
    fun libPath(name: String): String {
        val extracted = File(nativeLibDir, name)
        if (extracted.exists()) return extracted.absolutePath
        return "$apkPath!/lib/$abi/$name"
    }

    companion object {
        // Pacote do binario integrado nesta build (Terraria 1.4.5.8.5,
        // versionCode 301709). Nao e o pacote da Play (...TerrariaPaid).
        const val PACKAGE = "com.and.games505.Terraria"

        fun locate(context: Context): GameInstall? {
            val pm = context.packageManager
            val info = runCatching { pm.getPackageInfo(PACKAGE, 0) }.getOrNull() ?: return null
            val app = info.applicationInfo ?: return null
            val abi = if (Process.is64Bit()) "arm64-v8a" else "armeabi-v7a"
            @Suppress("DEPRECATION")
            val versionCode = info.longVersionCode
            return GameInstall(
                PACKAGE, app.sourceDir, app.nativeLibraryDir, versionCode, abi,
                app.splitSourceDirs?.toList() ?: emptyList(),
            )
        }
    }
}
