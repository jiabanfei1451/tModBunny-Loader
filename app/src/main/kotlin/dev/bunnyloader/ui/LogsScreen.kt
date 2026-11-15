package dev.bunnyloader.ui

import android.content.Context
import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.ClipboardManager
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import dev.bunnyloader.R
import dev.bunnyloader.game.BootLog
import dev.bunnyloader.game.LogChannel
import dev.bunnyloader.game.SessionTail
import dev.bunnyloader.mods.formatSize
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * A tela "Log": relatório de diagnóstico, log de sessão e logcat do processo,
 * num só lugar, com um seletor de origem no topo.
 *
 * Antes eram duas telas com dois botões ("ver relatório" e "ver log do C#") e
 * nenhum jeito de ver só as linhas do núcleo ou só as do .NET. Quem abre isso
 * está caçando UMA linha de erro: mostrar tudo junto e deixar filtrar é mais
 * rápido do que adivinhar qual dos dois botões tem o que ele quer.
 *
 * A lista é `LazyColumn` por linha, não um único `Text`: são até 9000 linhas no
 * canal "tudo", e medir isso de uma vez a cada recomposição trava a rolagem.
 */
@Composable
fun LogsScreen(onBack: () -> Unit) {
    val ctx = LocalContext.current
    val clipboard = LocalClipboardManager.current
    var channelIndex by rememberSaveable { mutableStateOf(0) }
    val channel = LogChannel.values()[channelIndex]
    var view by remember { mutableStateOf<SessionTail?>(null) }
    var menu by remember { mutableStateOf(false) }
    BackHandler { onBack() }

    // Ler o arquivo de sessão (até 1 MB do disco, mais o relatório) não pode
    // acontecer no frame em que o dedo toca o botão.
    LaunchedEffect(channel) {
        view = null
        view = withContext(Dispatchers.IO) { BootLog.channel(ctx, channel) }
    }

    val shown = view
    val lines = shown?.lines.orEmpty()
    val state = rememberLazyListState()

    // Num log o que interessa é o fim: o erro e o bloco do crash ficam lá.
    // Abrir no topo obrigaria a rolar o histórico inteiro. `shown` como chave:
    // só rola quando o texto chega (e de novo a cada troca de canal).
    LaunchedEffect(shown) {
        if (lines.isNotEmpty()) state.scrollToItem(lines.lastIndex)
    }

    Column(Modifier.fillMaxSize().padding(horizontal = EdgePad)) {
        // Mesma barra das outras telas internas (fonte de mods): seta, título
        // e a ação à direita. Assim o log não parece um popup solto.
        Row(Modifier.fillMaxWidth().padding(top = 10.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(
                Modifier.semantics { contentDescription = tr("Back") }
                    .pixelClickable(onBack).padding(8.dp),
            ) {
                PixelIcon(R.drawable.ic_seta_esq, 18.dp)
            }
            Column(Modifier.weight(1f).padding(start = 8.dp)) {
                PixelText(tr("Logs"), size = Ts.Head)
                if (shown != null && shown.file.isNotEmpty()) {
                    PixelText(
                        shown.file + " · " + formatSize(shown.totalBytes) +
                            if (shown.truncated) " · " + tr("LogTailOnly") else "",
                        size = Ts.Tiny, color = Bl.TextFaint,
                    )
                }
            }
            // Sem linha nenhuma não há o que copiar: o botão só apareceria
            // para dar um Toast de "nada". Ele copia o canal visível, não tudo.
            if (lines.isNotEmpty()) {
                PixelButton(tr("Copy"), { copySessionLog(ctx, clipboard, lines) }, fontSize = Ts.Small)
            }
        }

        // Seletor de origem. Fica na mesma faixa do título, logo abaixo dele,
        // porque é o primeiro ajuste que se procura ao abrir a tela.
        Row(
            Modifier.fillMaxWidth().padding(top = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            PixelText(
                tr("LogSource"), size = Ts.Small, color = Bl.TextFaint,
                modifier = Modifier.padding(end = 10.dp),
            )
            PixelButton(
                channelName(channel) + (if (menu) "  ^" else "  v"),
                { menu = !menu }, fontSize = Ts.Small,
            )
            if (shown != null && lines.isNotEmpty()) {
                PixelText(
                    "${lines.size}", size = Ts.Tiny, color = Bl.TextFaint,
                    modifier = Modifier.padding(start = 10.dp),
                )
            }
        }

        // As origens são listadas aqui, no fluxo da tela, e não num
        // `DropdownMenu`: aquele abre numa Popup e, neste tema que não usa
        // Material, o fundo dela sai transparente — as linhas do log
        // apareciam por baixo dos itens. Embutida, a lista empurra o log para
        // baixo e desaparece ao escolher, sem popup no caminho.
        if (menu) {
            Column(
                Modifier.fillMaxWidth().padding(top = 8.dp)
                    .pixelPanel(fill = Bl.FrameFill, raised = false)
                    .padding(vertical = 4.dp),
            ) {
                LogChannel.values().forEach { option ->
                    val on = option == channel
                    Box(
                        Modifier.fillMaxWidth()
                            .background(if (on) Bl.Button else Color.Transparent)
                            .pixelClickable { channelIndex = option.ordinal; menu = false }
                            .padding(horizontal = 14.dp, vertical = 11.dp),
                    ) {
                        PixelText(
                            (if (on) "> " else "   ") + channelName(option),
                            size = Ts.Small,
                            color = if (on) Bl.Text else Bl.TextDim,
                        )
                    }
                }
            }
        }

        // O texto do log corre sobre o cenário, e cenário é claro: sem um
        // painel atrás, 12sp de fonte pixelada não se lê no céu.
        Box(
            Modifier.weight(1f).padding(top = 6.dp)
                .pixelPanel(fill = Bl.FrameFill, raised = false).padding(horizontal = 8.dp, vertical = 6.dp)
        ) {
            when {
                shown == null -> PixelText(tr("Loading"), size = Ts.Small, color = Bl.TextDim)
                lines.isEmpty() -> PixelText(
                    // JS não é mais carregado por este build (o loader descarta
                    // o mod com "JS mods are no longer supported"). Dizer isso é
                    // melhor do que um "vazio" que parece bug do filtro.
                    if (channel == LogChannel.Js) tr("NoJsLog") else tr("NoSessionLog"),
                    size = Ts.Small, color = Bl.TextDim,
                    modifier = Modifier.padding(top = 12.dp),
                )
                else -> {
                    LazyColumn(
                        state = state, modifier = Modifier.fillMaxSize(),
                        contentPadding = PaddingValues(bottom = 24.dp),
                    ) {
                        itemsIndexed(lines) { _, line -> LogLine(line) }
                    }
                    PixelScrollbar(state, Modifier.fillMaxSize())
                }
            }
        }
    }
}

/** O nome do canal na língua do app. */
private fun channelName(channel: LogChannel): String = when (channel) {
    LogChannel.All -> tr("LogAll")
    LogChannel.CSharp -> tr("LogCSharp")
    LogChannel.Cpp -> tr("LogCpp")
    LogChannel.Js -> tr("LogJs")
    LogChannel.Java -> tr("LogJava")
}

/**
 * Uma linha do log, colorida pelo nível.
 *
 * O shim grava "12:03:04.567 E [mod] texto": a letra depois da hora é o nível,
 * a mesma posição que o relatório de diagnóstico usa para separar erro e aviso.
 * Sem isso, milhares de linhas iguais e a linha do erro some no meio.
 */
@Composable
private fun LogLine(line: String) {
    val level = line.getOrNull(13)
    val color = when {
        line.contains("=== CRASH NATIVO") || line.contains("=== fim do crash ===") -> Bl.Bad
        level == 'E' -> Bl.Bad
        level == 'W' -> WarnYellow
        line.isBlank() -> Bl.TextFaint
        else -> Bl.TextDim
    }
    Text(
        line,
        color = color,
        fontFamily = PixelFont,
        fontSize = Ts.Tiny.sp,
        lineHeight = (Ts.Tiny + 4).sp,
        modifier = Modifier.fillMaxWidth().padding(vertical = 1.dp),
    )
}

/** O amarelo de aviso do Terraria (o mesmo tom dos textos de dica do menu). */
private val WarnYellow = Color(0xFFE7C15C)

/**
 * Copia as linhas para a área de transferência.
 *
 * O binder do clipboard tem teto de ~1 MB: um log grande estoura e o botão
 * ficaria sem efeito nenhum. Então, se estourar, tentamos metade do texto
 * (de novo, até 200 linhas) — o fim do log, que é o que interessa, continua
 * indo. O usuário vê qual dos dois casos aconteceu pelo Toast.
 *
 * `internal` porque o botão "copiar" das configurações copia sem abrir esta
 * tela; lá as linhas vêm do disco na hora.
 */
internal fun copySessionLog(ctx: Context, clipboard: ClipboardManager, lines: List<String>) {
    if (lines.isEmpty()) return
    var slice = lines
    while (true) {
        try {
            clipboard.setText(AnnotatedString(slice.joinToString("\n")))
            Toast.makeText(ctx, tr("LogCopied", slice.size), Toast.LENGTH_SHORT).show()
            return
        } catch (_: Exception) {
            if (slice.size <= 200) {
                Toast.makeText(ctx, tr("LogCopyFailed"), Toast.LENGTH_LONG).show()
                return
            }
            slice = slice.takeLast(slice.size / 2)
        }
    }
}
