from pathlib import Path
import re
import struct

root = Path(__file__).resolve().parents[3]
image = (root / "refs/libil2cpp.so").read_bytes()
# NPC.NPCLoot 0x137D190, NPC.DoDeathEvents_DropBossPotionsAndHearts 0x13828AC (refs/dump.cs)
checks = [
    ("PreKill", 0x137D488, (0xF94002E0, 0xB940E008, 0x35000068, 0x97D10865)),
    ("OnKill", 0x137D4F8, (0xAA1303E0, 0xAA1403E1, 0x94000861, 0xAA1F03E0)),
    ("death return", 0x137D634, (0xA9457BF3, 0xA94453F5, 0xA9435BF7, 0xA94263F9)),
    ("BossLoot", 0x13829B4, (0xAA1303E0, 0x97FFA92C, 0xF9400328, 0x2D42A269)),
    ("heart roll", 0x1382A2C, (0xF9400340, 0xB940E008, 0x35000068, 0x97D0F2FC)),
    ("heart loop", 0x1382A5C, (0x710006FF, 0x5400044B, 0x52AFF018, 0x52B0001A)),
]


def same_word(actual, expected):
    # BL 目标地址随构建变化，按 CodeMatch 的规则只比较 opcode。
    bl = 0x94000000
    return actual == expected or ((actual & 0xFC000000) == bl and (expected & 0xFC000000) == bl)


for name, address, expected in checks:
    actual = struct.unpack_from("<4I", image, address)
    assert all(same_word(a, e) for a, e in zip(actual, expected)), (name, tuple(hex(x) for x in actual))
    print(f"{name}: ok")

native = (root / "app/src/main/cpp/script/dotnet/NativeApi.cpp").read_text(encoding="utf-8")
managed = (root / "app/src/main/csharp/BunnyLoader/Interop/NativeApi.cs").read_text(encoding="utf-8")
body = native.split("struct NativeApi {", 1)[1].split("};", 1)[0]
fields = re.findall(r"\(\*(\w+)\)\([^;]*;", body)
delegates = re.findall(r"public delegate\*[^;]+?\s+(\w+);", managed)
assert len(fields) == len(delegates), (len(fields), len(delegates))
assert [name.lower() for name in fields] == [name.lower() for name in delegates], (fields, delegates)
loot_index = fields.index("newParameterObject")
assert fields[loot_index:loot_index + 2] == ["newParameterObject", "installNpcLootHooks"]
tile_index = fields.index("registerTile")
assert fields[tile_index:tile_index + 7] == ["registerTile", "onTilesInstalled", "tileMapEntry", "tileAnimation", "tileEvents", "scanTiles", "readTileDrawData"]
print(f"NativeApi order: ok ({len(fields)} entries)")
print(f"csnpcloot native END pass={len(checks) + 1} fail=0")
