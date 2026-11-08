#!/usr/bin/env python3
"""Painted UI art for the game's menus and HUD, made with Meshy text-to-image.

Usage:
  scripts/ui_art.py gen [names...]   submit missing images, wait, download to assets/meshy/ui/<name>.png
  scripts/ui_art.py list             show every name and whether it is downloaded

Cards and the medallion are painted with a flat magenta window; tools/ui_process.py (run in Blender's
Python) keys that window out and writes the game copies to unity/Assets/Resources/UI.
"""
import json
import pathlib
import sys
import threading

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from meshy_assets import ROOT, fetch, submit, wait  # noqa: E402

OUT = ROOT / "assets" / "meshy" / "ui"
STATE = OUT / "state.json"

LOOK = ("Hand-painted fantasy game UI art in the style of Blizzard's Hearthstone: chunky ornate shapes, "
        "warm gold metal and carved stone, rich painterly textures, soft bevels, warm lighting. "
        "Absolutely no text, no letters, no numbers, no watermark.")
CARD = ("An empty vertical fantasy collectible card template, game UI asset, flat front view, perfectly "
        "symmetrical, filling the whole image. Thick ornate {metal} border decorated with {motif}. "
        "In the upper half a large arched portrait window filled with one solid flat pure magenta color (#FF00FF), "
        "no picture inside it. Across the middle, overlapping the bottom of the window, a wide empty {ribbon} "
        "ribbon banner for the card name. Below it a large empty smooth light parchment panel for rules text. "
        "Nothing else inside the card. The card fills the image edge to edge; anything outside the card's "
        "outline (its corners) is one solid flat pure green color (#00FF00). ")
ICON = ("Square fantasy card illustration of {what}, hand-painted Hearthstone card art style, painterly brush "
        "strokes, vivid saturated colors, dramatic rim lighting, dark moody background, subject centered and "
        "large, no border, no frame, no text.")
MEDAL = ("Circular fantasy skill icon of {what}, hand-painted Hearthstone hero power art, painterly, vivid "
         "glowing colors, dark background, subject centered and filling the circle, no border, no text.")
GLYPH = ("A single small fantasy game UI icon of {what}, hand-painted Hearthstone style, chunky readable "
         "silhouette, bold colors, soft bevel, centered, isolated on a plain white background, no text.")

ART = {
    # hero cards (class select)
    "card_knight": dict(ratio="2:3", prompt=CARD.format(
        metal="polished blue steel and gold", motif="a lion head crest at the top and small shields", ribbon="royal blue")),
    "card_assassin": dict(ratio="2:3", prompt=CARD.format(
        metal="blackened iron and dark violet enamel", motif="raven feathers and crossed daggers at the top", ribbon="deep purple")),
    "card_mage": dict(ratio="2:3", prompt=CARD.format(
        metal="crimson enamel and gold", motif="flames and a star at the top", ribbon="scarlet red")),
    "card_priest": dict(ratio="2:3", prompt=CARD.format(
        metal="white marble and gold", motif="sun rays and small angel wings at the top", ribbon="ivory white and gold")),
    # blessing card (buff picks) and the art painted into it
    "card_buff": dict(ratio="2:3", prompt=CARD.format(
        metal="aged bronze and gold", motif="a glowing amber gem at the top", ribbon="dark teal")),
    "buff_hp": dict(ratio="1:1", prompt=ICON.format(what="a glowing red heart bound with iron bands")),
    "buff_armor": dict(ratio="1:1", prompt=ICON.format(what="a heavy steel breastplate with gold rivets")),
    "buff_damage": dict(ratio="1:1", prompt=ICON.format(what="a sword blade sharpened on a whetstone, bright sparks flying")),
    "buff_rate": dict(ratio="1:1", prompt=ICON.format(what="an armored gauntlet striking fast, motion streaks and crackling lightning")),
    "buff_energy": dict(ratio="1:1", prompt=ICON.format(what="a large glowing blue energy crystal overflowing with light")),
    "buff_dash": dict(ratio="1:1", prompt=ICON.format(what="winged leather boots surrounded by swirling wind")),
    "buff_crit": dict(ratio="1:1", prompt=ICON.format(what="a fierce golden eagle's eye with a crosshair glint")),
    "buff_leech": dict(ratio="1:1", prompt=ICON.format(what="a dark clawed hand draining wisps of blue energy")),
    "buff_melee": dict(ratio="1:1", prompt=ICON.format(what="two crossed glowing broadswords")),
    "buff_light": dict(ratio="1:1", prompt=ICON.format(what="a swift swallow bird in flight with feather trails")),
    "buff_double": dict(ratio="1:1", prompt=ICON.format(what="a boot stepping off a small white cloud high in the sky")),
    "buff_dodge": dict(ratio="1:1", prompt=ICON.format(what="a graceful white crane in flight leaving ghostly afterimages")),
    # skill medallion (HUD) and the skill pictures inside it
    "medal": dict(ratio="1:1", bg=True, prompt=(
        "An empty round ornate gold medallion frame for a game skill button, thick sculpted gold rim with small "
        "deep blue sapphire gems, front view, symmetrical; the round center filled with one solid flat pure magenta color (#FF00FF). ")),
    "skill_knight": dict(ratio="1:1", prompt=MEDAL.format(what="a radiant golden shield dome with a lion emblem")),
    "skill_assassin": dict(ratio="1:1", prompt=MEDAL.format(what="a shadowy raven dissolving into purple smoke")),
    "skill_mage": dict(ratio="1:1", prompt=MEDAL.format(what="a flaming meteor falling from a starry sky")),
    "skill_priest": dict(ratio="1:1", prompt=MEDAL.format(what="a circle of warm holy light with a rising sun")),
    "skill_light": dict(ratio="1:1", prompt=MEDAL.format(what="a cyan mech thruster overdrive, glowing engine vents and speed lines")),
    "skill_heavy": dict(ratio="1:1", prompt=MEDAL.format(what="a salvo of six orange missiles launching from a mech shoulder pod")),
    # HUD pieces
    "panel": dict(ratio="16:9", bg=True, prompt=(
        "An empty horizontal fantasy game UI panel plate: dark carved slate stone slab with ornate gold filigree "
        "corner pieces and a thin straight gold trim running along the edges, plain uniform dark center, "
        "front view, symmetrical. ")),
    "button": dict(ratio="16:9", bg=True, prompt=(
        "An empty wide horizontal fantasy game menu button: dark polished wood plaque with a chunky gold rim and "
        "small gold end caps on the left and right, plain uniform center, front view, symmetrical. ")),
    "banner": dict(ratio="16:9", bg=True, prompt=(
        "An empty wide horizontal fantasy title ribbon banner: deep red cloth ribbon with gold trim and folded "
        "ends, flat front view, symmetrical, plain center. ")),
    "glyph_heart": dict(ratio="1:1", bg=True, prompt=GLYPH.format(what="a glossy red heart")),
    "glyph_armor": dict(ratio="1:1", bg=True, prompt=GLYPH.format(what="a silver steel kite shield")),
    "glyph_energy": dict(ratio="1:1", bg=True, prompt=GLYPH.format(what="a glowing blue energy crystal shard")),
    "glyph_gold": dict(ratio="1:1", bg=True, prompt=GLYPH.format(what="a small stack of shiny gold coins")),
    "glyph_skull": dict(ratio="1:1", bg=True, prompt=GLYPH.format(what="a cartoon bone-white skull")),
}


def load():
    return json.loads(STATE.read_text()) if STATE.exists() else {}


lock = threading.Lock()


def record(name, **kv):
    with lock:
        s = load()
        s.setdefault(name, {}).update(kv)
        OUT.mkdir(parents=True, exist_ok=True)
        STATE.write_text(json.dumps(s, indent=1, ensure_ascii=False))


def gen(names):
    state = load()
    todo = [n for n in names if not state.get(n, {}).get("done")]
    for n in todo:
        if state.get(n, {}).get("task"):
            continue
        a = ART[n]
        args = ["--ai-model", "gpt-image-2", "--prompt", a["prompt"] + " " + LOOK, "--aspect-ratio", a["ratio"]]
        if a.get("bg"):
            args += ["--remove-background", "true"]
        tid = submit("text-to-image", *args)
        record(n, task=tid)
        print(f"submitted {n}: {tid}", flush=True)

    def finish(n):
        tid = load()[n]["task"]
        try:
            t = wait("text-to-image", tid, n)
        except RuntimeError as e:
            print(f"FAILED {n}: {e}", flush=True)
            record(n, task=None)
            return
        path = fetch(t["image_urls"][0], OUT / f"{n}.png")
        record(n, done=True, image=path, credits=t.get("consumed_credits"))

    threads = [threading.Thread(target=finish, args=(n,)) for n in todo]
    for th in threads:
        th.start()
    for th in threads:
        th.join()
    s = load()
    print("credits:", sum((s[n].get("credits") or 0) for n in names if n in s))


if __name__ == "__main__":
    cmd, rest = (sys.argv[1], sys.argv[2:]) if len(sys.argv) > 1 else ("list", [])
    if cmd == "gen":
        gen(rest or list(ART))
    else:
        s = load()
        for n in ART:
            print(f"{n:16} {'done' if s.get(n, {}).get('done') else s.get(n, {}).get('task') or '-'}")
