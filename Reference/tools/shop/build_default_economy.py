# build_default_economy.py
# The "Default Economy" (the macOS / Windows / iPhone item and ship tables) as an overlay on the remake's data, which came
# from the Android OBB ("Android Economy"). KiritoJPK's GOF2_2.0.16_Default_Economy.apk (release GOF2FHD09282026) differs from
# the Android_Economy APK only by assets/data/bin/items.bin and ships.bin, which the game loads instead of the OBB's; they are
# kept in Reference/binaries/default_economy/. Its ships.bin was replaced on 2026-10-30 by the iOS 1.1.19 IPA's (identical
# layout; the APK's had 12 prices at the in-game 0.99 race discount, e.g. Betty 16038 for 16200), its items.bin is byte-identical.
#
#   items.bin (FileRead::loadItemsBinary 0x145f48): 233 x (int[] ingredients, int[] quantities, int[] attribute pairs),
#             big-endian ints, a count before each array
#   ships.bin: 64 x 9 ints (index, armor, cargo, price, primary, secondary, turret, equipment, handling)
#
# Writes Assets/Resources/GoF2Data/economy_default.json: every item / ship whose values differ from items.json /
# item_attributes.json / ships.json, with its complete Default Economy values (Database.ApplyDefaultEconomy).
#   python Reference/tools/shop/build_default_economy.py          (writes the file, prints a summary)
import json, os, struct

here = os.path.dirname(os.path.abspath(__file__))
root = os.path.join(here, '..', '..', '..')
BIN = os.path.join(root, 'Reference', 'binaries', 'default_economy')
D = os.path.join(root, 'Assets', 'Resources', 'GoF2Data')

# The stat names of Reference/tools/export.py (items.json's statList: every attribute from 9 on).
ATTR = {9: 'damage', 10: 'empDamage', 11: 'loadingTimeMs', 12: 'range', 13: 'projectileSpeed', 14: 'magnitude', 15: 'steerable',
        16: 'automatic', 17: 'handling', 18: 'shieldCapacity', 19: 'shieldRegenTime', 20: 'armor', 21: 'empDefense', 22: 'cargoBonus',
        23: 'automatic_2', 24: 'timeToLock', 25: 'boostSpeed', 26: 'boostRechargeMs', 27: 'boostDurationMs', 28: 'agility',
        29: 'timeToLock_2', 30: 'showClassAAsteroids', 31: 'radarShowsCargo', 32: 'handling_2', 33: 'miningYield', 34: 'cabinSize',
        35: 'effect_35', 36: 'loadingSpeed_36', 37: 'loadingSpeed_37', 38: 'energyConsumption', 39: 'fireRateFactor',
        40: 'damageFactor', 41: 'effect_41', 42: 'effect_42', 43: 'loadingSpeed_43', 44: 'range_44', 45: 'showOnRadar',
        46: 'effect_46', 47: 'range_47', 48: 'cabinSize_48', 49: 'speed_49', 50: 'magnitude_50', 51: 'range_51',
        52: 'gammaShielding', 53: 'range_53', 54: 'effect_54', 55: 'count', 56: 'effect_56', 57: 'showInfo', 58: 'showOnRadar_58',
        59: 'plasmaConsumption', 60: 'raceSpecific_guess', 61: 'iconIndex_guess'}


class R:
    def __init__(s, p): s.b = open(p, 'rb').read(); s.o = 0
    def i(s): v = struct.unpack('>i', s.b[s.o:s.o + 4])[0]; s.o += 4; return v
    def arr(s): n = s.i(); return [s.i() for _ in range(n)] if n > 0 else []
    def left(s): return len(s.b) - s.o


def load(name): return json.load(open(os.path.join(D, name), encoding='utf-8'))


r = R(os.path.join(BIN, 'items.bin'))
pc_items = [dict(ing=r.arr(), qty=r.arr(), attrs=r.arr()) for _ in range(233)]
assert r.left() == 0, 'items.bin: bytes left'
r = R(os.path.join(BIN, 'ships.bin'))
pc_ships = [[r.i() for _ in range(9)] for _ in range(64)]
assert r.left() == 0, 'ships.bin: bytes left'

items = load('items.json')
attrs = {a['index']: a for a in load('item_attributes.json')}
ships = load('ships.json')
ships = ships if isinstance(ships, list) else ships['ships']

out_items, out_ships = [], []
for it in items:
    k = it['index']
    pc = pc_items[k]
    keys, values = pc['attrs'][0::2], pc['attrs'][1::2]
    a = dict(zip(keys, values))
    bp = [dict(item=i, name=items[i]['name'], amount=q) for i, q in zip(pc['ing'], pc['qty'])]
    old = attrs.get(k, {})
    old_bp = [(b['item'], b['amount']) for b in (it.get('blueprint') or [])]
    if list(old.get('keys', [])) == keys and list(old.get('values', [])) == values and old_bp == list(zip(pc['ing'], pc['qty'])):
        continue
    out_items.append(dict(index=k, techLevel=a.get(3, 0), occurrence=a.get(6, 0), minPrice=a.get(7, 0), maxPrice=a.get(8, 0),
                          lowestPriceSystem=a.get(4, 0), highestPriceSystem=a.get(5, 0), keys=keys, values=values,
                          statList=[dict(key=ATTR.get(x, f'attr_{x}'), value=v) for x, v in a.items() if x >= 9],
                          blueprint=bp))
for s in ships:
    p = pc_ships[s['index']]
    cur = [s['index'], s['armor'], s['cargo'], s['price'], s['slots']['primary'], s['slots']['secondary'], s['slots']['turret'],
           s['slots']['equipment'], s['handling']]
    if cur == p: continue
    out_ships.append(dict(index=p[0], armor=p[1], cargo=p[2], price=p[3], primary=p[4], secondary=p[5], turret=p[6],
                          equipment=p[7], handling=p[8]))

json.dump(dict(items=out_items, ships=out_ships), open(os.path.join(D, 'economy_default.json'), 'w', encoding='utf-8', newline='\n'),
          ensure_ascii=False, indent=1)
print(f'{len(out_items)} items, {len(out_ships)} ships differ -> economy_default.json')
