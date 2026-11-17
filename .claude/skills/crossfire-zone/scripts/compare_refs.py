# python compare_refs_022.py : EVERY owner ref beside the build at the same angle (owner's 4 images of 16 Nov evening, the 5 concept
# paintings, the 8 close-up refs of 16 Nov) -> Review022/Refs_vs_Build_022.jpg (+ copy to Drive AI Outputs/crucible)
import os, shutil
from PIL import Image, ImageDraw, ImageFont
R = os.path.dirname(os.path.dirname(os.path.abspath(__file__))); rev = os.path.join(R, 'Review022')
OWN = r"C:\Users\dlaco\AppData\Local\Claude-Data\claude_pzs8sxrjxfjjc\side-session-notes\local_53d2243f-4863-41be-b554-47fd2c9332bc\crucible-022-owner-images"
CON = r"G:\My Drive\Krytical Nova Zones\Starter Zone Concepts\Siege Night\The Crucible - Lv 90-112"
OLD = r"G:\My Drive\KryticalNova\AI Outputs\crucible\owner-refs-2026-11-16"
pairs = [(OWN, '1.png', 'own1_cov_gate_out', 'Owner 1: Covenant gate, arena side'), (OWN, '2.png', 'own2_heg_gate_out', 'Owner 2: Hegemony gate, arena side'),
         (OWN, '3.png', 'own3_arena_cover', 'Owner 3 (021 build): brick ruins -> dark stone'), (OWN, '4.webp', 'own4_sealed_gate', 'Owner 4: sealed north gate, chains'),
         (CON, '01_Central_Heart__South_Path_Looking_North.png', '01_heart', 'Concept 01: heart'), (CON, '02_Hegemony_Yard__Inner_Gate_Looking_East.png', '02_hegemony', 'Concept 02: Hegemony yard'),
         (CON, '03_Covenant_Yard__Rear_Shrine_Looking_East.png', '03_covenant', 'Concept 03: Covenant yard'), (CON, '04_Null_Yard__Respawn_Apron_Looking_North.png', '04_null', 'Concept 04: Null yard'),
         (CON, '05_Entire_Battlefield_Panorama.png', '05_panorama', 'Concept 05: panorama'),
         (OLD, '05_arena_bowl_overhead_stairs_pillars.webp', 'ref_arena', 'Arena from above'), (OLD, '01_heart_dais_closeup.png', 'ref_heart', 'Heart dais'),
         (OLD, '03_covenant_shrine_crystal_closeup.webp', 'ref_cov_shrine', 'Covenant shrine'), (OLD, '04_null_shrine_crystal_and_gate_closeup.webp', 'ref_null_shrine', 'Null shrine and gate'),
         (OLD, '02_hegemony_shrine_beam_plinth.png', 'ref_heg_shrine', 'Hegemony shrine plinth'), (OLD, '02_hegemony_yard_topdown_walls.png', 'ref_heg_top', 'Hegemony wall tops'),
         (OLD, '03_covenant_yard_topdown_walls.png', 'ref_cov_top', 'Covenant wall tops'), (OLD, '04_null_yard_topdown_walls.png', 'ref_null_top', 'Null wall tops')]
f = ImageFont.truetype('arialbd.ttf', 24); H = 560; rows = []
for d, ref, shot, title in pairs:
    a = Image.open(os.path.join(d, ref)).convert('RGB'); b = Image.open(os.path.join(rev, 'f_' + shot + '.png')).convert('RGB')
    a = a.resize((min(1000, int(a.width * H / a.height)), H) if a.width * H / a.height > 1000 else (int(a.width * H / a.height), H), Image.LANCZOS); b = b.resize((int(b.width * H / b.height), H), Image.LANCZOS)
    sh = Image.new('RGB', (1000 + b.width + 30, H + 56), (14, 14, 22)); dr = ImageDraw.Draw(sh)
    dr.text((12, 14), "OWNER REF - " + title, fill=(240, 200, 90), font=f); dr.text((1042, 14), "BUILD 022 - same angle (Blender preview light)", fill=(130, 220, 255), font=f)
    sh.paste(a, ((1000 - a.width) // 2, 56)); sh.paste(b, (1030, 56)); rows.append(sh)
W = max(r.width for r in rows); al = Image.new('RGB', (W, sum(r.height for r in rows)), (14, 14, 22)); y = 0
for r in rows: al.paste(r, (0, y)); y += r.height
n = os.path.join(rev, 'Refs_vs_Build_022.jpg'); al.save(n, quality=86)
al.resize((al.width // 2, al.height // 2), Image.LANCZOS).save(os.path.join(rev, 'Refs_small_022.jpg'), quality=85)
dst = r"G:\My Drive\KryticalNova\AI Outputs\crucible"
if os.path.isdir(dst): shutil.copy(n, dst)
print(n, al.size)
