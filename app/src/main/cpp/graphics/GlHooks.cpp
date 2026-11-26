#include "graphics/GlHooks.h"
#include "graphics/GlApi.h"
#include "graphics/GlShadow.h"
#include "graphics/GpuTimer.h"
#include "graphics/EffectRenderer.h"
#include "graphics/RenderHook.h"
#include "core/Log.h"
#include "hook/HookManager.h"

#include <EGL/egl.h>
#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <dlfcn.h>
#include <map>
#include <mutex>
#include <string>
#include <sys/prctl.h>

namespace bl::graphics {

namespace {

using SwapFn = EGLBoolean (*)(EGLDisplay, EGLSurface);
using BindFramebufferFn = void (*)(GLenum, GLuint);
using ViewportFn = void (*)(GLint, GLint, GLsizei, GLsizei);
using ClearFn = void (*)(GLbitfield);
using DrawElementsFn = void (*)(GLenum, GLsizei, GLenum, const void*);
using DrawArraysFn = void (*)(GLenum, GLint, GLsizei);
using BlitFn = void (*)(GLint, GLint, GLint, GLint, GLint, GLint, GLint, GLint, GLbitfield, GLenum);
using UintFn = void (*)(GLuint);
using EnumFn = void (*)(GLenum);
using ColorMaskFn = void (*)(GLboolean, GLboolean, GLboolean, GLboolean);
using ScissorFn = void (*)(GLint, GLint, GLsizei, GLsizei);
using BlendFuncFn = void (*)(GLenum, GLenum);
using BlendFuncSeparateFn = void (*)(GLenum, GLenum, GLenum, GLenum);
using ClearColorFn = void (*)(GLfloat, GLfloat, GLfloat, GLfloat);
using DepthMaskFn = void (*)(GLboolean);
using DepthFuncFn = void (*)(GLenum);
using BlendEquationFn = void (*)(GLenum);
using BindTextureFn = void (*)(GLenum, GLuint);
using ShaderSourceFn = void (*)(GLuint, GLsizei, const GLchar* const*, const GLint*);
using LinkProgramFn = void (*)(GLuint);

SwapFn g_origSwap = nullptr;
BindFramebufferFn g_origBindFramebuffer = nullptr;
ViewportFn g_origViewport = nullptr;
ClearFn g_origClear = nullptr;
DrawElementsFn g_origDrawElements = nullptr;
DrawArraysFn g_origDrawArrays = nullptr;
BlitFn g_origBlit = nullptr;
UintFn g_origUseProgram = nullptr;
UintFn g_origBindVertexArray = nullptr;
EnumFn g_origActiveTexture = nullptr;
EnumFn g_origEnable = nullptr;
EnumFn g_origDisable = nullptr;
ColorMaskFn g_origColorMask = nullptr;
ScissorFn g_origScissor = nullptr;
BlendFuncFn g_origBlendFunc = nullptr;
BlendFuncSeparateFn g_origBlendFuncSeparate = nullptr;
ClearColorFn g_origClearColor = nullptr;
DepthMaskFn g_origDepthMask = nullptr;
DepthFuncFn g_origDepthFunc = nullptr;
BlendEquationFn g_origBlendEquation = nullptr;
BindTextureFn g_origBindTexture = nullptr;
ShaderSourceFn g_origShaderSource = nullptr;
LinkProgramFn g_origLinkProgram = nullptr;

// As nossas proprias chamadas GL (o desenho do efeito) passam pelos mesmos
// hooks: nao entram no rastro nem mudam o espelho do estado da Unity.
thread_local bool t_inside = false;

// So a thread de render da Unity conta. O RenderThread do Android (as Views
// do app, o botao flutuante) chama as mesmas funcoes num OUTRO contexto GL, e
// estragaria o espelho. Com render multithread a Unity desenha na
// UnityGfxDeviceW; sem, na UnityMain.
thread_local int t_unity = -1;

bool unityThread() {
    if (t_unity < 0) {
        char name[17] = {0};   // PR_GET_NAME: ate 16 bytes (pthread_getname_np so na API 26)
        prctl(PR_GET_NAME, name);
        t_unity = std::strncmp(name, "UnityGfxDevice", 14) == 0 || std::strcmp(name, "UnityMain") == 0;
    }
    return t_unity == 1;
}

bool tracked() {
    return !t_inside && unityThread();
}

// O espelho do estado da Unity (GlShadow.h) e onde foi o ultimo desenho dela:
// quando o marcador aparece, o framebuffer ja e o do marcador, e o do mundo e
// o do desenho anterior.
GlShadow g_shadow;
DrawTarget g_lastDraw;
bool g_synced = false;
bool g_trustShadow = true;
std::atomic<int64_t> g_shadowChecks{0};
std::atomic<int64_t> g_shadowMismatches{0};
constexpr int64_t kShadowChecks = 120;

// O estado da Unity agora: o espelho, conferido com o driver nos primeiros
// usos. Uma diferenca e o espelho deixa de valer (o glGet* volta de vez).
GlShadow unityState() {
    if (!g_synced) {
        g_shadow = queryGlState();
        g_synced = true;
    }
    if (!g_trustShadow) return queryGlState();
    if (g_shadowChecks < kShadowChecks) {
        g_shadowChecks++;
        const GlShadow real = queryGlState();
        char diff[160];
        if (compareGlState(g_shadow, real, diff, sizeof diff) > 0) {
            g_shadowMismatches++;
            g_trustShadow = false;
            BL_ERROR("efeitos de tela: espelho do estado GL errado (%s); volta para glGet*", diff);
            g_shadow = real;
            return real;
        }
    }
    return g_shadow;
}

void noteDraw() {
    g_lastDraw.fbo = static_cast<int>(g_shadow.drawFbo);
    for (int i = 0; i < 4; ++i) g_lastDraw.viewport[i] = g_shadow.viewport[i];
}

// O rastro: pedido pela thread do jogo, gravado pela de render entre dois
// eglSwapBuffers.
std::atomic<bool> g_traceRequested{false};
// v49: no quadro rastreado tambem lemos os pixels (ver dumpFramebuffer).
std::atomic<bool> g_dumpPending{false};
GLuint g_lastOffscreenFbo = 0;
int g_dumpCount = 0;
bool g_tracing = false;
std::string g_trace;
int g_pendingDraws = 0;

void flushDraws() {
    if (g_pendingDraws == 0) return;
    g_trace += " D" + std::to_string(g_pendingDraws);
    g_pendingDraws = 0;
}

void traceEvent(const char* fmt, ...) __attribute__((format(printf, 1, 2)));
void traceEvent(const char* fmt, ...) {
    if (!g_tracing || g_trace.size() > 12000) return;
    flushDraws();
    char buf[96];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buf, sizeof buf, fmt, args);
    va_end(args);
    g_trace += ' ';
    g_trace += buf;
}

void logTrace() {
    flushDraws();
    BL_INFO("rastro GL: %zu caracteres", g_trace.size());
    for (size_t i = 0; i < g_trace.size(); i += 700) {
        BL_INFO("rastro GL [%zu]:%s", i / 700, g_trace.substr(i, 700).c_str());
    }
}

// O blit do marcador (RenderHook.cpp) chegou: a camera do mundo acabou e a
// da interface ainda nao comecou.
void onMarker() {
    traceEvent("MARCADOR");
    noteMarkerHit();
    EffectFrame effects;
    if (!takeWorldEffects(effects) || g_lastDraw.viewport[2] <= 0) return;
    const GlShadow state = unityState();
    t_inside = true;
    EffectRenderer::Execute(effects, g_lastDraw, state);
    t_inside = false;
}

void hkBindFramebuffer(GLenum target, GLuint fbo) {
    if (tracked()) {
        if (target != GL_READ_FRAMEBUFFER) g_shadow.drawFbo = fbo;
        if (target != GL_DRAW_FRAMEBUFFER) g_shadow.readFbo = fbo;
        // v49: o ultimo alvo NAO-tela e' onde o mundo foi desenhado.
        if (fbo != 0) g_lastOffscreenFbo = fbo;
        traceEvent("FB%s(%u)", target == GL_READ_FRAMEBUFFER ? "r" : target == GL_DRAW_FRAMEBUFFER ? "d" : "", fbo);
    }
    g_origBindFramebuffer(target, fbo);
}

void hkViewport(GLint x, GLint y, GLsizei w, GLsizei h) {
    if (tracked()) {
        traceEvent("VP(%d,%d,%d,%d)", x, y, w, h);
        if (w == kMarkerWidth && h == kMarkerHeight) onMarker();
        g_shadow.viewport[0] = x;
        g_shadow.viewport[1] = y;
        g_shadow.viewport[2] = w;
        g_shadow.viewport[3] = h;
    }
    g_origViewport(x, y, w, h);
}

void hkUseProgram(GLuint program) {
    if (tracked()) {
        g_shadow.program = program;
        // v48: o rastro precisa saber SOB QUAL programa cada bloco de draws
        // saiu; sem isso "112 draws" nao diz nada sobre o estado.
        traceEvent("PG(%u)", program);
    }
    g_origUseProgram(program);
}

void hkScissor(GLint x, GLint y, GLsizei w, GLsizei h) {
    if (tracked()) traceEvent("SCO(%d,%d,%d,%d)", x, y, w, h);
    g_origScissor(x, y, w, h);
}

void hkBlendFunc(GLenum src, GLenum dst) {
    if (tracked()) traceEvent("BF(%d,%d)", (int)src, (int)dst);
    g_origBlendFunc(src, dst);
}

void hkBlendFuncSeparate(GLenum srgb, GLenum drgb, GLenum sa, GLenum da) {
    if (tracked()) traceEvent("BFS(%d,%d,%d,%d)", (int)srgb, (int)drgb, (int)sa, (int)da);
    g_origBlendFuncSeparate(srgb, drgb, sa, da);
}

void hkClearColor(GLfloat r, GLfloat g, GLfloat b, GLfloat a) {
    if (tracked()) traceEvent("CC(%.2f,%.2f,%.2f,%.2f)", r, g, b, a);
    g_origClearColor(r, g, b, a);
}

void hkDepthMask(GLboolean flag) {
    if (tracked()) traceEvent("DM(%d)", (int)flag);
    g_origDepthMask(flag);
}

void hkDepthFunc(GLenum func) {
    if (tracked()) traceEvent("DF(%d)", (int)func);
    g_origDepthFunc(func);
}

void hkBlendEquation(GLenum mode) {
    if (tracked()) traceEvent("BE(%d)", (int)mode);
    g_origBlendEquation(mode);
}

void hkBindTexture(GLenum target, GLuint texture) {
    if (tracked()) {
        int unit = static_cast<int>(g_shadow.activeTexture) - static_cast<int>(GL_TEXTURE0);
        traceEvent("TX(%d,%u)", unit, static_cast<unsigned>(texture));
    }
    g_origBindTexture(target, texture);
}

// ---- v49: leitura de pixels ----------------------------------------------
//
// O rastro provou que os dois casos (com e sem mod) emitem a MESMA sequencia
// de draws/programas (395 vs 379 draws, mesma ordem). Entao o problema nao e'
// "nao desenha": e' o que sai no pixel. Aqui, no quadro rastreado, lemos o
// framebuffer de destino (o offscreen onde o mundo foi desenhado) e o
// framebuffer 0 (a tela) e imprimimos uma miniatura ASCII + a cor media.
// - desenho presente no offscreen e ausente na tela = falha na composicao;
// - ausente nos dois = o desenho nao pinta (estado/textura).
// v49: tamanho de verdade da textura presa em GL_COLOR_ATTACHMENT0 do fbo.
// Ler com glReadPixels exige a largura/altura exatas: pedir mais que o real
// volta lixo, pedir menos mostra so' um canto.
// (glGetTexLevelParameteriv e' ES 3.1 e o header do NDK e' ES 3.0; entao
//  perguntamos o tamanho pelo GL_VIEWPORT corrente, que o jogo deixa no
//  tamanho do alvo enquanto desenha.)
bool attachmentSize(GLuint fbo, int* w, int* h) {
    (void)fbo;
    GLint vp[4] = {0, 0, 0, 0};
    gl().GetIntegerv(GL_VIEWPORT, vp);
    if (vp[2] <= 0 || vp[3] <= 0) return false;
    *w = vp[2];
    *h = vp[3];
    return true;
}

void dumpFramebuffer(const char* tag, GLuint fbo, int width, int height) {
    if (width <= 0 || height <= 0) {
        BL_INFO("pixel %s: tamanho invalido (%dx%d), nada lido", tag, width, height);
        return;
    }
    size_t bytes = static_cast<size_t>(width) * static_cast<size_t>(height) * 4;
    unsigned char* pixels = static_cast<unsigned char*>(std::malloc(bytes));
    if (!pixels) {
        BL_INFO("pixel %s: sem memoria para %zu bytes", tag, bytes);
        return;
    }

    GLint previous = 0;
    gl().GetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &previous);
    g_origBindFramebuffer(GL_READ_FRAMEBUFFER, fbo);
    gl().ReadPixels(0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, pixels);
    g_origBindFramebuffer(GL_READ_FRAMEBUFFER, static_cast<GLuint>(previous));

    const int cols = 48, rows = 16;
    const char* ramp = " .:-=+*#%@";
    int rampLen = 10;
    std::string art;
    double sumR = 0, sumG = 0, sumB = 0;
    long samples = 0;
    for (int row = 0; row < rows; ++row) {
        // GL le de baixo para cima; invertemos para a miniatura ficar "de pe".
        int y = height - 1 - (row * height + height / 2) / rows;
        if (y < 0) y = 0;
        if (y >= height) y = height - 1;
        for (int col = 0; col < cols; ++col) {
            int x = (col * width + width / 2) / cols;
            if (x < 0) x = 0;
            if (x >= width) x = width - 1;
            const unsigned char* p = pixels + (static_cast<size_t>(y) * width + x) * 4;
            double luma = (0.30 * p[0] + 0.59 * p[1] + 0.11 * p[2]) / 255.0;
            art.push_back(ramp[static_cast<int>(luma * (rampLen - 1) + 0.5)]);
            sumR += p[0]; sumG += p[1]; sumB += p[2];
            samples++;
        }
        art.push_back('|');
    }
    std::free(pixels);
    if (samples == 0) return;
    BL_INFO("pixel %s fbo=%u %dx%d media=%.0f,%.0f,%.0f", tag, static_cast<unsigned>(fbo), width, height,
            sumR / samples, sumG / samples, sumB / samples);
    for (int row = 0; row < rows; ++row)
        BL_INFO("pixel %s [%2d] %s", tag, row, art.substr(static_cast<size_t>(row) * (cols + 1), cols + 1).c_str());
}

void hkBindVertexArray(GLuint vao) {
    if (tracked()) g_shadow.vertexArray = vao;
    g_origBindVertexArray(vao);
}

void hkActiveTexture(GLenum unit) {
    if (tracked()) g_shadow.activeTexture = unit;
    g_origActiveTexture(unit);
}

bool* capSlot(GLenum cap) {
    switch (cap) {
        case GL_BLEND: return &g_shadow.blend;
        case GL_DEPTH_TEST: return &g_shadow.depth;
        case GL_STENCIL_TEST: return &g_shadow.stencil;
        case GL_CULL_FACE: return &g_shadow.cull;
        case GL_SCISSOR_TEST: return &g_shadow.scissor;
        default: return nullptr;
    }
}

void hkEnable(GLenum cap) {
    if (tracked()) {
        if (bool* slot = capSlot(cap)) *slot = true;
        traceEvent("EN(%d)", (int)cap);
    }
    g_origEnable(cap);
}

void hkDisable(GLenum cap) {
    if (tracked()) {
        if (bool* slot = capSlot(cap)) *slot = false;
        traceEvent("DIS(%d)", (int)cap);
    }
    g_origDisable(cap);
}

void hkColorMask(GLboolean r, GLboolean gr, GLboolean b, GLboolean a) {
    if (tracked()) {
        g_shadow.colorMask[0] = r;
        g_shadow.colorMask[1] = gr;
        g_shadow.colorMask[2] = b;
        g_shadow.colorMask[3] = a;
        traceEvent("CM(%d%d%d%d)", (int)r, (int)gr, (int)b, (int)a);
    }
    g_origColorMask(r, gr, b, a);
}

void hkClear(GLbitfield mask) {
    if (tracked()) {
        traceEvent("CL(%s%s%s)", mask & GL_COLOR_BUFFER_BIT ? "c" : "", mask & GL_DEPTH_BUFFER_BIT ? "d" : "",
                   mask & GL_STENCIL_BUFFER_BIT ? "s" : "");
    }
    g_origClear(mask);
}

// v51: leitura de pixels NO MEIO do quadro, a cada N desenhos. O rastro provou
// que os dois casos emitem quase a mesma sequencia (a diferenca e' 4 desenhos
// no programa 7, bem no comeco); a miniatura no fim do quadro so' mostra branco
// por igual. Lendo a cada N desenhos da' para ver em QUE desenho o quadro
// vira branco, e o programa esta no rotulo.
std::atomic<bool> g_midDump{false};
int g_midFrames = 0;
int g_frameDraws = 0;
const int kMidGates[] = {6, 12, 20, 26, 30, 34, 38, 44, 60, 90};

void midDumpMaybe() {
    if (!g_midDump) return;
    ++g_frameDraws;
    bool gate = false;
    for (int g : kMidGates) if (g == g_frameDraws) gate = true;
    if (!gate) return;
    GLint vp[4] = {0, 0, 0, 0};
    gl().GetIntegerv(GL_VIEWPORT, vp);
    GLint fbo = 0;
    gl().GetIntegerv(GL_FRAMEBUFFER_BINDING, &fbo);
    char tag[40];
    snprintf(tag, sizeof tag, "meio%d-p%u", g_frameDraws, static_cast<unsigned>(g_shadow.program));
    dumpFramebuffer(tag, static_cast<GLuint>(fbo), vp[2], vp[3]);
}

void hkDrawElements(GLenum mode, GLsizei count, GLenum type, const void* indices) {
    if (tracked()) {
        noteDraw();
        if (g_tracing) g_pendingDraws++;
    }
    g_origDrawElements(mode, count, type, indices);
    if (tracked()) midDumpMaybe();
}

void hkDrawArrays(GLenum mode, GLint first, GLsizei count) {
    if (tracked()) {
        noteDraw();
        if (g_tracing) g_pendingDraws++;
    }
    g_origDrawArrays(mode, first, count);
    if (tracked()) midDumpMaybe();
}

void hkBlit(GLint sx0, GLint sy0, GLint sx1, GLint sy1, GLint dx0, GLint dy0, GLint dx1, GLint dy1,
            GLbitfield mask, GLenum filter) {
    if (tracked()) traceEvent("BL(%d,%d,%d,%d->%d,%d,%d,%d)", sx0, sy0, sx1, sy1, dx0, dy0, dx1, dy1);
    g_origBlit(sx0, sy0, sx1, sy1, dx0, dy0, dx1, dy1, mask, filter);
}

int g_frame = 0;
bool g_programsDumped = false;

// v52: quem e' o programa N. Guardamos o texto do glShaderSource e, no
// glLinkProgram, imprimimos o programa e os shaders presos a ele. Sem isso
// "programa 8 pinta o quadro de branco" nao diz de quem e' o programa (do jogo
// ou um que nos mesmos compilamos).
std::mutex g_shaderGate;
std::map<GLuint, std::string> g_shaderSrc;
int g_linkLogged = 0;

void hkShaderSource(GLuint shader, GLsizei count, const GLchar* const* strings, const GLint* lengths) {
    if (count > 0 && strings) {
        std::string src;
        for (GLsizei i = 0; i < count && src.size() < 260; ++i) {
            if (!strings[i]) continue;
            GLint len = lengths ? lengths[i] : -1;
            src.append(strings[i], len > 0 ? static_cast<size_t>(len) : std::strlen(strings[i]));
            if (src.size() > 260) src.resize(260);
        }
        std::lock_guard<std::mutex> lock(g_shaderGate);
        if (g_shaderSrc.size() < 128) g_shaderSrc[shader] = src;
    }
    g_origShaderSource(shader, count, strings, lengths);
}

void hkLinkProgram(GLuint program) {
    g_origLinkProgram(program);
    if (g_linkLogged >= 200) return;
    GLint ok = 0;
    gl().GetProgramiv(program, GL_LINK_STATUS, &ok);
    GLuint ids[4] = {0, 0, 0, 0};
    GLsizei n = 0;
    if (gl().GetAttachedShaders) gl().GetAttachedShaders(program, 4, &n, ids);
    std::string joined;
    for (GLsizei i = 0; i < n; ++i) {
        std::lock_guard<std::mutex> lock(g_shaderGate);
        auto it = g_shaderSrc.find(ids[i]);
        if (it == g_shaderSrc.end()) continue;
        std::string one = it->second;
        for (char& c : one) if (c == '\n' || c == '\r') c = ' ';
        joined += " || " + one;
    }
    ++g_linkLogged;
    BL_INFO("[gl] link programa %u ok=%d n=%d:%s", static_cast<unsigned>(program), (int)ok, (int)n, joined.c_str());
}

// v53: os programas da Unity foram ligados ANTES de os nossos hooks existirem
// (o glLinkProgram so' e' hookado no primeiro pedido de rastro, no quadro 12),
// entao o v52 nao viu nenhum link. Aqui perguntamos na hora: para cada id,
// quem sao os shaders presos e o comeco do texto deles. Assim da' para ver
// se o programa que pinta o quadro de branco e' do jogo ou um que nos
// compilamos.
void dumpPrograms() {
    if (!gl().GetAttachedShaders || !gl().GetShaderSource) {
        BL_INFO("[gl] sem GetAttachedShaders/GetShaderSource, nao da' para listar programas");
        return;
    }
    for (GLuint p = 1; p <= 48; ++p) {
        GLuint ids[4] = {0, 0, 0, 0};
        GLsizei n = 0;
        gl().GetAttachedShaders(p, 4, &n, ids);
        if (n <= 0) continue;
        std::string joined;
        for (GLsizei i = 0; i < n; ++i) {
            GLint type = 0;
            gl().GetShaderiv(ids[i], GL_SHADER_TYPE, &type);
            char buf[280];
            buf[0] = 0;
            gl().GetShaderSource(ids[i], sizeof buf, nullptr, buf);
            std::string one(buf);
            for (char& c : one) if (c == '\n' || c == '\r' || c == '\t') c = ' ';
            if (!joined.empty()) joined += " || ";
            joined += (type == GL_VERTEX_SHADER ? "VS " : "FS ") + one;
        }
        BL_INFO("[gl] programa %u: %s", static_cast<unsigned>(p), joined.c_str());
    }
    while (gl().GetError() != GL_NO_ERROR) {
    }
}

EGLBoolean hkSwap(EGLDisplay display, EGLSurface surface) {
    if (!unityThread()) return g_origSwap(display, surface);

    if (g_tracing) {
        traceEvent("SWAP");
        g_tracing = false;
        g_midDump.store(false);
        logTrace();
        g_trace.clear();
    } else if (g_traceRequested.exchange(false)) {
        g_tracing = true;
        g_trace = "SWAP";
        g_frameDraws = 0;
        if (!g_programsDumped) {
            g_programsDumped = true;
            dumpPrograms();
        }
        // So' no primeiro quadro rastreado: 10 leituras de 2400x1080 travam o
        // quadro, e a pergunta ("em que desenho vira branco") uma vez basta.
        g_midDump.store(g_midFrames < 1);
        if (g_midFrames < 1) g_midFrames++;
    }

    // A camada Screen (e os do mundo, quando nao ha marcador): no fim do
    // quadro, por cima da interface.
    EffectFrame effects;
    if (takeScreenEffects(effects)) {
        const GlShadow state = unityState();
        DrawTarget target;
        target.fbo = static_cast<int>(state.drawFbo);
        for (int i = 0; i < 4; ++i) target.viewport[i] = state.viewport[i];
        t_inside = true;
        EffectRenderer::Execute(effects, target, state);
        t_inside = false;
    }

    // O quadro inteiro na GPU, de um swap ao outro, em quadros alternados (os
    // outros medem o efeito sozinho; consultas de tempo nao se aninham).
    GpuTimer::End(GpuChannel::Frame);

    // v49: miniatura ASCII do offscreen (onde o mundo foi desenhado) e da tela,
    // no quadro rastreado. Antes do swap para a tela ainda ter o conteudo final.
    if (g_dumpPending.exchange(false) && g_dumpCount < 2) {
        g_dumpCount++;
        int w = g_lastDraw.viewport[2];
        int h = g_lastDraw.viewport[3];
        GLint vp[4] = {0, 0, 0, 0};
        gl().GetIntegerv(GL_VIEWPORT, vp);

        if (w <= 0 || h <= 0) { w = vp[2]; h = vp[3]; }

        // O offscreen do mundo: o ultimo FBO nao-tela que o jogo prendeu.
        GLuint off = g_lastOffscreenFbo;
        int offW = w, offH = h;
        const bool haveSize = attachmentSize(off, &offW, &offH);
        GLint err = gl().GetError();
        BL_INFO("pixel: leitura pedida (offscreen fbo=%u %dx%d vp=%d,%d,%d,%d ultimoDraw=%d,%d,%d,%d errAntes=%d)",
                static_cast<unsigned>(off), offW, offH, vp[0], vp[1], vp[2], vp[3],
                g_lastDraw.viewport[0], g_lastDraw.viewport[1], g_lastDraw.viewport[2], g_lastDraw.viewport[3], (int)err);
        dumpFramebuffer("offscreen", off, offW, offH);
        dumpFramebuffer("tela", 0, vp[2] > 0 ? vp[2] : w, vp[3] > 0 ? vp[3] : h);
        BL_INFO("pixel: leitura terminada errDepois=%d", (int)gl().GetError());
    }

    const EGLBoolean result = g_origSwap(display, surface);
    if (++g_frame % 2 == 0) GpuTimer::Begin(GpuChannel::Frame);
    return result;
}

template <typename Fn>
bool hookGl(const char* name, void* address, Fn replacement, Fn* original) {
    if (address && hook::install(address, reinterpret_cast<void*>(replacement), reinterpret_cast<void**>(original))) {
        return true;
    }
    BL_ERROR("efeitos de tela: %s nao hookado", name);
    return false;
}

} // namespace

void installGlHooks() {
    static std::once_flag once;
    std::call_once(once, [] {
        auto& g = gl();
        if (!g.load()) return;
        void* egl = dlopen("libEGL.so", RTLD_NOW);
        void* swap = egl ? dlsym(egl, "eglSwapBuffers") : nullptr;

        bool ok = hookGl("eglSwapBuffers", swap, hkSwap, &g_origSwap);
        ok &= hookGl("glBindFramebuffer", reinterpret_cast<void*>(g.BindFramebuffer), hkBindFramebuffer, &g_origBindFramebuffer);
        ok &= hookGl("glViewport", reinterpret_cast<void*>(g.Viewport), hkViewport, &g_origViewport);
        ok &= hookGl("glUseProgram", reinterpret_cast<void*>(g.UseProgram), hkUseProgram, &g_origUseProgram);
        ok &= hookGl("glBindVertexArray", reinterpret_cast<void*>(g.BindVertexArray), hkBindVertexArray, &g_origBindVertexArray);
        ok &= hookGl("glActiveTexture", reinterpret_cast<void*>(g.ActiveTexture), hkActiveTexture, &g_origActiveTexture);
        ok &= hookGl("glEnable", reinterpret_cast<void*>(g.Enable), hkEnable, &g_origEnable);
        ok &= hookGl("glDisable", reinterpret_cast<void*>(g.Disable), hkDisable, &g_origDisable);
        ok &= hookGl("glColorMask", reinterpret_cast<void*>(g.ColorMask), hkColorMask, &g_origColorMask);
        ok &= hookGl("glClear", reinterpret_cast<void*>(g.Clear), hkClear, &g_origClear);
        ok &= hookGl("glDrawElements", reinterpret_cast<void*>(g.DrawElements), hkDrawElements, &g_origDrawElements);
        ok &= hookGl("glDrawArrays", reinterpret_cast<void*>(g.DrawArrays), hkDrawArrays, &g_origDrawArrays);
        ok &= hookGl("glBlitFramebuffer", reinterpret_cast<void*>(g.BlitFramebuffer), hkBlit, &g_origBlit);
        ok &= hookGl("glScissor", reinterpret_cast<void*>(g.Scissor), hkScissor, &g_origScissor);
        ok &= hookGl("glBlendFunc", reinterpret_cast<void*>(g.BlendFunc), hkBlendFunc, &g_origBlendFunc);
        ok &= hookGl("glBlendFuncSeparate", reinterpret_cast<void*>(g.BlendFuncSeparate), hkBlendFuncSeparate, &g_origBlendFuncSeparate);
        ok &= hookGl("glClearColor", reinterpret_cast<void*>(g.ClearColor), hkClearColor, &g_origClearColor);
        ok &= hookGl("glDepthMask", reinterpret_cast<void*>(g.DepthMask), hkDepthMask, &g_origDepthMask);
        ok &= hookGl("glDepthFunc", reinterpret_cast<void*>(g.DepthFunc), hkDepthFunc, &g_origDepthFunc);
        ok &= hookGl("glBlendEquation", reinterpret_cast<void*>(g.BlendEquation), hkBlendEquation, &g_origBlendEquation);
        ok &= hookGl("glBindTexture", reinterpret_cast<void*>(g.BindTexture), hkBindTexture, &g_origBindTexture);
        ok &= hookGl("glShaderSource", reinterpret_cast<void*>(g.ShaderSource), hkShaderSource, &g_origShaderSource);
        ok &= hookGl("glLinkProgram", reinterpret_cast<void*>(g.LinkProgram), hkLinkProgram, &g_origLinkProgram);
        BL_INFO("efeitos de tela: hooks GL %s", ok ? "ok" : "com falha");
    });
}

void requestGlTrace() {
    installGlHooks();
    g_traceRequested.store(true);
    // v49: junto com o rastro, a leitura de pixels do mesmo quadro.
    g_dumpPending.store(true);
}

void fillGlHookStats(RenderStats& stats) {
    stats.shadowChecks = g_shadowChecks.load();
    stats.shadowMismatches = g_shadowMismatches.load();
}

} // namespace bl::graphics
