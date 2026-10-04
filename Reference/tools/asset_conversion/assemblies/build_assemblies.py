# Builds Assets/Resources/GoF2Data/assemblies.json, the input of "GoF2 > Build > Assembled Prefabs".
#
# rules/assemblies_*.json hold the composition rules recovered from the decompiled code (one research
# pass per area; the matching *_notes.md explain functions, addresses and uncertainties):
#   ships     Globals::getShipGroup (+ Level::createShip for explosions)
#   stations  PlayerStation::PlayerStation (+ special station builds)
#   level     Level::createStaticObject / createAsteroids / createSpace, PlayerTurret, Explosion
#   scenes    Level::createScene (hangar, bar), CutScene, StarMap, ListItemWindow (shop preview)
# rules/name_groups.json groups every mesh by name; groups that no code rule covers are assembled from
# their layers (_add/_alpha/_emissive/...) as a fallback, marked "origin": "name-based".
#
# Output schema (see AssemblyBuilder.cs): entries[] {name, pack, category, origin, notes, root,
#   parts[] {key, parent, mesh, model, material, role, variant, condition, position, rotation, scale, lods},
#   lodDistances, lastVisibleDistance, scale, spawnRotation, explosionModel}
# Positions are game units (engine space), rotations euler degrees (engine space).
#
#   python Reference/tools/asset_conversion/assemblies/build_assemblies.py      (from the project root)
import json, math, os, re
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
RULES = os.path.join(HERE, 'rules')
OUT = os.path.join(ROOT, 'Assets/Resources/GoF2Data/assemblies.json')

res = json.load(open(os.path.join(ROOT, 'Assets/Resources/GoF2Data/resources.json')))
MESH = {}
for m in res['meshes']:
    MESH.setdefault(m['id'], m)
# 19080 is registered twice (test_dock and the mining plant); the code always means the mining plant.
MESH[19080] = next(m for m in res['meshes'] if m['id'] == 19080 and 'mining_plant' in m['model'])

def model_of(mid):
    m = MESH.get(mid)
    return m['model'] if m else None

def fbx_exists(model):
    return model and os.path.exists(os.path.join(ROOT, 'Assets', model))

def pack_of(model):
    parts = (model or '').split('/')
    return parts[1] if len(parts) > 2 and parts[1] in ('main', 'supernova', 'valkyrie') else 'main'

def vec3(v):
    if isinstance(v, (int, float)): return [float(v)] * 3
    if isinstance(v, list) and len(v) == 3 and all(isinstance(x, (int, float)) for x in v): return [float(x) for x in v]
    return []

def rotation_deg(v):
    """Rules give rotations as radian lists or as text like 'rotate(0,pi,0)'. Returns engine-space degrees."""
    if isinstance(v, list):
        r = vec3(v)
        return [math.degrees(x) for x in r] if r else []
    if isinstance(v, str):
        m = re.search(r'\(\s*([-\w.]+)\s*,\s*([-\w.]+)\s*,\s*([-\w.]+)\s*\)', v)
        if m:
            def num(s):
                s = s.strip()
                if s in ('pi', 'PI'): return 180.0
                if s in ('-pi', '-PI'): return -180.0
                try: return math.degrees(float(s))
                except ValueError: return 0.0
            return [num(x) for x in m.groups()]
    return []

def slug(name):
    # 'turret_002 (ship-mounted)' -> 'turret_002_ship_mounted'; long explanations in brackets are dropped.
    def bracket(m):
        words = m.group(1)
        return '_' + words if len(words) <= 20 else '_'
    s = re.sub(r'\s*\((.*?)\)\s*', bracket, name)
    return re.sub(r'[^A-Za-z0-9_]+', '_', s).strip('_').lower()

# Parts the game only shows in a certain state although the rules list them as plain children.
STATE_ROLES = [('jump_anim', 'jumpgate activated (replaces the idle anim_add layer)'),
               ('spawned on explode', 'spawned when the object explodes')]

problems = []

def convert(rule, source):
    name = slug(rule['name'])
    lods = [l for l in (rule.get('lods') or []) if isinstance(l.get('mesh'), int) and l['mesh'] > 0]
    lod_managed = bool(lods)
    parts, keys = [], Counter()

    def add(mesh, role, parent='', variant='', condition='', position=None, rotation=None, scale=None, levels=None, key=None):
        model = model_of(mesh) if mesh else None
        if mesh and not fbx_exists(model):
            problems.append(f'{name}: mesh {mesh} ({model or "no resource entry"}) missing, skipped')
            return None
        base = key or (role or 'part').split(' ')[0]
        keys[base] += 1
        k = base if keys[base] == 1 else f'{base}_{keys[base]}'
        parts.append(dict(key=k, parent=parent, mesh=mesh or 0, model=model or '', material=0, role=role or '',
                          variant=variant, condition=condition, position=vec3(position) if position is not None else [],
                          rotation=rotation_deg(rotation) if rotation is not None else [],
                          scale=vec3(scale) if scale is not None else [], lods=levels or []))
        return k

    root = rule.get('root')
    root_key = ''
    if isinstance(root, int) and root > 0:
        root_key = add(root, 'hull', levels=[0] if lod_managed else [], key='hull') or ''

    children = rule.get('children') or []
    # Nodes referenced as parents that aren't meshes (turret pivots) become empty transforms.
    if any(str(c.get('parent') or '').startswith('pivot') for c in children):
        add(0, 'pivot', key='pivot')

    by_mesh, by_role, created = {}, {}, []
    for c in children:
        mesh = c.get('mesh')
        if not isinstance(mesh, int): continue
        variant = c.get('variant') if c.get('variant') in ('player', 'npc') else ''
        condition = '' if variant or not c.get('variant') else str(c['variant'])
        for token, state in STATE_ROLES:
            if token in (c.get('role') or ''): condition = state
        levels = []
        if lod_managed:
            levels = [0]
            for i, l in enumerate(lods):
                kids = [x for x in (l.get('children') or []) if isinstance(x, int)]
                if variant == 'player': kids = [x for x in (l.get('childrenPlayer') or kids) if isinstance(x, int)]
                if mesh in kids: levels.append(i + 1)
        k = add(mesh, c.get('role'), variant=variant, condition=condition, position=c.get('position'),
                rotation=c.get('rotation'), scale=c.get('scale'), levels=levels)
        if k is None: continue
        created.append((k, c))
        by_mesh.setdefault(mesh, k)
        by_role.setdefault((c.get('role') or '').split(' ')[0], k)

    # Resolve parents: '' (root object), 'pivot', a mesh id, 'container #1'/'container 0', a role or model name.
    for k, c in created:
        p = str(c.get('parent') or '').strip()
        target = ''
        if not p or p.startswith('root') or p.startswith('turret container') or (root and p == str(root)):
            target = ''
        elif p.startswith('pivot'):
            target = 'pivot'
        elif re.match(r'^\d+$', p):
            target = by_mesh.get(int(p), '')
        elif p.startswith('container'):
            target = by_role.get('container', '')
        else:
            token = p.split(' ')[0]
            target = by_role.get(token, '')
            if not target:
                target = next((pk for pk, pc in created if token in (model_of(pc.get('mesh')) or '')), '')
            if not target: problems.append(f'{name}: parent "{p}" of {c.get("mesh")} not resolved, attached to root')
        if target == k: target = ''
        next(x for x in parts if x['key'] == k)['parent'] = target

    # LOD meshes; their extra children (listed per LOD, not part of LOD 0) are added per level.
    distances = []
    for i, l in enumerate(lods):
        distances.append(float(l['distance']))
        add(l['mesh'], f'lod_{i + 1}', levels=[i + 1], key=f'lod_{i + 1}')
        for x in l.get('children') or []:
            if isinstance(x, int) and x not in by_mesh:
                add(x, f'lod_{i + 1}_child', levels=[i + 1])
        chain = str(l.get('childTransform') or '') + ' '.join(str(x) for x in l.get('children') or [] if isinstance(x, str))
        if 'container_lod_1 chain' in chain:
            # Freighter LOD: low-detail containers (17053) replace the containers, same hierarchy.
            lod_container = 17053
            cont = [(pk, pc) for pk, pc in created if (pc.get('role') or '').startswith('container')]
            twin = {}
            for pk, pc in cont:
                src = next(x for x in parts if x['key'] == pk)
                nk = add(lod_container, f'container_lod_{i + 1}', parent=twin.get(src['parent'], src['parent']),
                         condition=src['condition'], position=src['position'] or None, levels=[i + 1])
                if nk: twin[pk] = nk

    lvd = rule.get('lastVisibleDistance')
    related = rule.get('related') or {}
    explosion = related.get('explosion') if isinstance(related.get('explosion'), int) else None
    notes = rule.get('notes') or ''
    if isinstance(rule.get('scale'), str): notes += f' | scale: {rule["scale"]}'
    return dict(name=name, pack=pack_of(model_of(root) if isinstance(root, int) else (parts[1]['model'] if len(parts) > 1 else '')),
                category=rule.get('category') or source, origin=rule.get('origin') or source, notes=notes.strip(' |'),
                root=root if isinstance(root, int) else 0, parts=parts, lodDistances=distances,
                lastVisibleDistance=float(lvd) if isinstance(lvd, (int, float)) else 0.0,
                scale=vec3(rule.get('scale')) if not isinstance(rule.get('scale'), str) else [],
                spawnRotation=rotation_deg(rule.get('rootRotation')) if rule.get('rootRotation') is not None else [],
                explosionModel=model_of(explosion) if explosion and fbx_exists(model_of(explosion)) else '')

entries = []
station_roots = {r['root'] for r in json.load(open(os.path.join(RULES, 'assemblies_stations.json'))) if isinstance(r.get('root'), int)}
for source in ('ships', 'stations', 'level', 'scenes'):
    for rule in json.load(open(os.path.join(RULES, f'assemblies_{source}.json'))):
        if source == 'level':
            # Special stations are built by PlayerStation too; the station rules are the complete version.
            if rule.get('category') == 'stations_special' and rule.get('root') in station_roots: continue
            # Suns, planets and skyboxes are the plane mesh / sky meshes textured per star system at runtime:
            # nothing to pre-assemble. The static freighter is the same build as ship 15 (Midorian).
            if rule.get('category') in ('suns', 'planets', 'skyboxes'): continue
            if rule['name'].startswith('cargo_001_midorian'): continue
        e = convert(rule, source)
        if len([p for p in e['parts'] if p['mesh']]) == 0:
            problems.append(f'{e["name"]}: no parts left, dropped'); continue
        entries.append(e)

# ---- name-based fallback for mesh groups no code rule covers ----------------------------------------
covered = {p['mesh'] for e in entries for p in e['parts'] if p['mesh']}
groups = json.load(open(os.path.join(RULES, 'name_groups.json')))['groups']
fallback = 0
for g in groups:
    sets = {}
    for m in g['members']:
        if m['lod'] != 0: continue          # LOD distances are unknown for these; the game never assembles them
        base = g['base'] + ('_' + m['subpart'] if m['subpart'] else '')
        sets.setdefault(base, []).append(m)
    for base, members in sets.items():
        ids = [(m['ids'][0] if m['ids'] else None, m) for m in members]
        ids = [(i, m) for i, m in ids if i and fbx_exists(model_of(i)) and model_of(i) == m['model']]
        if len(ids) < 2 or any(i in covered for i, _ in ids): continue
        main = next(((i, m) for i, m in ids if not m['layers']), ids[0])
        e = dict(name=slug(base), pack=pack_of(main[1]['model']), category=main[1]['model'].split('/')[2],
                 origin='name-based (no assembling code found; layers share one transform)', notes='',
                 root=main[0], parts=[], lodDistances=[], lastVisibleDistance=0.0, scale=[], spawnRotation=[], explosionModel='')
        for i, m in [main] + [x for x in ids if x is not main]:
            role = 'hull' if i == main[0] else (m['layers'] or 'layer')
            e['parts'].append(dict(key=role if i == main[0] else f'{role}_{i}', parent='', mesh=i, model=model_of(i), material=0,
                                   role=role, variant='', condition='', position=[], rotation=[], scale=[], lods=[]))
        entries.append(e); fallback += 1

# Different builds of the same model: name them after what they are (matched on the origin text).
RENAMES = [('station_076_midorian', 'mission exactly 0x5e', 'station_111_luur_mission_94'),
           ('station_076_midorian', 'case 0x59', 'station_111_luur_intact_mission_89'),
           ('sn_station_113_midorian', 'col_box', 'sn_station_113_midorian_wreck_proxy'),
           ('v_station_battlestation_anim', 'Level::createStaticObject', 'v_station_battlestation_anim_mission_object'),
           ('sn_burning_station', 'index==0x6f', 'sn_burning_station_luur'),
           ('sn_burning_station', 'Level::createStaticObject', 'sn_burning_station_mission_object')]
for e in entries:
    for name, origin, new in RENAMES:
        if e['name'] == name and origin in e['origin']: e['name'] = new
# The battlestation turret is in both the station and the turret rules; the turret one has the pivot hierarchy.
entries = [e for e in entries if not (e['name'] == 'v_station_battlestation_turret' and e['category'] == 'stations')]

names = Counter(e['name'] for e in entries)
seen = Counter()
for e in entries:
    if names[e['name']] > 1:
        seen[e['name']] += 1
        e['name'] = f'{e["name"]}_{seen[e["name"]]}'

json.dump(dict(note='Generated by Reference/tools/asset_conversion/assemblies/build_assemblies.py. Do not edit by hand.',
               entries=entries), open(OUT, 'w'), indent=1)
print(len(entries), 'entries', f'({fallback} name-based)', Counter(e['category'] for e in entries))
print(len(problems), 'problems')
for p in problems: print('  ', p)
