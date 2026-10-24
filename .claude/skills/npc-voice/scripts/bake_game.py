"""Bake the picked NPC voice lines into the game: Drive FLAC -> levelled OGG in Assets/_Game/Audio/Voices/NPC/.

python -I bake_game.py <batch.json> <picks.json> <clips folder>

For each NPC: the picked voice's kept lines that passed the speaker check (a scrapped line has no clip and is left
out, so an NPC never speaks in another voice). Each is levelled to -20 LUFS (true peak -1.5 dB), mono, 24 kHz, Ogg
Vorbis q5: VO_<Npc>_<n>.ogg. Writes the manifest Assets/_Game/Audio/Voices/NPC/npc_voice_lines.json that
Krytical Nova > Setup > NPC Voice Lines reads to fill each NPC's lines (text + clip). Old VO files are replaced.
"""
import json, os, shutil, subprocess, sys

PROJECT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", ".."))
DEST = os.path.join(PROJECT, "Assets", "_Game", "Audio", "Voices", "NPC")


def main():
    batch, picks_path, clips = sys.argv[1], sys.argv[2], sys.argv[3]
    vendors = {v["npc"]: v for v in json.load(open(batch, encoding="utf-8"))["vendors"]}
    picks = json.load(open(picks_path, encoding="utf-8"))
    manifest, missing = [], []
    for npc, pick in picks["picks"].items():
        v = vendors[npc]
        folder = os.path.join(DEST, npc)
        if os.path.isdir(folder):
            for f in os.listdir(folder):
                if f.startswith("VO_") and f.endswith(".ogg"):
                    os.remove(os.path.join(folder, f))  # its .meta stays, so a clip that comes back keeps its GUID
        os.makedirs(folder, exist_ok=True)
        lines = []
        for n in pick["keep"]:
            src = os.path.join(clips, f"{npc}_{pick['voice']}_{n}.flac")
            if not os.path.exists(src):
                missing.append(f"{npc}_{pick['voice']}_{n}")
                continue
            name = f"VO_{npc}_{n}.ogg"
            subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", src,
                            "-af", "loudnorm=I=-20:TP=-1.5:LRA=11", "-ac", "1", "-ar", "24000",
                            "-c:a", "libvorbis", "-q:a", "5", os.path.join(folder, name)], check=True)
            lines.append({"text": v["lines"][n - 1],
                          "clip": f"Assets/_Game/Audio/Voices/NPC/{npc}/{name}"})
        manifest.append({"npc": npc, "asset": f"NPC_{picks['asset_ids'][npc]}", "voice": pick["voice"],
                         "voiceDescription": v["voices"][pick["voice"]], "lines": lines})
    with open(os.path.join(DEST, "npc_voice_lines.json"), "w", encoding="utf-8") as f:
        json.dump({"_about": "Made by .claude/skills/npc-voice/scripts/bake_game.py; read by Krytical Nova > Setup > NPC Voice Lines.",
                   "npcs": manifest}, f, indent=1, ensure_ascii=False)
    total = sum(len(m["lines"]) for m in manifest)
    size = sum(os.path.getsize(os.path.join(dp, f)) for dp, _, fs in os.walk(DEST) for f in fs if f.endswith(".ogg"))
    print(f"Baked {total} lines for {len(manifest)} NPCs, {size / 1024:.0f} KB -> {DEST}")
    if missing:
        print("Left out (scrapped, never another voice): " + ", ".join(missing))


if __name__ == "__main__":
    main()
