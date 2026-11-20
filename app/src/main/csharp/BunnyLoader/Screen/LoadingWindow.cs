using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader.Core;
using Terraria.UI;

namespace BunnyLoader;

/// <summary>
/// Janela de carregamento DENTRO do jogo.
///
/// A carga dos mods roda na thread da sonda, enquanto a thread da Unity segue
/// desenhando o menu (o splash ja terminou: <c>_isWaitAsyncLoadComplete</c> ja
/// estava ligado quando a sonda comecou). Em vez de uma view do Android por
/// cima do SurfaceView — que nao acompanha a escala do jogo, cobre o menu com
/// um retangulo opaco e some no primeiro frame perdido — o painel e desenhado
/// com o proprio <c>Main.spriteBatch</c>, na mesma matriz (<c>UIScaleMatrix</c>)
/// e com a mesma fonte (<c>Utils.DrawBorderString</c>) da interface do jogo.
///
/// O desenho acontece depois do <c>Main.DoDraw</c> original: nesse ponto o
/// lote do jogo esta fechado, entao abrimos um proprio — e so um Begin/End por
/// quadro, com o mesmo estado que o menu usa.
///
/// Segue o formato de <c>Terraria.ModLoader.Core.SubworldVoteUI</c> (UIState +
/// elementos que se desenham sozinhos): nada aqui pede recurso de mod
/// (<c>ModContent.Request</c>) durante a carga, so o pixel branco do jogo e a
/// fonte do mouse text.
/// </summary>
internal static class LoadingWindow
{
    const string HookKey = "LoadingWindow";

    // Tempo (segundos) para o painel aparecer e para sumir depois dos 100%.
    const float FadeInSeconds = 0.20f;
    const float FadeOutSeconds = 0.45f;

    // Depois do 100% o painel fica parado antes de sumir: as ultimas linhas do
    // log sao justamente as que interessam (o balanco do patch de IL). Com
    // aviso/erro na tela a espera e maior.
    const float HoldQuietSeconds = 0.9f;
    const float HoldNoisySeconds = 2.4f;

    // Log na tela: as ultimas N linhas, como um console. O `il patch` de um mod
    // grande escreve dezenas de linhas; mostrar as antigas nao serve de nada.
    const int LogLines = 6;

    static UserInterface? ui;
    static LoadingState? state;

    // Escrito pela thread da sonda, lido pela thread do jogo.
    static volatile bool visible;
    static volatile string stage = "";
    static int target = -1;
    static int totalMods;

    // Anel de log. Quem escreve e a thread que carrega (uma de cada vez); quem
    // le e a thread do jogo. As strings sao imutaveis, entao nao ha estado
    // meio-escrito: no pior caso uma linha entra um quadro antes do contador.
    static readonly string[] logText = new string[LogLines];
    static readonly byte[] logLevel = new byte[LogLines];
    static readonly object logGate = new();
    static int logCount;
    static volatile bool sawWarning;

    // So a thread do jogo mexe daqui para baixo.
    static float shown;        // porcentagem suavizada (0..100)
    static float alpha;        // opacidade do painel (0..1)
    static float pulse;        // respiracao quando a etapa nao tem porcentagem
    static long lastStamp;
    static long lastReport;
    static bool closing;
    static long holdStamp;
    static long beginStamp;

    // Fator de escala do painel: 1 = base de projeto (1152x648, o BackBuffer do
    // jogo no celular). Sai da largura da tela a cada quadro, entao o painel
    // acompanha aparelho grande, tablet e rotacao em vez de ficar preso a um
    // tamanho fixo. Tudo que e medida em pixel passa por <see cref="S"/>.
    static float uiScale = 1f;
    static float S(float v) => v * uiScale;

    // A sonda pode morrer no meio (JIT, assinatura de mod); sem isto o painel
    // ficaria por cima do menu para sempre. O limite e alto de proposito: um
    // mod grande passa de um minuto so na reescrita do IL (Luminance: 78 s de
    // abertura no aparelho), e fechar o painel aos 40 s escondia justamente o
    // trecho em que o jogador precisa saber que ainda esta andando.
    const double SilentLimitSeconds = 180;

    internal static void Begin(int mods = 0)
    {
        try
        {
            if (mods > 0) totalMods = mods;
            stage = "正在准备…";
            target = -1;
            closing = false;
            holdStamp = 0;
            sawWarning = false;
            lock (logGate) { Array.Clear(logText); Array.Clear(logLevel); logCount = 0; }
            visible = true;
            lastReport = Stopwatch.GetTimestamp();
            beginStamp = lastReport;
            Install();
        }
        catch (Exception e) { ItemHooks.Report("LoadingWindow.Begin", e); }
    }

    /// <summary>
    /// Uma linha de log na tela. Chamado de <see cref="Log"/> a cada linha
    /// escrita — o arquivo continua sendo o registro completo; aqui e so o
    /// console que o jogador ve. Nao faz I/O, nao formata, nao aloca nada
    /// alem do que o chamador ja tinha.
    /// </summary>
    internal static void Line(int level, string text)
    {
        if (text == null || text.Length == 0) return;
        try
        {
            lock (logGate)
            {
                int slot = logCount % LogLines;
                logText[slot] = text;
                logLevel[slot] = (byte)level;
                logCount++;
            }
            if (level > 0) sawWarning = true;
        }
        catch { /* o painel e cosmetico */ }
    }

    /// <summary>Uma etapa qualquer: sem porcentagem vira barra pulsando.</summary>
    internal static void Report(string text, int percent)
    {
        lastReport = Stopwatch.GetTimestamp();
        if (text != null) stage = text;
        if (percent >= 100)
        {
            target = 100;
            closing = true;
            return;
        }
        if (percent >= 0) target = percent;
        if (!visible) visible = true;
    }

    static void Install() => ItemHooks.Once(HookKey, () =>
    {
        try
        {
            On_Main.DoDraw += DoDraw;
            // A porte do HookGate fica FECHADA durante a carga (e por bom
            // motivo: um gancho de mod ai derruba a thread do jogo). Sem esta
            // excecao o `__n` do HookGen le `Chain == null` e chama o original
            // direto: o painel nunca aparece, mesmo com a inscricao feita.
            Hooks.HookChain<On_Main.hook_DoDraw, On_Main.orig_DoDraw>.Always(true);
            Log.Info("loading: janela no jogo instalada (fora da porta de ganchos)");
        }
        catch (Exception e)
        {
            ItemHooks.Report("LoadingWindow.Install", e);
        }
    });

    static void DoDraw(On_Main.orig_DoDraw orig, Main self, GameTime gameTime)
    {
        // Um gancho de mod que estoura entre Begin e End deixa o lote aberto e o
        // Begin do jogo passa a falhar em todo quadro (tela preta da v115). Na
        // virada do quadro fechar e sempre correto, e e a unica forma de o jogo
        // voltar a desenhar sem reiniciar.
        SpriteBatchGuard.Unstick();
        orig(self, gameTime);
        Heartbeat();
        if (!visible) return;
        try { Render(gameTime); }
        catch (Exception e)
        {
            visible = false;
            ItemHooks.Report("LoadingWindow (frame)", e);
        }
    }

    static int frames;

    // Quadros que chegaram ao fim do desenho. O log de erro nao serve para
    // isso: o `__n` do HookGen engole a excecao de todo quadro e o jogo segue
    // rodando sem desenhar nada. Um numero que sobe diz que o quadro fecha.
    static int drawn;

    static void Heartbeat()
    {
        if (++drawn % 600 != 0) return;
        SpriteBatch batch = Main.spriteBatch;
        bool open = batch != null && batch._beginCalled;
        Log.Info($"desenho: {drawn} quadros completos; lote aberto={open}; {Main.screenWidth}x{Main.screenHeight}");
    }

    static void Render(GameTime gameTime)
    {
        SpriteBatch batch = Main.spriteBatch;
        if (batch == null || Main.screenWidth <= 0 || Main.screenHeight <= 0)
        {
            // Sem isto o relatorio so mostra "instalada" e nao diz se o quadro
            // chegou: a diferenca entre "o gancho nao roda" e "roda sem lote".
            if (frames == 0)
                Log.Warn($"loading: quadro sem lote (batch={batch != null}, {Main.screenWidth}x{Main.screenHeight})");
            return;
        }

        if (frames++ == 0)
            Log.Info($"loading: primeiro quadro do painel ({Main.screenWidth}x{Main.screenHeight})");

        float delta = Delta();
        Silent();
        Stage(delta);

        if (ui == null)
        {
            ui = new UserInterface();
            state = new LoadingState();
            state.Activate();
            ui.SetState(state);
        }

        state!.Refresh(stage, shown, target < 0);

        // NAO usar `Main.UIScaleMatrix` aqui. Ele ja reduz tudo pela escala de
        // UI do jogo (~0.6 no celular), e como o painel calcula as medidas em
        // pixels de tela (`Main.screenWidth`), o resultado saia com 60% do
        // tamanho pedido: 82% de largura viravam 49% na tela (v47). O lote do
        // menu usa essa matriz porque os elementos de UI do jogo sao medidos em
        // unidades de UI; o painel e medido em pixels, entao desenha 1:1.
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None,
            Main.Rasterizer, null, Matrix.Identity, true);
        ui.Draw(batch, gameTime);
        batch.End();

        if (alpha <= 0f && closing) visible = false;
    }

    static float Delta()
    {
        long now = Stopwatch.GetTimestamp();
        float elapsed = lastStamp != 0 ? (float)((now - lastStamp) / (double)Stopwatch.Frequency) : 1f / 60f;
        lastStamp = now;
        return Math.Clamp(elapsed, 0f, 0.25f);
    }

    /// <summary>Fecha o painel quando nada mais vai chegar (a carga morreu calada).</summary>
    static void Silent()
    {
        if (closing || lastReport == 0) return;
        double idle = (Stopwatch.GetTimestamp() - lastReport) / (double)Stopwatch.Frequency;
        if (idle < SilentLimitSeconds) return;
        Log.Warn($"loading: {idle:0} s sem etapa nova; fechando o painel");
        closing = true;
        target = 100;
    }

    /// <summary>Suavizacao: o alvo anda aos saltos (uma etapa por mod), a barra nao.</summary>
    static void Stage(float delta)
    {
        int goal = target < 0 ? 0 : Math.Clamp(target, 0, 100);
        if (target > 0 || closing)
        {
            // Nunca volta: duas etapas podem calcular o mesmo trecho, mas a
            // barra andando para tras parece travamento.
            if (shown < goal) shown += (goal - shown) * Math.Min(1f, 0.18f + delta * 6f);
            if (goal - shown < 0.35f) shown = goal;
        }

        pulse += delta * 1.6f;
        if (closing && shown >= 99.5f)
        {
            // Segura o painel cheio por um instante: as ultimas linhas do log
            // (o balanco do patch de IL) somem junto com ele se desvanecer na
            // hora. Com aviso na tela a espera e maior.
            if (holdStamp == 0) holdStamp = Stopwatch.GetTimestamp();
            double held = (Stopwatch.GetTimestamp() - holdStamp) / (double)Stopwatch.Frequency;
            if (held >= (sawWarning ? HoldNoisySeconds : HoldQuietSeconds))
                alpha = Math.Max(0f, alpha - delta / FadeOutSeconds);
        }
        else
        {
            holdStamp = 0;
            alpha = Math.Min(1f, alpha + delta / FadeInSeconds);
        }
    }

    // ------------------------------------------------------------------
    // Elementos: todos se desenham sozinhos, como SubworldVoteUI.Label.
    // ------------------------------------------------------------------

    sealed class Backdrop : UIElement
    {
        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            float a = alpha;

            // Escurece o menu: o painel precisa de fundo, e o jogador ve que o
            // jogo esta ocupado sem perder a referencia do menu por tras.
            spriteBatch.Draw(pixel, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), new Color(4, 6, 16) * (0.55f * a));

            CalculatedStyle d = GetDimensions();
            int pad = Math.Max(1, (int)S(2f));
            var box = new Rectangle((int)d.X, (int)d.Y, (int)d.Width, (int)d.Height);
            spriteBatch.Draw(pixel, new Rectangle(box.X - pad, box.Y - pad, box.Width + pad * 2, box.Height + pad * 2), new Color(10, 14, 34) * a);
            spriteBatch.Draw(pixel, box, new Color(52, 66, 128) * (0.96f * a));
            spriteBatch.Draw(pixel, new Rectangle(box.X + pad, box.Y + pad, box.Width - pad * 2, box.Height - pad * 2), new Color(44, 56, 112) * (0.96f * a));
            // Fio claro no topo: o mesmo brilho do UIPanel do jogo.
            spriteBatch.Draw(pixel, new Rectangle(box.X + pad, box.Y + pad, box.Width - pad * 2, Math.Max(1, (int)S(2f))), new Color(150, 170, 230) * (0.55f * a));
        }
    }

    sealed class Caption : UIElement
    {
        internal string Text = "";
        internal float TextScale = 0.85f;
        internal Color Tint = new(235, 238, 248);
        internal float Anchor = 0.5f;

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            if (Text.Length == 0) return;
            CalculatedStyle d = GetDimensions();
            float scale = TextScale * uiScale;
            float height = 27f * scale;
            var at = new Vector2(d.X + d.Width * Anchor, d.Y + (d.Height - height) * 0.5f);
            Utils.DrawBorderString(spriteBatch, Text, at, Tint * alpha, scale, Anchor, 0f, -1);
        }
    }

    /// <summary>Barra + porcentagem no mesmo elemento: a porcentagem fica colada na ponta.</summary>
    sealed class Progress : UIElement
    {
        internal float Value;      // 0..1
        internal bool Unknown;
        internal int Percent;

        const float LabelWidth = 88f;

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            CalculatedStyle d = GetDimensions();
            float a = alpha;
            int barWidth = (int)Math.Max(0, d.Width - S(LabelWidth));
            var box = new Rectangle((int)d.X, (int)d.Y, barWidth, (int)d.Height);

            spriteBatch.Draw(pixel, box, new Color(10, 16, 40) * a);
            var inner = new Rectangle(box.X + 2, box.Y + 2, Math.Max(0, box.Width - 4), Math.Max(0, box.Height - 4));
            spriteBatch.Draw(pixel, inner, new Color(18, 28, 66) * a);

            float fill = Unknown ? pulse : Value;
            int width = (int)(inner.Width * Math.Clamp(fill, 0f, 1f));
            if (width > 0)
            {
                spriteBatch.Draw(pixel, new Rectangle(inner.X, inner.Y, width, inner.Height), new Color(226, 208, 74) * a);
                spriteBatch.Draw(pixel, new Rectangle(inner.X, inner.Y, width, 2), new Color(255, 246, 168) * (0.85f * a));
            }

            string label = Unknown ? "…" : Percent + "%";
            float labelScale = 0.78f * uiScale;
            Utils.DrawBorderString(spriteBatch, label,
                new Vector2(box.Right + S(16f), d.Y + (d.Height - 27f * labelScale) * 0.5f),
                new Color(255, 236, 140) * a, labelScale, 0f, 0f, -1);
        }
    }

    /// <summary>
    /// O console do carregamento: as ultimas linhas escritas por <see cref="Log"/>,
    /// como o log do tModLoader de PC.
    ///
    /// O `il patch` de um mod grande escreve dezenas de linhas ("N campos
    /// reescritos, M sem traducao, K corpos esvaziados" e depois um por campo
    /// que falta no shim). Sem isso na tela, o jogador so ve "正在重写 IL…" e
    /// nao tem como saber se o patch andou, parou ou desistiu — o arquivo de
    /// log so serve depois, fora do jogo.
    /// </summary>
    sealed class LogPanel : UIElement
    {
        const float LineHeight = 19f;
        const float Scale = 0.56f;

        static readonly Color Normal = new(176, 188, 210);
        static readonly Color Warn = new(232, 214, 96);
        static readonly Color Err = new(242, 132, 112);

        // Medir a fonte a cada quadro para cada linha e caro (MeasureString
        // atravessa o bridge). A linha so muda quando muda, entao guardamos o
        // resultado por posicao do anel e por largura.
        readonly string[] src = new string[LogLines];
        readonly string[] fitted = new string[LogLines];
        readonly bool[] guessed = new bool[LogLines];
        float fittedWidth;
        float fittedScale;
        int measureFailures;
        bool noMeasure;
        bool lastGuess;

        float DrawScale => Scale * uiScale;

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            int total = Volatile.Read(ref logCount);
            if (total == 0) return;

            float lineHeight = S(LineHeight);
            CalculatedStyle d = GetDimensions();
            int fit = (int)(d.Height / lineHeight);
            if (fit > LogLines) fit = LogLines;
            if (fit < 1) fit = 1;
            int from = Math.Max(0, total - fit);

            float avail = Math.Max(40f, d.Width - S(4f));
            float scale = DrawScale;
            if (Math.Abs(fittedWidth - avail) > 0.5f || Math.Abs(fittedScale - scale) > 0.001f)
            {
                fittedWidth = avail;
                fittedScale = scale;
                Array.Clear(src);
            }

            float a = alpha;
            for (int i = 0; i < fit; i++)
            {
                int slot = (from + i) % LogLines;
                string? text = logText[slot];
                if (text == null) continue;
                if (!ReferenceEquals(src[slot], text) || (guessed[slot] && !noMeasure))
                {
                    src[slot] = text;
                    fitted[slot] = Fit(text, avail);
                    guessed[slot] = lastGuess;
                }
                Color tint = logLevel[slot] switch
                {
                    2 => Err,
                    1 => Warn,
                    _ => Normal,
                };
                Utils.DrawBorderString(spriteBatch, fitted[slot] ?? text,
                    new Vector2(d.X, d.Y + i * lineHeight), tint * a, scale, 0f, 0f, -1);
            }
        }

        /// <summary>Corta pelo que a fonte mede; sem fonte, por estimativa de largura.</summary>
        string Fit(string text, float width)
        {
            lastGuess = false;
            float scale = DrawScale;
            if (!noMeasure)
            {
                try
                {
                    var font = FontAssets.MouseText.Value;
                    float measured = font.MeasureString(text).X * scale;
                    if (measured <= width) return text;
                    int len = Math.Max(8, (int)(text.Length * (width - S(14f)) / measured));
                    // A estimativa erra para mais ou para menos conforme o
                    // alfabeto; duas medidas de ajuste bastam.
                    for (int tries = 0; tries < 3 && len > 8; tries++)
                    {
                        if (font.MeasureString(text.Substring(0, len)).X * scale <= width) break;
                        len -= Math.Max(2, len / 12);
                    }
                    return text.Substring(0, Math.Max(1, len - 1)) + "…";
                }
                catch
                {
                    // A fonte pode ainda nao estar carregada no primeiro quadro
                    // (Asset.Value devolve nulo). Nao desistir da medicao por
                    // causa disso: so depois de muitas falhas seguidas.
                    lastGuess = true;
                    if (++measureFailures > 60) noMeasure = true;
                }
            }
            lastGuess = true;
            return Estimate(text, width);
        }

        string Estimate(string text, float width)
        {
            // Ideograma ocupa ~2 posicoes: contar tudo como 1 enchia a linha
            // de texto chines para fora do painel.
            float budget = width / (7.4f * DrawScale);
            float used = 0f;
            int len = 0;
            for (; len < text.Length; len++)
            {
                used += text[len] < 0x2E80 ? 1f : 2f;
                if (used > budget) break;
            }
            if (len >= text.Length) return text;
            return text.Substring(0, Math.Max(1, len - 1)) + "…";
        }
    }

    sealed class LoadingState : UIState
    {
        // Alturas fixas do topo; o resto do painel e o console.
        const float BarTop = 74f;
        const float LogTop = 100f;
        const float BottomPad = 34f;   // assinatura + respiro

        Backdrop backdrop = null!;
        Caption title = null!, stage = null!, signature = null!, clock = null!;
        Progress bar = null!;
        LogPanel log = null!;
        string shownStage = "";
        int shownPercent = -2;
        int shownClock = -1;
        float shownHeight = -1f;

        public override void OnInitialize()
        {
            backdrop = new Backdrop();
            backdrop.HAlign = 0.5f;
            backdrop.VAlign = 0.42f;
            Append(backdrop);

            title = new Caption {
                Text = "正在加载模组",
                TextScale = 0.95f,
                Tint = new Color(255, 232, 132),
                Anchor = 0.5f,
            };
            title.Width.Set(0f, 1f);
            title.Height.Set(28f, 0f);
            title.Top.Set(12f, 0f);
            backdrop.Append(title);

            stage = new Caption { TextScale = 0.78f, Anchor = 0.5f };
            stage.Width.Set(0f, 1f);
            stage.Height.Set(24f, 0f);
            stage.Top.Set(44f, 0f);
            backdrop.Append(stage);

            // Relogio no canto. Quem desenha e a thread do jogo, entao ele anda
            // mesmo quando a thread de carga esta presa dentro de UMA chamada
            // (Cecil lendo um DLL grande, Assembly.Load): sem isto o painel
            // parece congelado e a pessoa mata o jogo achando que travou.
            clock = new Caption {
                TextScale = 0.62f,
                Tint = new Color(150, 162, 198),
                Anchor = 1f,
            };
            clock.Left.Set(24f, 0f);
            clock.Width.Set(-24f, 1f);
            clock.Height.Set(28f, 0f);
            clock.Top.Set(12f, 0f);
            backdrop.Append(clock);

            bar = new Progress();
            bar.Left.Set(24f, 0f);
            bar.Top.Set(BarTop, 0f);
            bar.Width.Set(-48f, 1f);
            bar.Height.Set(18f, 0f);
            backdrop.Append(bar);

            log = new LogPanel();
            log.Left.Set(26f, 0f);
            log.Top.Set(LogTop, 0f);
            log.Width.Set(-52f, 1f);
            backdrop.Append(log);

            signature = new Caption {
                Text = "Bunny Loader",
                TextScale = 0.62f,
                Tint = new Color(158, 170, 205),
                Anchor = 0f,
            };
            signature.Left.Set(24f, 0f);
            signature.Width.Set(0f, 0f);
            signature.Height.Set(18f, 0f);
            backdrop.Append(signature);
        }

        internal void Refresh(string text, float percentage, bool unknown)
        {
            float sw = Main.screenWidth;
            float sh = Main.screenHeight;
            if (sw < 40 || sh < 40) return;

            // Base de projeto 1152x648. O fator sai da tela REAL a cada quadro,
            // entao celular grande, tablet e rotacao mudam o tamanho do painel
            // em vez de herdar o de uma resolucao fixa. Sem teto: 82% e 82% em
            // qualquer aparelho.
            float k = Math.Clamp(sw / 1152f, 0.6f, 3f);
            bool rescaled = Math.Abs(uiScale - k) > 0.002f;
            uiScale = k;

            float width = sw * 0.82f;
            if (width > sw - S(32f)) width = Math.Max(S(220f), sw - S(32f));
            // Altura: cabe o console todo em tela grande; em tela baixa o
            // console encolhe (uma linha que seja) em vez de sair do quadro.
            float height = Math.Min(S(252f), Math.Max(S(148f), sh * 0.62f));

            if (rescaled || Math.Abs(backdrop.Width.Pixels - width) > 0.5f || Math.Abs(shownHeight - height) > 0.5f)
            {
                shownHeight = height;
                backdrop.Width.Set(width, 0f);
                backdrop.Height.Set(height, 0f);
                title.Top.Set(S(12f), 0f);
                title.Height.Set(S(28f), 0f);
                stage.Top.Set(S(44f), 0f);
                stage.Height.Set(S(24f), 0f);
                clock.Left.Set(S(24f), 0f);
                clock.Width.Set(-S(24f), 1f);
                clock.Height.Set(S(28f), 0f);
                clock.Top.Set(S(12f), 0f);
                bar.Left.Set(S(24f), 0f);
                bar.Top.Set(S(BarTop), 0f);
                bar.Width.Set(-S(48f), 1f);
                bar.Height.Set(S(18f), 0f);
                log.Left.Set(S(26f), 0f);
                log.Top.Set(S(LogTop), 0f);
                log.Width.Set(-S(52f), 1f);
                log.Height.Set(Math.Max(S(19f), height - S(BottomPad) - S(LogTop)), 0f);
                signature.Left.Set(S(24f), 0f);
                signature.Top.Set(height - S(26f), 0f);
                signature.Height.Set(S(18f), 0f);
                Recalculate();
            }

            string trimmed = text ?? "";
            if (trimmed.Length > 52) trimmed = trimmed.Substring(0, 51) + "…";
            if (trimmed != shownStage)
            {
                shownStage = trimmed;
                stage.Text = trimmed;
            }

            int percent = (int)Math.Round(percentage);
            if (percent != shownPercent || bar.Unknown != unknown)
            {
                shownPercent = percent;
                bar.Percent = percent;
                bar.Value = percent / 100f;
                bar.Unknown = unknown;
            }

            long start = beginStamp != 0 ? beginStamp : lastReport;
            if (start != 0)
            {
                int seconds = (int)((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency);
                if (seconds != shownClock)
                {
                    shownClock = seconds;
                    clock.Text = seconds.ToString() + " 秒";
                }
            }
        }
    }
}
