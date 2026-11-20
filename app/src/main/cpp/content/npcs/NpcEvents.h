#pragma once

#include "il2cpp/Types.h"
#include <cstdint>

namespace bl::runtime {

using NpcEventCallback = int32_t (*)(int32_t event, void* npc, void* args);

bool setNpcEvents(int32_t type, uint32_t events, NpcEventCallback callback);
bool npcFrameEvent(Il2CppObject* npc);
// Quantas vezes o detour de NPC.AI ja entrou. Zero com o mundo rodando quer
// dizer que o gancho nunca e chamado: ou o NPC.UpdateNPC foi pulado (Tempo
// Parado) ou a reescrita do comeco de AI() nao pegou.
uint32_t npcAiCallCount();
void runNpcOriginal(Il2CppObject* npc, int32_t event, int32_t argument);
Il2CppObject* npcImmunityData(int32_t type);

}
