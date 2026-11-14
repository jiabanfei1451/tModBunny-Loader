package dev.bunnyloader.game

import android.app.ActivityManager
import android.app.ApplicationExitInfo
import android.content.Context
import android.os.Build
import android.util.Log
import dev.bunnyloader.TAG
import java.io.File

/**
 * Trilha do boot hospedado, gravada em arquivo.
 *
 * A GameActivity roda no processo `:game`, e quando ela falha só sobra um Toast
 * que some. Sem adb no aparelho do usuário não dá para ler o logcat, então cada
 * passo vai também para um arquivo no filesDir (compartilhado entre os dois
 * processos do app) e o launcher mostra o conteúdo na tela.
 */
object BootLog {
    private const val FILE = "hosted-boot.log"
    /** Quantas linhas dos mods o relatório mostra (as últimas). */
    private const val MOD_LINES = 200

    private fun file(ctx: Context) = File(ctx.filesDir, FILE)

    /** Começa uma trilha nova (chamar no início do boot). */
    fun reset(ctx: Context) {
        runCatching { file(ctx).writeText("") }
    }

    fun add(ctx: Context, line: String) {
        Log.i(TAG, "boot: $line")
        runCatching { file(ctx).appendText(line + "\n") }
    }

    /** Registra uma falha com a causa raiz desembrulhada (reflection esconde). */
    fun fail(ctx: Context, what: String, t: Throwable?) {
        val root = t?.let { generateSequence(it) { c -> c.cause }.last() }
        val detail = root?.let { "${it.javaClass.simpleName}: ${it.message}" } ?: "sem detalhes"
        add(ctx, "FALHA: $what")
        add(ctx, "  -> $detail")
        root?.stackTrace?.take(6)?.forEach { add(ctx, "     at $it") }
    }

    /**
     * Captura exceções de QUALQUER thread do processo.
     *
     * Os try/catch da GameActivity só cobrem o onCreate; a Unity estoura depois,
     * nas threads dela, e aí só sobra "CRASH (Java)" no exit reason — sem stack.
     */
    fun installCrashHandler(ctx: Context) {
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, t ->
            runCatching {
                add(ctx, "CRASH na thread '${thread.name}'")
                var e: Throwable? = t
                var depth = 0
                while (e != null && depth++ < 3) {
                    add(ctx, "  ${e.javaClass.name}: ${e.message}")
                    e.stackTrace.take(24).forEach { add(ctx, "    at $it") }
                    e = e.cause?.also { add(ctx, "  causado por:") }
                }
            }
            previous?.uncaughtException(thread, t)
        }
    }

    private fun logcatFile(ctx: Context) = File(ctx.filesDir, "hosted-logcat.log")

    /**
     * Captura o logcat DO PRÓPRIO processo para arquivo.
     *
     * A Unity imprime o motivo do erro no logcat imediatamente antes de abortar,
     * e um app pode ler os próprios logs sem permissão especial. Isso é bem mais
     * confiável que o tombstone, que desde o Android 12 vem em protobuf e chegou
     * truncado aqui.
     */
    fun captureLogcat(ctx: Context) {
        val out = logcatFile(ctx)
        runCatching { out.writeText("") }
        Thread {
            runCatching {
                val pid = android.os.Process.myPid()
                val p = Runtime.getRuntime().exec(
                    arrayOf("logcat", "-v", "brief", "--pid=$pid"),
                )
                p.inputStream.bufferedReader().forEachLine { line ->
                    // TAG é o primeiro da lista de propósito: sem ele o filtro
                    // descartava as linhas do nosso próprio núcleo (giveItem,
                    // cheats, mods), que são justamente as que o usuário
                    // precisa conseguir mandar quando algo não funciona.
                    val keep = line.contains(TAG) ||
                        line.contains("Unity") || line.contains("IL2CPP") ||
                        line.contains("DEBUG") || line.contains("libc") ||
                        line.contains("Fatal") || line.contains("FATAL") ||
                        line.contains("Error") || line.contains("error") ||
                        line.contains("JNI") || line.contains("dlopen") ||
                        line.contains("art")
                    // Ruído do Unity: a mesma linha de layout serializado se
                    // repete centenas de vezes no boot e enchia a cota antes do
                    // tombstone (que é o que interessa) ser escrito.
                    val noise = line.contains("has a different serialization layout") ||
                        line.contains("Did you #ifdef UNITY_EDITOR")
                    // Limite de tamanho: interessa o fim, não o histórico todo.
                    // O recorte é feito na leitura (lastLogcat usa a cauda).
                    if (keep && !noise && out.length() < 1_000_000) out.appendText(line + "\n")
                }
            }
        }.apply { isDaemon = true }.start()
    }

    /**
     * O relatório do Diagnóstico (Configurações): o que um jogador que sofreu um
     * crash copia ou fotografa e manda para quem faz o Bunny Loader ou o mod.
     *
     * Na ordem de quem vai ler: o que é o aparelho e o que estava ligado, os
     * erros da última partida, e só depois o detalhe técnico do boot. Num print
     * de celular cabe o começo; por isso a causa vem antes do histórico.
     */
    fun read(ctx: Context): String =
        header(ctx) +
            "\n--- por que o jogo fechou (últimas vezes) ---\n" + exitReasons(ctx) +
            "\n--- última partida: erros e avisos ---\n" + sessionProblems(ctx) +
            "\n--- última partida: linhas dos mods ---\n" + sessionModLines(ctx) +
            "\n--- última partida: bloco do crash nativo ---\n" + crashBlock(ctx) +
            "\n--- última partida: linhas antes do crash ---\n" + sessionTail(ctx, 60) +
            "\n--- boot do jogo ---\n" +
            (runCatching { file(ctx).readText() }.getOrDefault("").ifBlank { "(sem registro ainda)" }) +
            "\n--- logcat do processo do jogo ---\n" + lastLogcat(ctx)

    /** O nome do app como o launcher o mostra (vem do rótulo do manifesto). */
    private fun appLabel(ctx: Context): String = runCatching {
        val pm = ctx.packageManager
        pm.getApplicationLabel(pm.getApplicationInfo(ctx.packageName, 0)).toString()
    }.getOrDefault("Bunny Loader")

    /** Versões, aparelho, data e os mods ligados: sem isso ninguém reproduz nada. */
    private fun header(ctx: Context): String = buildString {
        append("=== ").append(appLabel(ctx)).append(": relatório ===\n")
        append(appLabel(ctx)).append(" v").append(dev.bunnyloader.BuildConfig.VERSION_NAME)
        append(" · Terraria ").append(BundledRuntime.VERSION_NAME)
        append(" (").append(BundledRuntime.VERSION_CODE).append(")\n")
        append("Aparelho: ").append(Build.MANUFACTURER).append(' ').append(Build.MODEL)
        append(" · Android ").append(Build.VERSION.RELEASE).append(" (API ").append(Build.VERSION.SDK_INT).append(')')
        append(" · ").append(Build.SUPPORTED_ABIS.firstOrNull() ?: "?").append('\n')
        append("Gerado em: ").append(
            java.text.SimpleDateFormat("yyyy-MM-dd HH:mm", java.util.Locale.US).format(java.util.Date()),
        ).append('\n')
        val mods = runCatching {
            val repo = dev.bunnyloader.mods.ModRepository(ctx)
            repo.list().filter { repo.isEnabled(it.uid) }
                .joinToString(", ") { "${it.name} ${it.version} (${it.id})" }
        }.getOrDefault("(não deu para ler)")
        // As duas listas sao INDEPENDENTES: os .bl vivem em enabled.json do
        // ModRepository, os .tmod em TML/enabled.json (e mode.txt para o lado
        // nativo). Mostrar so a primeira fazia "Mods ligados: nenhum" parecer
        // que o jogo estava limpo, enquanto um .tmod continuava carregando.
        append("Mods ligados (.bl): ").append(mods.ifBlank { "nenhum" }).append('\n')
        val tml = runCatching {
            dev.bunnyloader.mods.TmlMods.enabled(ctx).sorted().joinToString(", ")
        }.getOrDefault("(não deu para ler)")
        append("TML ligados (.tmod): ").append(tml.ifBlank { "nenhum" }).append('\n')
    }

    /**
     * O log de sessão mais novo (`logs/bunny_<data>.txt`, o mesmo que o modder
     * abre): as linhas de erro e aviso, e o fim dele. Depois de um crash, o
     * mais novo é o da partida que caiu — o launcher não abre um novo.
     */
    private fun lastSessionLog(ctx: Context): File? {
        val base = ctx.getExternalFilesDir(null)?.parentFile ?: return null
        return File(base, "logs").listFiles { f -> f.name.startsWith("bunny_") && f.name.endsWith(".txt") }
            .orEmpty().maxByOrNull { it.name }
    }

    /**
     * O que os mods escreveram (bl.log, Log.Info do C#): é onde um teste ou uma
     * medição deixa o resultado para quem só tem o celular.
     */
    private fun sessionModLines(ctx: Context): String = runCatching {
        val last = lastSessionLog(ctx) ?: return@runCatching "(nenhum log de sessão)\n"
        val lines = last.readLines().filter { "[mod]" in it }.takeLast(MOD_LINES)
        if (lines.isEmpty()) "(nenhuma)\n" else lines.joinToString("\n", postfix = "\n")
    }.getOrElse { "(erro lendo: ${it.javaClass.simpleName})\n" }

    /**
     * As linhas do log de sessao IMEDIATAMENTE antes do crash.
     *
     * E a parte que responde "onde o jogo estava quando morreu". O bloco do
     * sinal e gravado no FIM do arquivo, entao `takeLast` pegava o comeco dele
     * (o historico mais velho) — o recorte tem de parar antes do bloco.
     */
    private fun sessionTail(ctx: Context, count: Int): String = runCatching {
        val last = lastSessionLog(ctx) ?: return@runCatching "(nenhum log de sessão)\n"
        val all = last.readLines()
        val crashAt = all.indexOfLast { it.contains("=== CRASH NATIVO") }
        val lines = (if (crashAt > 0) all.subList(0, crashAt) else all).takeLast(count)
        if (lines.isEmpty()) "(vazio)\n" else lines.joinToString("\n", postfix = "\n")
    }.getOrElse { "(erro lendo: ${it.javaClass.simpleName})\n" }

    /**
     * O cabecalho do bloco que o manipulador de sinal nativo gravou no fim do
     * log: o sinal, o PC (com nome de simbolo) e o endereco que faltou.
     *
     * A cauda de 40 linhas NAO alcanca essas tres: o bloco tem 40 linhas de
     * historico antes do rodape, entao o PC ficava logo acima do recorte. Sem
     * ele nao da para dizer se o SIGSEGV foi no jogo, na Unity ou no nucleo.
     */
    /**
     * O bloco do crash INTEIRO: registradores, `PC mapa`, `PC metodo`, a pilha
     * do C# (`JIT: ...`) e as ultimas linhas do nucleo, em ordem cronologica.
     *
     * Antes daqui saiam 4 linhas (so o cabecalho), e a cauda de 40 linhas do
     * log pegava o COMECO do bloco — justamente o historico mais velho. Foi por
     * isso que varias rodadas culparam um passo que nem era o ultimo.
     */
    private fun crashBlock(ctx: Context): String = runCatching {
        val last = lastSessionLog(ctx) ?: return@runCatching "(nenhum log de sessão)\n"
        val lines = last.readLines()
        val start = lines.indexOfLast { it.contains("=== CRASH NATIVO") }
        if (start < 0) return@runCatching "(sem bloco de crash nativo no log)\n"
        val rest = lines.subList(start, lines.size)
        val stop = rest.indexOfFirst { it.contains("=== fim do crash ===") }
        val block = if (stop < 0) rest else rest.subList(0, stop + 1)
        // 260 = cabecalho + registradores + mapa/metodo + pilha do C# + as 192
        // linhas do nucleo + rodape. Cortar em menos escondia justamente o fim,
        // que e onde o processo morreu.
        block.take(260).joinToString("\n", postfix = "\n")
    }.getOrElse { "(erro lendo: ${it.javaClass.simpleName})\n" }

    private fun sessionProblems(ctx: Context): String = runCatching {
        val last = lastSessionLog(ctx) ?: return@runCatching "(nenhum log de sessão)"
        val lines = last.readLines()
        // "12:03:04.567 E texto": a letra depois da hora é o nível.
        val problems = lines.filter { it.length > 13 && (it[13] == 'E' || it[13] == 'W') }.takeLast(30)
        buildString {
            append(last.name).append('\n')
            if (problems.isEmpty()) append("(nenhum erro ou aviso)\n")
            else problems.forEach { append(it).append('\n') }
            append(">>> fim do log:\n")
            lines.takeLast(15).forEach { append(it).append('\n') }
        }
    }.getOrElse { "(erro lendo: ${it.javaClass.simpleName})" }

    /**
     * A CAUSA primeiro, depois o resto.
     *
     * Quando a Unity aborta via JNI FatalError, a ART despeja um stack enorme
     * logo depois — e cortar pelo fim justamente esconde a mensagem, que vem
     * antes. Então destacamos as linhas de causa e só depois mostramos a cauda.
     */
    private fun lastLogcat(ctx: Context): String = runCatching {
        val lines = logcatFile(ctx).readLines()
        if (lines.isEmpty()) return@runCatching "(vazio)"
        val causes = lines.filter { l ->
            l.contains("FatalError called") || l.contains("JNI DETECTED") ||
                l.contains("Abort message") || l.contains("UnsatisfiedLink") ||
                l.contains("dlopen") || l.contains("Unable to") ||
                l.contains("Failed to") || l.contains("E/Unity") ||
                l.contains("E Unity") || l.contains("could not") ||
                l.startsWith("E/$TAG")  // erro do próprio núcleo
        }.distinct().take(15)
        buildString {
            if (causes.isNotEmpty()) {
                append(">>> CAUSA:\n")
                causes.forEach { append("  ").append(it.trim()).append("\n") }
                append(">>> resto:\n")
            }
            // Sem os quadros de stack repetidos, que não acrescentam nada.
            val clean = lines.filterNot { it.contains("native: #") || it.contains("runtime.cc:") }
            // Da linha do crash em diante vem o dump do manipulador de sinal,
            // que já sai inteiro acima. Aqui interessa o que o núcleo escreveu
            // ANTES dele: o logcat guarda também as linhas de DEBUG, em ordem.
            val crashAt = clean.indexOfLast { it.contains("=== CRASH NATIVO") }
            val before = if (crashAt > 0) clean.subList(0, crashAt) else clean
            before.takeLast(45).forEach { append(it).append("\n") }
        }
    }.getOrElse { "(sem captura)" }

    /**
     * Motivo das últimas mortes de processo do app. Crash NATIVO não passa por
     * try/catch nem aparece na trilha acima; esta API pública (Android 11+)
     * entrega o motivo e, para crash nativo, o tombstone.
     */
    private fun exitReasons(ctx: Context): String = runCatching {
        if (Build.VERSION.SDK_INT < 30) return@runCatching "(precisa Android 11+)"
        val am = ctx.getSystemService(ActivityManager::class.java)
        val list = am.getHistoricalProcessExitReasons(ctx.packageName, 0, 5)
        if (list.isEmpty()) return@runCatching "(nenhum registro)"
        list.joinToString("\n") { info ->
            buildString {
                append(info.processName).append(": ").append(reasonName(info.reason))
                append(" status=").append(info.status)
                // SIGKILL nao e abort: e o sistema (LMK / economia de bateria
                // da OEM) ou o usuario forcando a parada. Diferenciar evita
                // confundir "fechei o jogo" com "o jogo caiu".
                if (info.reason == ApplicationExitInfo.REASON_SIGNALED && info.status == 9)
                    append(" (SIGKILL: sistema/force-stop, nao crash)")
                info.description?.let { append("\n   ").append(it) }
                append(tombstone(info))
            }
        }
    }.getOrElse { "(erro lendo: ${it.javaClass.simpleName})" }

    /**
     * Para crash NATIVO o sistema guarda o tombstone (mensagem de abort +
     * backtrace) e o entrega por getTraceInputStream(). É a única forma de saber
     * por que o processo abortou sem ter adb no aparelho.
     */
    private fun tombstone(info: ApplicationExitInfo): String {
        if (info.reason != ApplicationExitInfo.REASON_CRASH_NATIVE) return ""
        return runCatching {
            val bytes = info.traceInputStream?.use { it.readBytes() }
                ?: return "\n   (sem tombstone)"
            // O trace chega em texto OU em binário, conforme o aparelho. Decodificar
            // tudo como texto vira lixo (foi o que aconteceu: o relatório veio com
            // uma parede de caracteres estranhos). Então extraímos só os trechos
            // imprimíveis, e de preferência os que carregam a mensagem do abort.
            val strings = printableStrings(bytes, 6)
            val interesting = strings.filter { s ->
                s.contains("signal", true) || s.contains("abort", true) ||
                    s.contains("Cause", true) || s.contains("backtrace", true) ||
                    s.startsWith("#") || s.contains("libbunny") || s.contains("libmono") ||
                    s.contains("assert", true) || s.contains("Fatal", true)
            }
            val picked = (if (interesting.isEmpty()) strings else interesting).take(24)
            if (picked.isEmpty()) "\n   (tombstone sem trechos legíveis)"
            else "\n" + picked.joinToString("\n") { "   " + it }
        }.getOrElse { "\n   (tombstone ilegível: ${it.javaClass.simpleName})" }
    }

    /** Trechos imprimíveis de um buffer (o `strings` do Unix, em Java). */
    private fun printableStrings(bytes: ByteArray, minLength: Int): List<String> {
        val out = ArrayList<String>()
        val current = StringBuilder()
        for (raw in bytes) {
            val c = raw.toInt() and 0xFF
            if (c in 0x20..0x7E) current.append(c.toChar())
            else {
                if (current.length >= minLength) out.add(current.toString())
                current.setLength(0)
            }
        }
        if (current.length >= minLength) out.add(current.toString())
        return out
    }

    private fun reasonName(r: Int): String = when (r) {
        ApplicationExitInfo.REASON_CRASH -> "CRASH (Java)"
        ApplicationExitInfo.REASON_CRASH_NATIVE -> "CRASH NATIVO"
        ApplicationExitInfo.REASON_ANR -> "ANR"
        ApplicationExitInfo.REASON_LOW_MEMORY -> "SEM MEMORIA"
        ApplicationExitInfo.REASON_SIGNALED -> "SINAL"
        ApplicationExitInfo.REASON_EXIT_SELF -> "saiu sozinho"
        ApplicationExitInfo.REASON_USER_REQUESTED -> "usuario"
        ApplicationExitInfo.REASON_OTHER -> "outro"
        else -> "codigo $r"
    }
}
