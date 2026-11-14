#pragma once

// Comparacao de assinaturas de codigo ARM64 usada pelos hooks de NPC.
//
// As assinaturas sao tiradas de UMA build do jogo. Qualquer instrucao com
// deslocamento relativo (BL/B/B.cond/CBZ/ADRP/LDR literal) muda quando a
// funcao muda de lugar — comparar os 32 bits crus reprova o gancho mesmo com
// o site IDENTICO. Os campos de imediato relativo viram curinga.

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace bl::npccode {

inline bool sameInstruction(uint32_t actual, uint32_t expected) {
    if (actual == expected) return true;
    struct Reloc { uint32_t mask, value; };
    static constexpr Reloc kReloc[] = {
        {0xFC000000u, 0x94000000u},  // BL
        {0xFC000000u, 0x14000000u},  // B
        {0xFF00001Fu, 0x54000000u},  // B.cond
        {0x7F00001Fu, 0x34000000u},  // CBZ
        {0x7F00001Fu, 0x35000000u},  // CBNZ
        {0x9F00001Fu, 0x90000000u},  // ADRP
        {0x9F00001Fu, 0x10000000u},  // ADR
        {0xFF00001Fu, 0x58000000u},  // LDR (literal, 64)
        {0xFF00001Fu, 0x18000000u},  // LDR (literal, 32)
    };
    for (const Reloc& r : kReloc)
        if ((expected & r.mask) == r.value) return (actual & r.mask) == r.value;
    return false;
}

inline bool sameBlock(const uint8_t* code, size_t offset, const uint32_t* expected, size_t count) {
    uint32_t actual[8]{};
    if (count > 8) return false;
    std::memcpy(actual, code + offset, count * sizeof(uint32_t));
    for (size_t i = 0; i < count; i++)
        if (!sameInstruction(actual[i], expected[i])) return false;
    return true;
}

inline bool sameBlock(const uint8_t* code, size_t offset, const std::array<uint32_t, 4>& expected) {
    return sameBlock(code, offset, expected.data(), expected.size());
}

}
