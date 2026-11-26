package dev.bunnyloader

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.imageResource
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import dev.bunnyloader.game.Eligibility
import dev.bunnyloader.ui.AppSplash
import dev.bunnyloader.ui.Bl
import dev.bunnyloader.ui.ExplorarTab
import dev.bunnyloader.ui.InicioTab
import dev.bunnyloader.ui.ModBackdrop
import dev.bunnyloader.ui.ModDetail
import dev.bunnyloader.ui.ModUpdatesDialog
import dev.bunnyloader.ui.GameBlockedDialog
import dev.bunnyloader.ui.LanguageConfirmDialog
import dev.bunnyloader.ui.PacotesTab
import dev.bunnyloader.ui.ConfigTab
import dev.bunnyloader.ui.PixelFont
import dev.bunnyloader.ui.PixelIcon
import dev.bunnyloader.ui.PixelCorner
import dev.bunnyloader.ui.pixelPanel
import dev.bunnyloader.ui.UiLocalization
import dev.bunnyloader.ui.tr
import dev.bunnyloader.ui.PixelText
import dev.bunnyloader.ui.mix
import dev.bunnyloader.ui.Shell
import dev.bunnyloader.ui.Ts
import dev.bunnyloader.ui.Biome
import dev.bunnyloader.ui.Prefs
import dev.bunnyloader.ui.Scenery
import dev.bunnyloader.ui.CardFx
import dev.bunnyloader.ui.LocalRemoteFiles
import dev.bunnyloader.ui.LocalRemoteMedia
import dev.bunnyloader.ui.LocalThemeFx
import dev.bunnyloader.ui.ThemeFx
import dev.bunnyloader.ui.cardFx

/**
 * Launcher do Bunny Loader.
 *
 * Quatro abas num rodapé de terra, com o botão de jogar erguido no meio: a
 * navegação fica nas bordas e o centro é a única ação que importa.
 */
class LauncherActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        UiLocalization.initialize(this)
        val model = ViewModelProvider(this, object : ViewModelProvider.Factory {
            override fun <T : ViewModel> create(modelClass: Class<T>): T {
                require(modelClass == LauncherState::class.java)
                return modelClass.cast(LauncherState(Shell(applicationContext)))
            }
        })[LauncherState::class.java]
        window.setBackgroundDrawableResource(R.color.splash_background)
        setContent {
            var splash by rememberSaveable { mutableStateOf(true) }
            dev.bunnyloader.ui.BunnyTheme {
                Box(Modifier.fillMaxSize()) {
                    LauncherScreen(model.shell, showDialogs = !splash)
                    if (splash) AppSplash({ !model.shell.refreshingPackages }) { splash = false }
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        dev.bunnyloader.game.AotImages.requestSync(this)
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) hideNavigationBar()
    }

    private fun hideNavigationBar() {
        WindowCompat.getInsetsController(window, window.decorView).apply {
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            hide(WindowInsetsCompat.Type.navigationBars())
        }
    }
}

/** Mantém downloads, importações e a mesma trava durante recriações da Activity. */
private class LauncherState(val shell: Shell) : ViewModel() {
    override fun onCleared() { shell.close() }
}

private enum class Tab(val icon: Int, val label: String) {
    INICIO(R.drawable.ic_tab_inicio, "Home"),
    EXPLORAR(R.drawable.ic_tab_explorar, "Explore"),
    PACOTES(R.drawable.ic_tab_pacotes, "Packages"),
    CONFIG(R.drawable.ic_config, "Settings"),
}

@Composable
private fun LauncherScreen(shell: Shell, showDialogs: Boolean) {
    val ctx = LocalContext.current
    val prefs = remember { Prefs(ctx) }
    // Saveable: o seletor de arquivos (foto do perfil, mundos) pode fazer o
    // sistema recriar a Activity, e a resposta dele só chega à aba que pediu
    // se ela for a mesma na volta.
    var tab by rememberSaveable { mutableStateOf(Tab.INICIO) }
    var blocked by remember { mutableStateOf<Eligibility.Status?>(null) }
    var askLanguage by remember { mutableStateOf(!prefs.launchedBefore) }
    var openMod by rememberSaveable { mutableStateOf<String?>(null) }
    val tabState = rememberSaveableStateHolder()
    var sceneryChoice by remember { mutableStateOf(prefs.scenery) }
    // Saveable: girar a tela recria a Activity, e no automático isso trocaria
    // o cenário no meio do uso.
    val biome = Biome.entries[rememberSaveable(sceneryChoice) {
        prefs.resolveScenery(sceneryChoice).ordinal
    }]
    // A paleta antes de qualquer tela ler uma cor: o primeiro quadro já sai no tom do tema.
    remember(biome) { dev.bunnyloader.ui.Bl.palette = biome.palette }
    var themeEffects by remember { mutableStateOf(prefs.themeEffects) }
    // Início sem nada por cima: só o cenário (ver InicioTab).
    var immersive by remember { mutableStateOf(false) }
    androidx.compose.runtime.LaunchedEffect(tab, openMod) {
        if (tab != Tab.INICIO || openMod != null) immersive = false
    }

    BackHandler(enabled = openMod != null) { openMod = null }
    BackHandler(enabled = immersive) { immersive = false }

    // Um relógio para cenário e cartões, limitado a 30 fps (20 sem efeitos).
    // As leituras ficam no desenho: a animação não recompõe a navegação.
    val fxTime = remember { androidx.compose.runtime.mutableFloatStateOf(0f) }
    val fxLifecycle = androidx.compose.ui.platform.LocalLifecycleOwner.current.lifecycle
    androidx.compose.runtime.LaunchedEffect(themeEffects, fxLifecycle) {
        val origin = android.os.SystemClock.uptimeMillis()
        fxLifecycle.repeatOnLifecycle(androidx.lifecycle.Lifecycle.State.STARTED) {
            var previous = 0L
            while (true) {
                androidx.compose.runtime.withFrameMillis { frame ->
                    if (frame - previous >= if (themeEffects) 32L else 49L) {
                        fxTime.floatValue = (android.os.SystemClock.uptimeMillis() - origin) / 1000f
                        previous = frame
                    }
                }
            }
        }
    }
    val fx = remember(biome, themeEffects) {
        ThemeFx(if (themeEffects) biome.cardFx() else CardFx.NONE, fxTime)
    }

    // Voltou do jogo ou do gerenciador de arquivos: relê bunny_packs, que pode
    // ter pacote novo ou editado à mão.
    val lifecycle = androidx.compose.ui.platform.LocalLifecycleOwner.current.lifecycle
    androidx.compose.runtime.DisposableEffect(lifecycle) {
        val observer = androidx.lifecycle.LifecycleEventObserver { _, event ->
            if (event == androidx.lifecycle.Lifecycle.Event.ON_RESUME) {
                shell.rescan()
                // Sem forçar: voltar do jogo dentro do intervalo não pede a
                // lista de novo (e, passado o intervalo, o ETag evita baixá-la).
                shell.refreshRemote()
            }
        }
        lifecycle.addObserver(observer)
        onDispose { lifecycle.removeObserver(observer) }
    }

    // A câmera do cenário dá um passo por aba (e meio ao abrir um mod): é o
    // que faz as camadas deslizarem cada uma no seu ritmo.
    val pan by animateFloatAsState(
        tab.ordinal * 240f + (if (openMod != null) 120f else 0f),
        tween(1400, easing = FastOutSlowInEasing), label = "camera",
    )

    val hide by animateFloatAsState(if (immersive) 1f else 0f, tween(450, easing = FastOutSlowInEasing),
        label = "immersive")

    // Guardada: `shell::requestRemoteFiles` é um objeto novo a cada recomposição,
    // e um valor novo num staticCompositionLocal recompõe a tela inteira.
    val requestFiles = remember(shell) { shell::requestRemoteFiles }
    val mediaEntry = remember(shell) { shell::mediaEntry }
    androidx.compose.runtime.CompositionLocalProvider(
        LocalThemeFx provides fx,
        LocalRemoteFiles provides requestFiles,
        LocalRemoteMedia provides mediaEntry,
    ) {
    Box(Modifier.fillMaxSize().background(androidx.compose.material3.MaterialTheme.colorScheme.background)) {
        // A ficha aberta pode trazer o fundo do mod (theme.background).
        openMod?.let { shell.entry(it) }?.let { entry ->
            androidx.compose.runtime.key(entry.uid) { ModBackdrop(entry, shell.catalog, Modifier.fillMaxSize()) }
        }
        Row(Modifier.fillMaxSize().statusBarsPadding()) {
            LauncherRail(
                current = tab,
                onSelect = { tab = it; openMod = null },
                onStart = {
                    val status = Eligibility.check(ctx)
                    if (status == Eligibility.Status.OK) ctx.startActivity(dev.bunnyloader.compile.CompileActivity.gameIntent(ctx))
                    else blocked = status
                },
                modifier = Modifier.fillMaxHeight()
                    .padding(start = 8.dp, top = 8.dp, bottom = 8.dp)
                    .graphicsLayer {
                        translationX = -size.width * hide
                        alpha = 1f - hide
                    },
            )
            // Teto de largura: solto num tablet ou no emulador, uma linha de
            // mod com 1600px vira uma faixa vazia com um ícone perdido na
            // esquerda. Deitado num celular (~900dp) o teto quase não aparece.
            Box(
                Modifier.weight(1f).fillMaxHeight()
                    .padding(end = 8.dp).graphicsLayer {
                        translationY = -size.height * hide
                        alpha = 1f - hide
                    },
                contentAlignment = Alignment.TopCenter,
            ) {
                Box(Modifier.widthIn(max = 980.dp).fillMaxSize()) {
                    val detail = openMod?.let { shell.entry(it) }
                    if (detail != null) {
                        ModDetail(detail, shell) { openMod = null }
                    } else {
                        tabState.SaveableStateProvider(tab.name) {
                            when (tab) {
                                Tab.INICIO -> InicioTab(shell, onHide = { immersive = true }) { openMod = it }
                                Tab.EXPLORAR -> ExplorarTab(shell) { openMod = it }
                                Tab.PACOTES -> PacotesTab(shell) { openMod = it }
                                Tab.CONFIG -> ConfigTab(
                                    shell, sceneryChoice,
                                    onScenery = {
                                        sceneryChoice = it
                                        prefs.scenery = it
                                    },
                                    themeEffects = themeEffects,
                                    onThemeEffects = {
                                        themeEffects = it
                                        prefs.themeEffects = it
                                    },
                                )
                            }
                        }
                    }
                }
            }
        }
        if (immersive) ImmersiveOverlay { immersive = false }
        if (showDialogs) ModUpdatesDialog(shell)
        blocked?.let { GameBlockedDialog(it) { blocked = null } }
        if (showDialogs && askLanguage) LanguageConfirmDialog {
            prefs.launchedBefore = true
            askLanguage = false
        }
    }
    }
}

/**
 * Por cima de tudo enquanto o Início está escondido: pega o arraste para baixo
 * (que traz a interface de volta) e mostra a dica por alguns segundos.
 */
@Composable
private fun ImmersiveOverlay(onRestore: () -> Unit) {
    val threshold = with(androidx.compose.ui.platform.LocalDensity.current) { 64.dp.toPx() }
    var hint by remember { mutableStateOf(true) }
    androidx.compose.runtime.LaunchedEffect(Unit) {
        kotlinx.coroutines.delay(2500)
        hint = false
    }
    val hintAlpha by animateFloatAsState(if (hint) 1f else 0f, tween(600), label = "hint")
    Box(
        Modifier.fillMaxSize().pointerInput(Unit) {
            var pulled = 0f
            detectVerticalDragGestures(
                onDragStart = { pulled = 0f },
                onVerticalDrag = { change, amount ->
                    change.consume()
                    pulled += amount
                    if (pulled > threshold) onRestore()
                },
            )
        },
    ) {
        PixelText(
            tr("SwipeDownToReturn"),
            size = Ts.Small, color = Bl.TextDim,
            modifier = Modifier.align(Alignment.BottomCenter).navigationBarsPadding()
                .padding(bottom = 24.dp).graphicsLayer { alpha = hintAlpha },
        )
    }
}

// ------------------------------- lateral -------------------------------

/**
 * A navegação do launcher deitado: uma coluna de slots à esquerda, como a
 * barra de itens do Terraria em pé, com o botão de jogar no pé da coluna.
 *
 * Deitado, uma barra de rodapé comeria a altura que a lista de pacotes mais
 * precisa — e a coluna ainda deixa o polegar esquerdo alcançar tudo.
 */
@Composable
private fun LauncherRail(
    current: Tab,
    onSelect: (Tab) -> Unit,
    onStart: () -> Unit,
    modifier: Modifier = Modifier,
) {
    // A coluna do Material: superfície arredondada, sem moldura pixelada.
    Column(
        modifier.width(112.dp)
            .clip(androidx.compose.foundation.shape.RoundedCornerShape(24.dp))
            .background(androidx.compose.material3.MaterialTheme.colorScheme.surfaceContainer)
            .padding(8.dp),
        verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        for (tab in Tab.entries) RailButton(tab, current, onSelect)
        Spacer(Modifier.weight(1f))
        RailStart(onStart)
    }
}

/** Um destino da navegação: pílula do Material quando é o atual. */
@Composable
private fun RailButton(tab: Tab, current: Tab, onSelect: (Tab) -> Unit) {
    val selected = tab == current
    val scheme = androidx.compose.material3.MaterialTheme.colorScheme
    Row(
        Modifier.fillMaxWidth()
            .clip(androidx.compose.foundation.shape.RoundedCornerShape(50))
            .background(if (selected) scheme.secondaryContainer else Color.Transparent)
            .clickable(onClick = { onSelect(tab) })
            .padding(horizontal = 8.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        PixelIcon(tab.icon, 20.dp, alpha = if (selected) 1f else 0.8f)
        PixelText(
            tr(tab.label),
            size = Ts.Tiny,
            color = if (selected) scheme.onSecondaryContainer else scheme.onSurfaceVariant,
            maxLines = 1,
            modifier = Modifier.padding(start = 6.dp),
        )
    }
}

/** O "jogar": o botão principal do Material, na largura da coluna. */
@Composable
private fun RailStart(onStart: () -> Unit) {
    androidx.compose.material3.Button(
        onClick = onStart,
        modifier = Modifier.fillMaxWidth(),
        shape = androidx.compose.material3.MaterialTheme.shapes.medium,
        contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 8.dp, vertical = 10.dp),
    ) {
        PixelIcon(R.drawable.ic_start, 14.dp)
        PixelText(
            tr("Play"), size = Ts.Tiny, maxLines = 1,
            modifier = Modifier.padding(start = 6.dp),
        )
    }
}
