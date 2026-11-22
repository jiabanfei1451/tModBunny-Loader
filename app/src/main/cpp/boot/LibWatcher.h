#pragma once
#include <chrono>

namespace bl::loader {

// Registra um hook PENDENTE em il2cpp_init. O ShadowHook aplica sozinho
// quando a libil2cpp.so for carregada pela Unity.
bool installWatcher();
bool watcherInstalled();
bool waitForIl2cppInit(std::chrono::steady_clock::time_point deadline);
void waitForIl2cppInit();

} // namespace bl::loader
