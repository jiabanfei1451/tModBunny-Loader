cmake_minimum_required(VERSION 3.22)
project(bunny CXX C ASM)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)

# --- ShadowHook (via prefab, vem do Gradle) ---
find_package(shadowhook REQUIRED CONFIG)

# Nossos fontes C++ (recursivo).
file(GLOB_RECURSE BL_SOURCES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/*.cpp)
list(FILTER BL_SOURCES EXCLUDE REGEX "/compile/")

# Upstream 0.5.13: FxcEffect/SpriteFx (MojoShader) e JitRecorder (AOT) ficam
# guardados na arvore mas fora do build: dependem de entradas do NativeApi
# desta engine que ainda nao existem (SpriteFx*, fxc). Ligar quando a porta
# desses recursos for feita.
list(FILTER BL_SOURCES EXCLUDE REGEX "/(SpriteFx|JitRecorder)\\.cpp$")

# MojoShader (terceiro do upstream): HLSL/.fxc -> GLSL ES 3. O FxcEffect.cpp
# usa. Montado como o upstream monta (lib estatica, C99, sem o header de
# versao que o build oficial gera).
set(MOJOSHADER_DIR ${CMAKE_CURRENT_SOURCE_DIR}/third_party/mojoshader)
add_library(mojoshader STATIC
    ${MOJOSHADER_DIR}/mojoshader.c
    ${MOJOSHADER_DIR}/mojoshader_common.c
    ${MOJOSHADER_DIR}/mojoshader_effects.c
    ${MOJOSHADER_DIR}/profiles/mojoshader_profile_common.c
    ${MOJOSHADER_DIR}/profiles/mojoshader_profile_glsl.c)
target_include_directories(mojoshader PUBLIC ${MOJOSHADER_DIR})
target_compile_definitions(mojoshader PUBLIC
    MOJOSHADER_NO_VERSION_INCLUDE
    MOJOSHADER_EFFECT_SUPPORT
    # Mesma lista do upstream: dosagem dos perfis que o mojoshader versionado
    # aqui nao traz (D3D/HLSL/BYTECODE/ARB1/METAL/SPIRV). Sobra GLSL (GLSLES3),
    # que e' o perfil usado pelo FxcEffect.cpp.
    SUPPORT_PROFILE_D3D=0
    SUPPORT_PROFILE_BYTECODE=0
    SUPPORT_PROFILE_HLSL=0
    SUPPORT_PROFILE_ARB1=0
    SUPPORT_PROFILE_ARB1_NV=0
    SUPPORT_PROFILE_METAL=0
    SUPPORT_PROFILE_SPIRV=0
    SUPPORT_PROFILE_GLSPIRV=0)
set_target_properties(mojoshader PROPERTIES C_STANDARD 99 C_VISIBILITY_PRESET hidden POSITION_INDEPENDENT_CODE ON)
target_compile_definitions(mojoshader PRIVATE NDEBUG)
target_compile_options(mojoshader PRIVATE -O2 -w)

add_library(blcompile SHARED compile/CompilerHost.cpp)
target_link_libraries(blcompile log dl)

# O trampolim da chamada guardada (il2cpp/GuardedCall.h), em assembly.
file(GLOB_RECURSE BL_ASM_SOURCES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/*.S)

add_library(bunny SHARED ${BL_SOURCES} ${BL_ASM_SOURCES})

target_include_directories(bunny PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_BINARY_DIR}/gen)

target_compile_options(bunny PRIVATE -O2
    $<$<COMPILE_LANGUAGE:CXX>:-fno-exceptions>
    $<$<COMPILE_LANGUAGE:CXX>:-fno-rtti>)

# Sem -fexceptions em arquivo nenhum. Uma excecao do jogo vem do __cxa_throw
# do libil2cpp, com o desenrolador do libgcc; um `catch` daqui, ou um frame
# daqui com destrutor pendente, usaria a personalidade da libbunny, que le o
# contexto como o do LLVM libunwind e derruba o processo. Quem chama codigo do
# jogo e pode ver uma excecao usa o il2cpp::guardedCall (il2cpp/GuardedCall.S),
# que nao depende de desenrolador. Um hook nativo no meio da pilha do jogo (o
# loadHook do TexturePacks.cpp) nao precisa de nada: sem personalidade, o frame
# dele e so atravessado (um destrutor pendente ali fica sem rodar).
target_link_libraries(bunny shadowhook::shadowhook mojoshader log dl z m)
