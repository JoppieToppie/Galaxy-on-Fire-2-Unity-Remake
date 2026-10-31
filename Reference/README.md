# Reference: the original game

Source material for the remake. Nothing in here is used by Unity; it's for reading and searching.

| Path | What |
|---|---|
| `decompiled/native/game/*.c` | Game logic, one file per C++ class (`Level`, `PlayerEgo`, `PlayerFighter`, `Status`, `Generator`, `Ship`, `Item`, …). This is the part to port. |
| `decompiled/native/engine/*.c` | AbyssEngine (rendering, files, sound). Unity replaces it; reference only. |
| `decompiled/native/INDEX.md` | Every class with its function count and code size. |
| `decompiled/native/CLASS_HIERARCHY.txt` | Inheritance tree recovered from the C++ type info (e.g. `PlayerFighter <- KIPlayer`). |
| `decompiled/native/_ALL_FUNCTIONS.c` | All 4,028 functions in one file. Grep it, or use `tools/show.py`. |
| `decompiled/native/_FUNCTIONS.tsv` | Function name, address and size. |
| `decompiled/native/game/BuildResourceList_DISASSEMBLY.txt` | The one function too large to decompile (resource table). |
| `decompiled/native/BuildResourceList_EMULATED_OUTPUT.json` | Its result, obtained by emulation. The cleaned version is `Assets/Resources/GoF2Data/resources.json`. |
| `decompiled/java/` | The Android Java wrapper (activity, input, JNI bridge). |
| `binaries/libgof2hdaa.so` | The original 32-bit ARM game library (for Ghidra or constant lookups). |
| `binaries/GOF2_2.0.16_Android_Price.apk` | The modded APK the code came from. The 1.5 GB OBB is not included; it's on the GitHub release page. |
| `binaries/default_economy/` | `items.bin` from KiritoJPK's `GOF2_2.0.16_Default_Economy.apk` (release GOF2FHD09282026, the only files that APK adds over the Android one) and `ships.bin` from the iOS 1.1.19 IPA's `data/bin/` (byte-identical items; the APK's ships.bin had 12 prices at the in-game 0.99 race discount): the macOS / Windows / iPhone item and ship tables. `tools/shop/build_default_economy.py` turns them into `Assets/Resources/GoF2Data/economy_default.json`. |
| `tools/show.py` | `python Reference/tools/show.py "^PlayerFighter::update$"` prints matching decompiled functions. |
| `tools/rf.py` | `python Reference/tools/rf.py 000a8178` reads a `DAT_` constant as float/hex (needs `pip install pyelftools`). |
| `tools/asset_conversion/` | The scripts that produced `Assets`: `aem.py` (meshes), `aei.py` (textures), `parse_bins.py` (data), `emu_resources.py` (resource table), `aem2fbx.py` (Blender FBX export), `convert_remodel.py` (a remodelled room's new .aem files -> FBX + the current sidecar format), `run_audio.sh` (FMOD banks). |
| `tools/*.java` | Ghidra headless scripts used for the decompile. |

## Reading the pseudocode

- Addresses use Ghidra's image base **0x10000**.
- Fields show up as offsets like `*(float *)(this + 0x27c)`. See `docs/notes/flight-model.md` for the offsets identified so far.
- `DAT_xxxxxxxx` values are constants in the binary; read them with `tools/rf.py`.
- Floating-point arguments are sometimes lost (`extraout_s1`, `in_r1`). Check call sites when something looks missing.
- Game time is in milliseconds (`update(int dt)`).
