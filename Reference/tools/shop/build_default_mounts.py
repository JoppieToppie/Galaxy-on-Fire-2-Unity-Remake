# build_default_mounts.py <weapons_hd.bin>
# The iOS 1.1.19 build's weapon mount table differs from the Android OBB's in the type-3 entries (the engine exhaust anchors,
# weapons.md) of four ships: VoidX (9 anchors instead of 11), H'Soc (other offsets / scales), Specter (12 instead of 14),
# Scimitar (11 instead of 13). The Default economy (the iOS / PC tables) takes the iOS anchors: this writes
# Assets/Resources/GoF2Data/weapons_hd_default.json with the complete mount sets of every ship whose set differs
# (Database.ApplyDefaultEconomy replaces those ships' sets). The .bin comes from the IPA's data/bin/ (not in the repo); the
# layout is parse_bins.py's weapons(): per ship int16 index and count, per mount int16 type, x, y, z, type 3 + 3 floats.
import json, os, struct, sys

here = os.path.dirname(os.path.abspath(__file__))
root = os.path.join(here, '..', '..', '..')
D = os.path.join(root, 'Assets', 'Resources', 'GoF2Data')


def weapons(path):
    b = open(path, 'rb').read(); o = 0; out = []
    def sl():
        nonlocal o; v = struct.unpack('<h', b[o:o + 2])[0]; o += 2; return v
    def fl():
        nonlocal o; v = struct.unpack('<f', b[o:o + 4])[0]; o += 4; return v
    while len(b) - o >= 4:
        ship = sl(); n = sl(); ws = []
        for _ in range(n):
            t = sl(); x, y, z = sl(), sl(), sl()
            w = dict(slotType=t, position_file=[x, y, z], position_engine=[x, z, -y])
            if t == 3: w['turretAngles'] = [fl(), fl(), fl()]
            ws.append(w)
        out.append(dict(ship=ship, mounts=ws))
    return out


android = {s['ship']: s for s in json.load(open(os.path.join(D, 'weapons_hd.json'), encoding='utf-8'))}
ios = weapons(sys.argv[1])
out = []
for s in ios:
    a = android.get(s['ship'])
    if a is None: continue
    if json.dumps(a['mounts'], sort_keys=True) == json.dumps(s['mounts'], sort_keys=True): continue
    out.append(dict(ship=s['ship'], shipName=a.get('shipName'), mounts=s['mounts']))
json.dump(out, open(os.path.join(D, 'weapons_hd_default.json'), 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
print(f'{len(out)} ships differ -> weapons_hd_default.json:', [(s["ship"], s["shipName"], len(s["mounts"])) for s in out])
