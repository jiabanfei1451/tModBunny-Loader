#pragma once
#include "graphics/RenderHook.h"
#include <string>

namespace bl::graphics {

/**
 * Hooks na libEGL/libGLESv2, na thread de render da Unity (UnityGfxDeviceW).
 *
 * Esta libunity nao tem mais GL.IssuePluginEvent nem CommandBuffer (o build
 * removeu as icalls que o jogo nao usa), entao o ponto do quadro tem de ser
 * achado nas proprias chamadas GL. Por enquanto: o eglSwapBuffers (fim do
 * quadro, onde a prova de conceito desenha) e um rastro de um quadro inteiro
 * (framebuffers, viewports, clears, desenhos) para achar a fronteira entre a
 * camera do mundo e a da interface.
 */
void installGlHooks();   // uma vez so; as chamadas seguintes nao fazem nada

// Grava o proximo quadro inteiro; o resultado sai no log ("rastro GL: ...").
void requestGlTrace();

// Os contadores do espelho do estado GL (GlShadow.h).
void fillGlHookStats(RenderStats& stats);

// v55: estagio de vertice dos shaders por-vertice de mod. `primBuild` liga
// um programa (VS do tradutor + FS do mod) com os atributos nos mesmos slots
// do programa do jogo ligado agora; `primBind` arma a substituicao para esse
// programa (com a matriz de mundo); `primRelease` desarma.
int32_t primBuild(const char* vs, const char* fs, char* out, int32_t capacity);
int32_t primBind(int32_t program, const float* mvp);
void primRelease();

} // namespace bl::graphics
