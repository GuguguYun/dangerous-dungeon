#!/usr/bin/env python3
"""Make the game's assets with the Meshy API (staging account).

Pipeline per asset: concept image -> image-to-3D (textured) -> remesh to a game budget
-> (characters) rigging -> library animations. Every task id goes into assets/meshy/state.json
before waiting on it, so a rerun resumes and never pays for the same step twice.

Usage:
  scripts/meshy_assets.py images [names...]   submit/await concept images, download PNGs
  scripts/meshy_assets.py models [names...]   image -> textured 3D model
  scripts/meshy_assets.py remesh [names...]   model -> game polycount (GLB + FBX)
  scripts/meshy_assets.py rig [names...]      characters: rigging (+ walk/run clips)
  scripts/meshy_assets.py anim [names...]     characters: library action clips
  scripts/meshy_assets.py status              one line per asset
"""
import json
import pathlib
import subprocess
import sys
import time
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "assets" / "meshy"
STATE = OUT / "state.json"
KEY = [
    "--api-key-file", str(pathlib.Path.home() / ".config/meshy/staging.env"),
    "--base-url-v1", "https://api.staging.meshy.ai/openapi/v1",
    "--base-url-v2", "https://api.staging.meshy.ai/openapi/v2",
    "--output-schema", "v1", "--format", "json", "--no-update-check",
]

STYLE = ("Stylized 3D game asset, chunky cute proportions, bold readable silhouette, hand-painted "
         "texture look, vibrant colors, soft flat even lighting, plain light gray background, "
         "centered, whole subject visible, no text.")
CHAR = "Neutral expression, empty hands, standing in A-pose, front view, full body visible. "
PROP = "Single object, three-quarter view, isolated. "
MECH = "Standing in A-pose, front view, full body visible. "

ASSETS = {
    # characters (rigged); height in metres
    "hero": dict(kind="char", height=1.4, polycount=12000, prompt=(
        "A small heroic knight character for a 3D action game, about 3 heads tall, blue steel plate armor "
        "with gold trim, rounded helmet with a glowing cyan visor slit, short red scarf, brown leather gloves "
        "and boots, no weapon. ")),
    "gunner": dict(kind="char", height=1.3, polycount=10000, prompt=(
        "A goblin gunner enemy for a 3D action game, about 3 heads tall, green skin, big pointy ears, "
        "mischievous grin, brown leather vest, red bandana, goggles on the forehead, no weapon. ")),
    "charger": dict(kind="char", height=1.6, polycount=10000, prompt=(
        "A hulking boar-headed brute enemy for a 3D action game, purple fur, big white tusks, spiked iron "
        "shoulder pads, barrel chest, short sturdy legs, belt with skull buckle, no weapon. ")),
    "spreader": dict(kind="char", height=1.3, polycount=10000, prompt=(
        "A goblin shaman enemy for a 3D action game, about 3 heads tall, green skin, wears a bone skull mask, "
        "tattered purple robe with glowing green runes, feathers and beads, no staff. ")),
    "boss": dict(kind="char", height=2.6, polycount=15000, prompt=(
        "A giant orc warlord boss for a 3D action game, heavy and muscular, dark red skin, horned iron "
        "helmet, black spiked plate armor, fur cape, glowing yellow eyes, no weapon. ")),
    # weapons (static); length in metres along the barrel / blade
    "pistol": dict(kind="weapon", length=0.45, polycount=3000, prompt=(
        "A chunky retro sci-fi pistol, worn brass and dark steel, glowing cyan energy cell on the side. ")),
    "smg": dict(kind="weapon", length=0.6, polycount=3000, prompt=(
        "A compact stylized submachine gun, dark gunmetal with orange accents, short barrel, curved magazine. ")),
    "shotgun": dict(kind="weapon", length=0.9, polycount=3000, prompt=(
        "A stylized double-barrel shotgun, short sawn-off barrels, warm wooden stock, brass fittings. ")),
    "rifle": dict(kind="weapon", length=1.0, polycount=3000, prompt=(
        "A stylized sci-fi energy rifle, long barrel, glowing green coils, white and dark gray armor panels. ")),
    "launcher": dict(kind="weapon", length=1.0, polycount=3000, prompt=(
        "A stylized shoulder rocket launcher tube, olive green with red warning stripes, chunky scope and grip. ")),
    "sword": dict(kind="weapon", length=1.3, polycount=3000, prompt=(
        "A knight greatsword, wide steel blade with glowing blue runes, golden crossguard, leather-wrapped grip. ")),
    "hammer": dict(kind="weapon", length=1.2, polycount=3000, prompt=(
        "A huge war hammer, stone and iron head with crackling yellow lightning runes, long wooden handle. ")),
    # props
    "chest": dict(kind="prop", size=1.0, polycount=3000, prompt=(
        "A treasure chest, dark wood with gold trim and corners, big golden lock, lid closed. ")),
    "barrel": dict(kind="prop", size=1.1, polycount=2000, prompt=(
        "A wooden barrel with dark iron hoops, slightly worn. ")),
    "brazier": dict(kind="prop", size=1.4, polycount=3000, prompt=(
        "A standing iron brazier on three legs with a bowl of glowing orange coals. ")),
    # round 2: higher-quality hero and orc (same concept images, 4K + PBR, denser mesh)
    "hero_hq": dict(kind="char", height=1.4, polycount=30000, from_image="hero", texture="4k", pbr=True, prompt=""),
    "boss_hq": dict(kind="char", height=2.6, polycount=30000, from_image="boss", texture="4k", pbr=True, prompt=""),
    # round 2: floor bosses
    "goblin_king": dict(kind="char", height=2.2, polycount=25000, texture="4k", prompt=(
        "A fat goblin king boss for a 3D action game, green skin, huge belly, golden crown with red gems, "
        "red royal cape with white fur trim, gold rings and chains, big grin with crooked teeth, no weapon. ")),
    "boar_chief": dict(kind="char", height=2.8, polycount=25000, texture="4k", prompt=(
        "A huge boar chieftain boss for a 3D action game, brown fur, massive tusks, tribal red war paint, "
        "horned bull-skull headdress, bone necklace, leather kilt with iron plates, no weapon. ")),
    # round 2: weapons with special mechanics
    "bow": dict(kind="weapon", length=1.1, polycount=3000, prompt=(
        "A stylized hunting longbow, curved dark wood limbs with green leaf carvings, taut glowing string. ")),
    "laser": dict(kind="weapon", length=0.8, polycount=3000, prompt=(
        "A stylized sci-fi laser gun, sleek white body with a big glowing red crystal lens at the front. ")),
    "flamer": dict(kind="weapon", length=0.9, polycount=3000, prompt=(
        "A stylized flamethrower, brass nozzle, red fuel tank on top, copper pipes, small pilot flame. ")),
    "grenade": dict(kind="weapon", length=0.8, polycount=3000, prompt=(
        "A stylized grenade launcher with a fat rotating drum magazine, olive steel, yellow hazard marks. ")),
    "missile": dict(kind="weapon", length=0.9, polycount=3000, prompt=(
        "A stylized shoulder missile pod with four small rocket tubes, dark steel, red rocket tips, targeting screen. ")),
    "thunder": dict(kind="weapon", length=1.3, polycount=3000, prompt=(
        "A stylized wizard staff with a crackling blue storm orb caged in gold prongs at the top, dark wood shaft. ")),
    "frost": dict(kind="weapon", length=1.3, polycount=3000, prompt=(
        "A stylized ice staff topped with a cluster of glowing pale blue ice crystals, silver shaft with frost. ")),
    "boomerang": dict(kind="weapon", length=0.6, polycount=2500, prompt=(
        "A stylized ornate boomerang, V shape, painted wood with gold tips and blue tribal patterns. ")),
    "spear": dict(kind="weapon", length=1.8, polycount=3000, prompt=(
        "A stylized long spear, leaf-shaped steel head with gold inlay, red tassel, long wooden shaft. ")),
    "daggers": dict(kind="weapon", length=0.5, polycount=3000, prompt=(
        "A stylized curved assassin dagger, purple glowing blade, black leather grip, silver guard. ")),
    # round 3: playable classes (the knight is hero_hq)
    "assassin": dict(kind="char", height=1.4, polycount=30000, texture="4k", pbr=True, prompt=(
        "A small stealthy assassin hero for a 3D action game, about 3 heads tall, dark purple hooded cloak, "
        "black leather armor with silver buckles, face mask covering the mouth, glowing violet eyes, "
        "slim build, no weapon. ")),
    "mage": dict(kind="char", height=1.4, polycount=30000, texture="4k", pbr=True, prompt=(
        "A small fire mage hero for a 3D action game, about 3 heads tall, tall pointed red wizard hat, "
        "flowing red and gold robe with star patterns, short white beard, glowing amber eyes, no staff. ")),
    "priest": dict(kind="char", height=1.4, polycount=30000, texture="4k", pbr=True, prompt=(
        "A small holy priestess hero for a 3D action game, about 3 heads tall, white and gold robe, "
        "blue sash, golden halo circlet, long blonde braid, kind face, no weapon. ")),
    # round 4: denser minions (re-remesh the original high-poly models) and room decorations
    "gunner_hq": dict(kind="char", height=1.3, polycount=18000, from_model="gunner", prompt=""),
    "charger_hq": dict(kind="char", height=1.6, polycount=18000, from_model="charger", prompt=""),
    "spreader_hq": dict(kind="char", height=1.3, polycount=18000, from_model="spreader", prompt=""),
    "banner": dict(kind="prop", size=2.2, polycount=3000, prompt=(
        "A tattered hanging cloth war banner, deep crimson with a gold skull emblem, frayed bottom edge, "
        "hanging from a short iron rod, front view, flat. ")),
    "skulls": dict(kind="prop", size=1.0, polycount=3000, prompt=(
        "A small pile of cartoon skulls and bones on the ground, bleached bone color, a few cracks. ")),
    "rubble": dict(kind="prop", size=1.4, polycount=3000, prompt=(
        "A pile of broken grey stone blocks and rubble chunks, dungeon debris, mossy edges. ")),
    "broken_pillar": dict(kind="prop", size=2.0, polycount=3000, prompt=(
        "A broken carved stone pillar, the top snapped off with jagged edge, dark purple-grey stone, standing upright. ")),
    "weapon_rack": dict(kind="prop", size=1.6, polycount=3000, prompt=(
        "A wooden weapon rack holding two old swords, a spear and an axe, iron bands, standing upright. ")),
    "candles": dict(kind="prop", size=0.8, polycount=3000, prompt=(
        "A cluster of melted wax candles of different heights on a small stone slab, warm glowing flames. ")),
    # round 5: trap props
    "gas_crate": dict(kind="prop", size=1.0, polycount=3000, prompt=(
        "A small metal canister crate painted toxic green with a yellow skull hazard sign, leaking valves, "
        "dented steel corners. ")),
    # round 6: pilotable mechs (bipedal so the humanoid rig and animation library fit; weapons built into the arms)
    "light_mech": dict(kind="char", height=2.1, polycount=30000, texture="4k", pbr=True, suffix=MECH, prompt=(
        "A small agile scout mech robot for a 3D action game, bipedal humanoid shape, about 2.5 heads tall, "
        "sleek white and cyan armor plates, round enclosed cockpit head with a glowing cyan visor, slim jointed "
        "legs, small thruster vents on the back, the right forearm is a twin-barrel rapid-fire gun, the left "
        "forearm carries a folded glowing cyan energy blade. ")),
    "heavy_mech": dict(kind="char", height=2.5, polycount=30000, texture="4k", pbr=True, suffix=MECH, prompt=(
        "A small heavy battle mech robot for a 3D action game, bipedal humanoid shape, about 2.5 heads tall, "
        "bulky chunky build, dark gunmetal armor with orange hazard stripes, boxy enclosed cockpit head with a "
        "glowing orange visor, huge shoulders with a missile pod on each shoulder, big hydraulic fists, thick "
        "stompy legs, the right forearm has a short heavy cannon barrel. ")),
    # round 7: the shopkeeper
    "merchant": dict(kind="char", height=1.3, polycount=30000, texture="4k", pbr=True, prompt=(
        "A friendly goblin merchant shopkeeper for a 3D action game, about 3 heads tall, green skin, big ears, "
        "round spectacles, wide grin with a gold tooth, brown hooded travelling coat with many pouches, "
        "a big leather backpack stuffed with scrolls and potions, no weapon. ")),
    # round 8: new enemies and a companion
    "bomber": dict(kind="char", height=1.3, polycount=30000, texture="4k", pbr=True, prompt=(
        "A goblin bomber enemy for a 3D action game, about 3 heads tall, green skin, wild toothy grin, leather "
        "aviator cap with round goggles, a bandolier of round black bombs with fuses across the chest, a big sack "
        "of bombs on the back, patched brown trousers, no weapon. ")),
    "archer": dict(kind="char", height=1.5, polycount=30000, texture="4k", pbr=True, prompt=(
        "A skeleton archer enemy for a 3D action game, cute chibi proportions, about 3 heads tall, bleached bones, "
        "glowing blue eye sockets, tattered dark blue hood and short cape, a quiver of arrows on the back, one "
        "rusty iron shoulder pad, no weapon. ")),
    "pet_drone": dict(kind="prop", size=0.6, polycount=6000, texture="4k", pbr=True, prompt=(
        "A small cute round flying robot drone companion, one big glowing cyan eye lens, white and cyan shell, "
        "two tiny propeller rotors on the sides, a small antenna, toy-like. ")),
    # round 9: the floor-3 boss
    "lich": dict(kind="char", height=2.6, polycount=30000, texture="4k", pbr=True, prompt=(
        "A skeleton lich king boss for a 3D action game, chunky stylized proportions, tall and gaunt, bleached "
        "skull with glowing icy blue eyes, jagged ice crown, tattered dark purple and frost-blue royal robes with "
        "silver trim, bony hands, floating icy shards around the shoulders, no weapon, no staff. ")),
    # round 10: floor pickups and shop wares (health potion, energy crystal); size = largest dimension
    "potion_red": dict(kind="prop", size=0.5, polycount=4000, prompt=(
        "A chunky stylized health potion: a round-bellied glass flask with a short thick neck, filled to the "
        "top with glowing bright ruby-red liquid with a few bubbles, a big brown cork stopper, a gold metal "
        "collar on the neck tied with a small red ribbon. The glass is painted solid and glossy, not see-through. ")),
    "energy_crystal": dict(kind="prop", size=0.42, polycount=4000, prompt=(
        "A chunk of glowing blue energy crystal: one big chunky faceted crystal shard pointing straight up with "
        "three smaller shards fused around its base, bright sapphire blue with a glowing cyan inner core and "
        "white edge highlights, no rock, no stand, no base. ")),
    # round 11: the last glowing balls (shop blessing and upgrade wares, the blood altar's offering, bombs)
    "blessing_scroll": dict(kind="prop", size=0.6, polycount=4000, prompt=(
        "A chunky stylized magic blessing scroll, half unrolled, cream parchment covered in glowing golden "
        "runes, ornate gold-capped wooden rollers on both ends, a red ribbon with a round red wax seal. ")),
    "smith_anvil": dict(kind="prop", size=0.6, polycount=4000, prompt=(
        "A small chunky stylized blacksmith anvil of dark iron with a short smithing hammer lying across its "
        "top, a red-hot glowing orange sword blade resting on the anvil, toy-like proportions. ")),
    "blood_heart": dict(kind="prop", size=0.55, polycount=4000, prompt=(
        "A large glowing crimson heart-shaped ruby crystal wrapped in thorny dark gold vines, faceted, deep "
        "blood red with a bright inner glow, no stand, no base. ")),
    "bomb": dict(kind="prop", size=0.34, polycount=3000, prompt=(
        "A classic cartoon round black iron bomb with a brass cap on top and a short twisted rope fuse, "
        "a few rivets, chunky stylized. ")),
    # round 12: the shield guard (blocks shots from the front; flank it or break its guard)
    "shield_goblin": dict(kind="char", height=1.5, polycount=30000, texture="4k", pbr=True, prompt=(
        "A stocky armored hobgoblin shield guard enemy for a 3D action game, about 3 heads tall, dark olive "
        "green skin, heavy dented iron helmet with a nose guard and small horns, chainmail shirt under a "
        "rusty iron breastplate, thick leather bracers, big iron boots, a determined scowl, no weapon. ")),
    "tower_shield": dict(kind="prop", size=1.15, polycount=4000, prompt=(
        "A big tall rectangular tower shield, front view, dark wooden planks bound with thick riveted iron "
        "bands and an iron rim, a painted red goblin skull emblem in the middle, battered and chipped. ")),
    # tileable textures for the Blender dungeon kit (image only)
    "tex_floor": dict(kind="texture", prompt=(
        "Seamless tileable texture of square stone floor flagstones, 2 by 2 large slabs with dark grout, "
        "cool blue-gray stone with subtle cracks and moss specks. ")),
    "tex_wall": dict(kind="texture", prompt=(
        "Seamless tileable texture of a castle wall made of large rectangular stone bricks in staggered rows, "
        "muted lavender-gray stone with darker mortar. ")),
    "tex_trim": dict(kind="texture", prompt=(
        "Seamless tileable texture of dark carved stone with worn edges, deep purple-gray granite. ")),
    "tex_wood": dict(kind="texture", prompt=(
        "Seamless tileable texture of warm brown wooden planks with visible grain and a few iron nails. ")),
}
TEXTURE = ("Top-down orthographic view filling the whole square image edge to edge, hand-painted stylized "
           "game texture, flat even lighting with no shadows or highlights, no perspective, no objects, no text.")

# Library action ids (looked up with `meshy animation-catalog list`); order = clip order in the file.
ANIMS = {
    "merchant": {"idle": 89, "taunt": 88, "cast": 125},
    "bomber": {"idle": 89, "throw": 421, "hit": 178, "die": 8},
    "lich": {"idle": 89, "cast": 129, "slam": 127, "hit": 178, "die": 8, "taunt": 88},
    "archer": {"idle": 89, "shoot": 224, "hit": 178, "die": 8},
    "shield_goblin": {"idle": 89, "bash": 4, "hit": 178, "die": 8},
    "light_mech": {"idle": 89, "shoot": 98, "slash": 97, "hit": 178},
    "heavy_mech": {"idle": 89, "shoot": 98, "swing": 128, "stomp": 86, "hit": 178},
    "gunner_hq": {"idle": 89, "shoot": 104, "hit": 178, "die": 8},
    "charger_hq": {"idle": 89, "attack": 4, "hit": 178, "die": 8},
    "spreader_hq": {"idle": 89, "cast": 125, "hit": 178, "die": 8},
    "assassin": {"idle": 89, "shoot": 98, "dodge": 158, "hit": 178, "die": 8, "slash": 92, "cast": 125},
    "mage": {"idle": 89, "shoot": 98, "dodge": 158, "hit": 178, "die": 8, "slash": 97, "cast": 129},
    "priest": {"idle": 89, "shoot": 98, "dodge": 158, "hit": 178, "die": 8, "slash": 97, "cast": 126},
    "hero_hq": {"idle": 89, "shoot": 98, "dodge": 158, "hit": 178, "die": 8, "slash": 97},
    "boss_hq": {"walk": 119, "slam": 127, "swing": 128, "hit": 178, "die": 8, "taunt": 88},
    "goblin_king": {"idle": 89, "slam": 86, "cast": 125, "hit": 178, "die": 8, "taunt": 88},
    "boar_chief": {"idle": 89, "slam": 127, "swing": 128, "hit": 178, "die": 8, "taunt": 88},
    "hero": {"idle": 89, "shoot": 98, "dodge": 158, "hit": 178, "die": 8, "slash": 97},
    "gunner": {"idle": 89, "shoot": 104, "hit": 178, "die": 8},
    "charger": {"idle": 89, "attack": 4, "hit": 178, "die": 8},
    "spreader": {"idle": 89, "cast": 125, "hit": 178, "die": 8},
    "boss": {"walk": 119, "slam": 127, "swing": 128, "hit": 178, "die": 8},
}


def load():
    return json.loads(STATE.read_text()) if STATE.exists() else {}


def save(state, name):
    """Merge one asset's entry into the file as it is now, so parallel stage runs don't clobber each other."""
    fresh = load()
    fresh.setdefault(name, {}).update(state[name])
    STATE.write_text(json.dumps(fresh, indent=1, ensure_ascii=False))


def meshy(*args):
    try:
        p = subprocess.run(["meshy", *args, *KEY], capture_output=True, text=True, timeout=60)
    except subprocess.TimeoutExpired:
        # A hung request: callers treat "network" errors on reads as retryable.
        raise RuntimeError(f"meshy {' '.join(args[:2])}: network timeout after 60 s")
    out = p.stdout
    try:
        d = json.loads(out[out.index("{"):])
    except ValueError:
        raise RuntimeError(f"meshy {' '.join(args[:2])}: no JSON\n{out}\n{p.stderr}")
    if not d.get("ok"):
        raise RuntimeError(f"meshy {' '.join(args[:2])}: {json.dumps(d.get('error'), ensure_ascii=False)}")
    return d["result"]


def submit(resource, *args):
    r = meshy(resource, "create", *args, "--async")
    sub = r.get("submission") or r.get("task") or {}
    return sub.get("task_id") or r.get("task_id")


def wait(resource, task_id, label):
    while True:
        try:
            t = meshy(resource, "get", task_id)["task"]
        except RuntimeError as e:  # a status read never spends credits, so retry network blips
            if "network" not in str(e) and "timeout" not in str(e).lower():
                raise
            print(f"  {label}: network error, retrying", flush=True)
            time.sleep(10)
            continue
        if t["status"] == "SUCCEEDED":
            print(f"  {label}: done ({t.get('consumed_credits')} credits)", flush=True)
            return t
        if t["status"] in ("FAILED", "CANCELED", "EXPIRED"):
            raise RuntimeError(f"{label}: {t['status']} {t.get('task_error')}")
        time.sleep(8)


def fetch(url, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    for attempt in range(5):
        try:
            urllib.request.urlretrieve(url, path)
            break
        except OSError:
            if attempt == 4:
                raise
            time.sleep(5)
    return str(path.relative_to(ROOT))


def names_for(args, kinds=None):
    names = args or list(ASSETS)
    return [n for n in names if kinds is None or ASSETS[n]["kind"] in kinds]


def run_stage(names, stage, resource, make, finish):
    """Submit every missing task first, record it, then wait for all of them."""
    state = load()
    prev = {"model": "image", "remesh": "model", "rig": "remesh", "anim": "rig"}.get(stage)
    for n in names:
        s = state.setdefault(n, {})
        if s.get(stage + "_task") or s.get(stage + "_done"):
            continue
        if prev and not s.get(prev + "_done"):
            print(f"  {n}: waiting for {prev}", flush=True)
            continue
        tid = make(n, s)
        if tid:
            s[stage + "_task"] = tid
            save(state, n)
            print(f"  {n}: {stage} submitted {tid}", flush=True)
    for n in names:
        s = state.get(n, {})
        if s.get(stage + "_done") or not s.get(stage + "_task"):
            continue
        t = wait(resource, s[stage + "_task"], f"{n} {stage}")
        finish(n, s, t)
        s[stage + "_done"] = True
        s.setdefault("credits", 0)
        s["credits"] += t.get("consumed_credits") or 0
        save(state, n)


def stage_images(names):
    # Assets that reuse another asset's concept image inherit its finished image task.
    state = load()
    for n in names:
        src = ASSETS[n].get("from_image")
        if src and not state.get(n, {}).get("image_done") and state.get(src, {}).get("image_done"):
            state.setdefault(n, {}).update(image_task=state[src]["image_task"], image=state[src]["image"], image_done=True)
            save(state, n)

    def make(n, s):
        a = ASSETS[n]
        if a["kind"] == "texture":
            prompt = a["prompt"] + TEXTURE
        else:
            prompt = a["prompt"] + a.get("suffix", CHAR if a["kind"] == "char" else PROP) + STYLE
        args = ["--ai-model", "nano-banana-pro", "--prompt", prompt, "--aspect-ratio", "1:1"]
        if a["kind"] == "char":
            args += ["--pose-mode", "a-pose"]
        return submit("text-to-image", *args)

    def finish(n, s, t):
        s["image"] = fetch(t["image_urls"][0], OUT / "images" / f"{n}.png")

    run_stage(names, "image", "text-to-image", make, finish)


def stage_models(names):
    def make(n, s):
        a = ASSETS[n]
        args = ["--input-task-id", s["image_task"], "--should-texture", "true",
                "--texture-resolution", a.get("texture", "2k"), "--target-formats", "glb,fbx"]
        if a.get("pbr"):
            args += ["--enable-pbr", "true"]
        if a["kind"] == "char":
            args += ["--pose-mode", "a-pose"]
        return submit("image-to-3d", *args)

    def finish(n, s, t):
        s["model_faces"] = t.get("face_count")
        s["model_glb"] = fetch(t["model_urls"]["glb"], OUT / "models" / n / "model.glb")

    run_stage(names, "model", "image-to-3d", make, finish)


def stage_remesh(names):
    # Assets that re-remesh another asset's model inherit its finished image and model tasks.
    state = load()
    for n in names:
        src = ASSETS[n].get("from_model")
        if src and not state.get(n, {}).get("model_done") and state.get(src, {}).get("model_done"):
            state.setdefault(n, {}).update({k: state[src][k] for k in ("image_task", "image", "image_done", "model_task", "model_glb", "model_done")})
            save(state, n)

    def make(n, s):
        a = ASSETS[n]
        return submit("remesh", "--input-task-id", s["model_task"], "--topology", "triangle",
                      "--target-polycount", str(a["polycount"]), "--target-formats", "glb,fbx")

    def finish(n, s, t):
        s["remesh_faces"] = t.get("face_count")
        s["remesh_glb"] = fetch(t["model_urls"]["glb"], OUT / "models" / n / "game.glb")
        if t["model_urls"].get("fbx"):
            s["remesh_fbx"] = fetch(t["model_urls"]["fbx"], OUT / "models" / n / "game.fbx")

    run_stage(names, "remesh", "remesh", make, finish)


def stage_rig(names):
    def make(n, s):
        return submit("rigging", "--input-task-id", s["remesh_task"],
                      "--height-meters", str(ASSETS[n]["height"]))

    def finish(n, s, t):
        r = t.get("result") or {}
        s["rig_fbx"] = fetch(r["rigged_character_fbx_url"], OUT / "models" / n / "rigged.fbx")
        s["rig_glb"] = fetch(r["rigged_character_glb_url"], OUT / "models" / n / "rigged.glb")
        basic = r.get("basic_animations") or {}
        for clip in ("walking", "running"):
            url = basic.get(f"{clip}_fbx_url")
            if url:
                s[f"rig_{clip}"] = fetch(url, OUT / "models" / n / f"{clip}.fbx")

    run_stage(names, "rig", "rigging", make, finish)


def stage_anim(names):
    def make(n, s):
        ids = ",".join(str(i) for i in ANIMS[n].values())
        return submit("animate", "--rig-task-id", s["rig_task"], "--action-ids", ids)

    def finish(n, s, t):
        r = t.get("result") or {}
        url = r.get("animation_fbx_url") or r.get("animation_glb_url")
        ext = "fbx" if r.get("animation_fbx_url") else "glb"
        s["anim_file"] = fetch(url, OUT / "models" / n / f"actions.{ext}")
        s["anim_clips"] = list(ANIMS[n])

    run_stage(names, "anim", "animate", make, finish)


def status():
    state = load()
    total = 0
    for n in ASSETS:
        s = state.get(n, {})
        total += s.get("credits", 0)
        done = [k[:-5] for k in s if k.endswith("_done")]
        print(f"{n:9s} {ASSETS[n]['kind']:6s} done={','.join(done) or '-'} credits={s.get('credits', 0)}")
    print("total credits", total)


if __name__ == "__main__":
    cmd, args = sys.argv[1], sys.argv[2:]
    if cmd == "images":
        stage_images(names_for(args))
    elif cmd == "models":
        stage_models(names_for(args))
    elif cmd == "remesh":
        stage_remesh(names_for(args))
    elif cmd == "rig":
        stage_rig(names_for(args, {"char"}))
    elif cmd == "anim":
        stage_anim(names_for(args, {"char"}))
    elif cmd == "status":
        status()
