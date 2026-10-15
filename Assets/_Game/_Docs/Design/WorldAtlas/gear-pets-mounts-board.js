// Krytical Nova II — World Atlas (Figma) — page "Gear, pet, mount Concept art"
// Run inside Figma with that file open (README.md next to this file says how).
// Safe to run again, also on a board an earlier version of this script already changed.
//
// GEAR board, every lineage column of all three factions:
//   • Rare and Epic look boxes: a 1920 × 1080 concept-sheet area, the six piece slots kept,
//     three weapon slots added
//   • Rare → EPIC FACTION CAPE band → Epic → Legendary (God levels G1–G4, G5–G8, G9–G12)
//     → LEGENDARY FACTION CAPE band. Each cape band spans both lineages of the faction and
//     holds one 1920 × 1080 image: one cape per faction per tier.
//   • the tier once called Elite is now Epic; the God-level tier is Legendary
// PETS & MOUNTS board, to the right:
//   • the three Twelve Sky 2 pet reference tables
//   • eight new pet boxes: 1920 × 1080 reference image, the pet's stat row, five evolution boxes
//   • four mount slots
// Images already dropped into the pets & mounts board are carried over when it is rebuilt.

const PAGE_NAME = 'Gear, pet, mount Concept art';

const C = {
  ink: '#14122b', cream: '#f0ebdd', paper: '#fffef8', panel: '#e5e1d8', grey: '#696578',
  dash: '#a9a3b4', cyan: '#4fe3f0', cyanPale: '#dce8f0', yellow: '#f2d35b', purple: '#b591ec',
  purplePale: '#e3d6f8', legend: '#ff7a3d', legendPale: '#ffc49c', mint: '#fff6c4', red: '#d8343c',
};
function rgb(hex) { const n = parseInt(hex.slice(1), 16); return { r: ((n >> 16) & 255) / 255, g: ((n >> 8) & 255) / 255, b: (n & 255) / 255 }; }
const solid = (hex) => [{ type: 'SOLID', color: rgb(hex) }];

// ---- sizes ---------------------------------------------------------------------------------
const BOX_W = 2000, INNER_W = 1952, LIN_W = 2048, IMG_W = 1920, IMG_H = 1080;
const PIECE_GRID_W = 528, WEAPON_H = 204 * 3 + 24 * 2;
const FACTION_W = 32 + LIN_W * 2 + 32 + 32;            // 4192
const FACTION_GAP = 354, FACTIONS_PAD = 129;
const EVO_IMG_W = 960, EVO_IMG_H = 540;
const PET_BOX_W = (EVO_IMG_W + 32) * 5 + 24 * 4 + 48; // 5104: five evolution slots fit inside
const PET_SEC_W = PET_BOX_W + 64, PET_BOARD_W = PET_SEC_W + 256;

const F = {
  label: { family: 'Inter', style: 'Semi Bold' }, body: { family: 'Inter', style: 'Regular' },
  bold: { family: 'Inter', style: 'Bold' }, head: { family: 'Barlow Condensed', style: 'Bold' },
  title: { family: 'Barlow Condensed', style: 'ExtraBold' },
};

// ---- Twelve Sky 2 pet stats ------------------------------------------------------------------
const HP = ['500', '1,000', '1,500', '2,200'], FORCE = ['450', '900', '1,350', '2,000'];
const ATK = ['250', '500', '750', '1,100'], DEF = ['500', '1,000', '1,500', '2,200'];
const NONE = ['–', '–', '–', '–'], ADD = ['100', '150', '200', '250'];
const REF = [
  { rarity: 'Rare', req: 'Adept 1 or higher', pets: [
    ['Horn Beetle', NONE, NONE, ATK, DEF], ['Solitary Owl', HP, NONE, ATK, NONE], ['Blood Bat', NONE, FORCE, ATK, NONE],
    ['Ancient Turtle', HP, NONE, NONE, DEF], ['Sacred Kirin', NONE, FORCE, NONE, DEF], ['Tribal Lion', HP, FORCE, NONE, NONE]] },
  { rarity: 'Epic', req: 'Adept 15 or higher', pets: [
    ['Azure Magpie', HP, NONE, ATK, DEF], ['Flying Hawk', NONE, FORCE, ATK, DEF], ['Crown Seahorse', HP, FORCE, ATK, NONE], ['Tyran Kingbird', HP, FORCE, NONE, DEF]] },
  { rarity: 'Epic', req: 'Adept 30 or higher', pets: [['Mystic Dragon', HP, FORCE, ATK, DEF]] },
];
// The stat layout each new pet gets: [rarity, level requirement, health, flux, attack, defense].
// Edit a line to give a pet a different layout.
const NEW_PET_STATS = [
  ['Rare', 'Adept 1 or higher', NONE, NONE, ATK, DEF],
  ['Rare', 'Adept 1 or higher', HP, NONE, ATK, NONE],
  ['Rare', 'Adept 1 or higher', NONE, FORCE, ATK, NONE],
  ['Rare', 'Adept 1 or higher', HP, NONE, NONE, DEF],
  ['Epic', 'Adept 15 or higher', HP, NONE, ATK, DEF],
  ['Epic', 'Adept 15 or higher', NONE, FORCE, ATK, DEF],
  ['Epic', 'Adept 15 or higher', HP, FORCE, ATK, NONE],
  ['Epic', 'Adept 30 or higher', HP, FORCE, ATK, DEF],
];
const STAT_COLS = [{ label: 'Level', w: 120 }, { label: 'Name', w: 260 }, { label: 'Level requirement', w: 220 },
  ...['Health', 'Flux (Force)', 'Attack (ATK)', 'Defense (DEF)'].flatMap((s) => ['25%', '50%', '75%', '100%'].map((p) => ({ label: s.split(' ')[0] + ' ' + p, w: 110 }))),
  ...['125%', '150%', '175%', '200%'].map((p) => ({ label: 'Add. ATK ' + p, w: 110 }))];
const MAXES = ['2,200', '2,000', '1,100', '250'];

// ==============================================================================================
async function main() {
  for (const f of Object.values(F)) await figma.loadFontAsync(f);
  const page = figma.root.children.find((p) => p.name === PAGE_NAME);
  if (!page) throw new Error('Page not found: ' + PAGE_NAME);
  await figma.setCurrentPageAsync(page);
  const factions = page.children.find((n) => n.name === 'Factions' && n.type === 'FRAME');
  if (!factions) throw new Error('Frame "Factions" not found on the page');
  const report = { factions: [], gearBoxes: 0, sheetsMoved: 0, weaponRows: 0, legendarySections: 0, capeBands: 0, petImagesCarried: 0 };

  let proto = factions.findOne((n) => n.type === 'FRAME' && n.name === 'Armor' && n.parent && n.parent.name === 'Piece art row');
  if (!proto) throw new Error('No dashed "Armor" frame found to use as a template');
  proto = proto.clone(); page.appendChild(proto);

  // every faction panel, wherever it sits (one may have slipped out of the "Factions" frame)
  const factionFrames = page.findAll((n) => n.type === 'FRAME' && n.children.some((c) => c.name === 'Faction identity'))
    .filter((n) => !isInside(n, 'Pets and Mounts'));
  for (const f of factionFrames) if (f.parent !== factions) { const ax = f.absoluteTransform[0][2], ay = f.absoluteTransform[1][2]; factions.appendChild(f); f.x = ax - factions.absoluteTransform[0][2]; f.y = ay - factions.absoluteTransform[1][2]; }
  const order = ['HEGEMONY', 'NULL', 'COVENANT'];
  factionFrames.sort((a, b) => order.indexOf(factionName(a).toUpperCase()) - order.indexOf(factionName(b).toUpperCase()) || a.x - b.x);

  for (const faction of factionFrames) {
    const fname = factionName(faction);
    report.factions.push(fname);
    const lineages = faction.children.filter((n) => n.type === 'FRAME' && n.children.some((c) => c.name === 'Lineage identity')).sort((a, b) => a.x - b.x);
    const capeImages = [];

    for (const lineage of lineages) {
      renameEliteToEpic(lineage);
      for (const box of lineage.findAll((n) => n.type === 'FRAME' && /^(Rare|Epic) • /.test(n.name))) convertLookBox(box, proto, report);

      lineage.resize(LIN_W, lineage.height); lineage.layoutSizingVertical = 'HUG';
      for (const c of lineage.children) if (['Lineage identity', 'Rare', 'Epic'].includes(c.name)) c.layoutSizingHorizontal = 'FILL';
      const ident = lineage.children.find((c) => c.name === 'Lineage identity');
      if (ident) for (const c of ident.children) c.layoutSizingHorizontal = 'FILL';
      for (const sec of ['Rare', 'Epic']) { const s = lineage.children.find((c) => c.name === sec); if (s) for (const c of s.children) c.layoutSizingHorizontal = 'FILL'; }

      // an earlier version put a three-slot capes strip in every column: keep its art, drop it
      for (const old of lineage.children.filter((c) => c.name === 'Faction capes')) {
        capeImages.push(...old.findAll((n) => n.type === 'RECTANGLE' && n.fills.length && n.fills[0].type === 'IMAGE'));
        for (const img of capeImages) if (img.parent && isInside(img, 'Faction capes')) page.appendChild(img);
        old.remove();
      }
      ensureLegendary(lineage, report);
      ensureSpacer(lineage, 'Epic cape space', 'before', 'Epic');
      ensureSpacer(lineage, 'Legendary cape space', 'after', 'Legendary');
    }

    // lineage columns side by side
    lineages.forEach((l, i) => { l.x = 32 + i * (LIN_W + 32); l.y = 232; });

    // faction-wide cape bands sit over the spacers
    const lineageNames = lineages.map((l) => (l.findOne((n) => n.type === 'TEXT' && n.name === 'Lineage name') || { characters: l.name }).characters);
    const bandW = lineages.length ? lineages[lineages.length - 1].x + LIN_W - 32 : FACTION_W - 64;
    const epicBand = ensureCapeBand(faction, 'Epic faction cape', 'EPIC FACTION CAPE • ' + fname.toUpperCase(), 'EPIC', C.purple, C.purplePale,
      'One cape for the whole ' + fname + ' faction, worn by both ' + lineageNames.join(' and ') + '. Epic rarity: it sits between the Rare and Epic gear tiers.', bandW, proto, report);
    const legBand = ensureCapeBand(faction, 'Legendary faction cape', 'LEGENDARY FACTION CAPE • ' + fname.toUpperCase(), 'LEGENDARY', C.legend, C.legendPale,
      'The God-level cape of the ' + fname + ' faction, one for both ' + lineageNames.join(' and ') + '. Legendary rarity, alongside the G1–G12 gear.', bandW, proto, report);
    if (capeImages.length) { const area = epicBand.findOne((n) => n.name === 'Image area 1920×1080'); if (area && !area.children.length) fitInto(capeImages[0], area); }
    for (const l of lineages) {
      const s1 = l.children.find((c) => c.name === 'Epic cape space'); if (s1) { s1.resize(s1.width, epicBand.height); s1.layoutSizingHorizontal = 'FILL'; }
      const s2 = l.children.find((c) => c.name === 'Legendary cape space'); if (s2) { s2.resize(s2.width, legBand.height); s2.layoutSizingHorizontal = 'FILL'; }
      const dots = l.children.find((c) => c.name === 'Panel edge dot shadow');
      if (dots) { dots.x = Math.round((LIN_W - dots.width) / 2); dots.y = l.height - 184; }
    }
    let maxBottom = 0;
    for (const l of lineages) maxBottom = Math.max(maxBottom, l.y + l.height);
    if (lineages.length) {
      const l0 = lineages[0];
      const sp1 = l0.children.find((c) => c.name === 'Epic cape space'), sp2 = l0.children.find((c) => c.name === 'Legendary cape space');
      faction.appendChild(epicBand); epicBand.x = l0.x; epicBand.y = l0.y + sp1.y;
      faction.appendChild(legBand); legBand.x = l0.x; legBand.y = l0.y + sp2.y;
    }

    const identity = faction.children.find((c) => c.name === 'Faction identity');
    if (identity) {
      identity.resize(FACTION_W - 64, identity.height);
      const accent = identity.children.find((c) => c.name === 'Faceted header accent');
      if (accent) accent.x = identity.width - accent.width + 95;
      const lookCount = identity.findOne((n) => n.type === 'TEXT' && n.name === 'Look count');
      if (lookCount) lookCount.characters = lineages.length + ' LINEAGES / ' + lineages.length * 10 + ' LOOKS / 2 CAPES';
    }
    const refs = faction.children.find((c) => c.name === 'Faction weapon references');
    if (refs) { refs.y = maxBottom + 32; refs.resize(FACTION_W - 64, refs.height); for (const c of refs.children) c.layoutSizingHorizontal = 'FILL'; maxBottom = refs.y + refs.height; }
    faction.resize(FACTION_W, maxBottom + 32);
  }

  // faction panels side by side, board around them
  let factionBottom = 0;
  factionFrames.forEach((f, i) => { f.x = FACTIONS_PAD + i * (FACTION_W + FACTION_GAP); f.y = 416; factionBottom = Math.max(factionBottom, f.y + f.height); });
  const boardW = FACTIONS_PAD * 2 + FACTION_W * 3 + FACTION_GAP * 2;
  const notes = factions.children.find((c) => c.name === 'Source and production notes');
  if (notes) {
    notes.y = factionBottom + 100; notes.resize(boardW - 128, notes.height);
    const scope = notes.children.find((c) => c.name === 'Scope'); if (scope) scope.x = notes.width - scope.width;
    const art = notes.children.find((c) => c.name === 'Art language'); if (art) art.x = Math.round((notes.width - art.width) / 2);
  }
  updateBoardGuide(factions, boardW);
  const oldBottom = factions.y + factions.height;
  factions.resize(boardW, (notes ? notes.y + notes.height : factionBottom) + 64);
  const newBottom = factions.y + factions.height;
  const loose = page.children.filter((n) => n !== factions && n.type === 'RECTANGLE' && n.y > factions.y + 1000 && n.y < newBottom && n.x + n.width > factions.x && n.x < factions.x + boardW);
  if (loose.length) { const minY = Math.min(...loose.map((n) => n.y)); const shift = newBottom + 400 - minY; if (shift > 0) for (const n of loose) n.y += shift; }

  // ================= PETS & MOUNTS (rebuilt, art carried over) ==============================
  const accentProto = factionFrames[0] && factionFrames[0].findOne((n) => n.name === 'Faceted header accent');
  const dotsProto = factionFrames[0] && factionFrames[0].children.find((c) => c.name === 'Faction header dot shadow');
  const carried = [];
  const oldBoard = page.children.find((n) => n.name === 'Pets and Mounts' && n.type === 'FRAME');
  if (oldBoard) {
    for (const img of oldBoard.findAll((n) => n.type === 'RECTANGLE' && n.fills.length && n.fills[0].type === 'IMAGE')) {
      const slot = ancestor(img, (a) => /^(Reference image|Stage \d|Mount \d)$/.test(a.name));
      const box = ancestor(img, (a) => /^(Pet \d\d|Mount roster)$/.test(a.name));
      carried.push({ key: (box ? box.name : '') + '/' + (slot ? slot.name : ''), img });
      page.appendChild(img);
    }
    oldBoard.remove();
  }
  const board = buildPetsAndMountsBoard(proto, accentProto, dotsProto);
  page.appendChild(board);
  board.x = factions.x + factions.width + 600; board.y = factions.y;
  for (const { key, img } of carried) {
    const [boxName, slotName] = key.split('/');
    const box = board.findOne((n) => n.name === boxName);
    const slot = box && box.findOne((n) => n.name === slotName);
    const area = slot && slot.findOne((n) => /^Image area /.test(n.name));
    if (area && !area.children.length) { fitInto(img, area); report.petImagesCarried++; }
    else { img.x = board.x + board.width + 200; img.y = board.y + report.petImagesCarried * 600; }
  }

  proto.remove();
  report.gearBoard = { w: Math.round(factions.width), h: Math.round(factions.height) };
  report.petsBoard = { w: Math.round(board.width), h: Math.round(board.height) };
  return report;
}

// ---- helpers: tree ----------------------------------------------------------------------------
function isInside(n, name) { for (let p = n.parent; p; p = p.parent) if (p.name === name) return true; return false; }
function ancestor(n, fn) { for (let p = n.parent; p; p = p.parent) if (fn(p)) return p; return null; }
function factionName(f) { const t = f.findOne((n) => n.type === 'TEXT' && n.name === 'Faction name'); const s = t ? t.characters : f.name; return s.charAt(0) + s.slice(1).toLowerCase(); }
function toHex(c) { return '#' + [c.r, c.g, c.b].map((v) => Math.round(v * 255).toString(16).padStart(2, '0')).join(''); }

// ---- Elite → Epic --------------------------------------------------------------------------------
function renameEliteToEpic(lineage) {
  const sec = lineage.children.find((c) => c.name === 'Elite');
  if (sec) sec.name = 'Epic';
  const epic = lineage.children.find((c) => c.name === 'Epic');
  if (!epic) return;
  const r = epic.findOne((n) => n.type === 'TEXT' && n.name === 'Rarity'); if (r && r.characters !== 'EPIC') r.characters = 'EPIC';
  for (const box of epic.findAll((n) => n.type === 'FRAME' && /^Elite • /.test(n.name))) box.name = box.name.replace(/^Elite/, 'Epic');
  for (const t of epic.findAll((n) => n.type === 'TEXT' && n.name === 'Scheme name' && /^Elite/.test(n.characters))) t.characters = t.characters.replace(/^Elite/, 'Epic');
}

// ---- one gear look box --------------------------------------------------------------------------
function convertLookBox(box, proto, report) {
  report.gearBoxes++;
  box.resize(BOX_W, box.height); box.layoutSizingVertical = 'HUG';
  for (const c of box.children) if (c.name === 'Look heading' || c.name === 'Shared-look levels') c.layoutSizingHorizontal = 'FILL';
  const areas = box.children.find((c) => c.name === 'Art insertion areas');
  if (!areas) return;
  areas.layoutMode = 'VERTICAL'; areas.itemSpacing = 24; areas.layoutSizingHorizontal = 'FILL'; areas.layoutSizingVertical = 'HUG';
  const full = areas.children.find((c) => c.name === 'Full set / concept art');
  if (full) {
    full.resize(INNER_W, full.height); full.itemSpacing = 8; full.layoutSizingHorizontal = 'FILL'; full.layoutSizingVertical = 'HUG';
    const label = full.children.find((c) => c.type === 'TEXT' && c.name === 'Art target label');
    if (label) { label.characters = 'Full set / concept sheet • 1920 × 1080'; label.layoutSizingHorizontal = 'FILL'; }
    let area = full.children.find((c) => c.name === 'Image area 1920×1080');
    if (!area) { area = imageArea(IMG_W, IMG_H); full.insertChild(1, area); }
    const img = full.children.find((c) => c.type === 'RECTANGLE' && c.fills.length && c.fills[0].type === 'IMAGE');
    if (img) { fitInto(img, area); report.sheetsMoved++; }
  }
  let lower = areas.children.find((c) => c.name === 'Pieces and weapons');
  if (!lower) {
    lower = figma.createFrame(); lower.name = 'Pieces and weapons'; lower.layoutMode = 'HORIZONTAL'; lower.itemSpacing = 24; lower.fills = [];
    areas.appendChild(lower); lower.layoutSizingHorizontal = 'FILL'; lower.layoutSizingVertical = 'HUG';
    const grid = areas.children.find((c) => c.name === 'Piece art grid');
    if (grid) { lower.appendChild(grid); grid.resize(PIECE_GRID_W, grid.height); }
    const row = figma.createFrame(); row.name = 'Weapon slots'; row.layoutMode = 'HORIZONTAL'; row.itemSpacing = 24; row.fills = [];
    lower.appendChild(row); row.layoutSizingHorizontal = 'FILL'; row.layoutSizingVertical = 'HUG';
    for (let i = 1; i <= 3; i++) {
      const slot = proto.clone(); slot.name = 'Weapon ' + i; row.appendChild(slot); slot.resize(440, WEAPON_H); slot.layoutSizingHorizontal = 'FILL';
      setLabel(slot, 'Weapon ' + i + ' • class');
    }
    report.weaponRows++;
  }
  const rim = box.children.find((c) => c.name === 'Rarity rim');
  if (rim) rim.resize(8, box.height);
}

// ---- Legendary (God levels) section after Epic ----------------------------------------------------
const SETS = [['G1–G4', ['G1', 'G2', 'G3', 'G4']], ['G5–G8', ['G5', 'G6', 'G7', 'G8']], ['G9–G12', ['G9', 'G10', 'G11', 'G12']]];
function ensureLegendary(lineage, report) {
  let leg = lineage.children.find((c) => c.name === 'Legendary' || c.name === 'God');
  const epic = lineage.children.find((c) => c.name === 'Epic');
  if (!leg) {
    if (!epic) return;
    leg = epic.clone();
    lineage.insertChild(lineage.children.indexOf(epic) + 1, leg);
    for (const img of leg.findAll((n) => n.type === 'RECTANGLE' && n.fills.length && n.fills[0].type === 'IMAGE')) img.remove();
    for (const s of leg.findAll((n) => n.type === 'TEXT' && (n.name === 'Section status' || n.name === 'Art status'))) s.characters = 'Awaiting art';
    report.legendarySections++;
  }
  leg.name = 'Legendary';
  leg.layoutSizingHorizontal = 'FILL';
  const rarity = leg.findOne((n) => n.type === 'TEXT' && n.name === 'Rarity'); if (rarity) rarity.characters = 'LEGENDARY';
  const count = leg.findOne((n) => n.type === 'TEXT' && n.name === 'Art set count'); if (count) count.characters = '3 GOD-LEVEL LOOKS / G1–G12';
  const marker = leg.findOne((n) => n.type === 'RECTANGLE' && n.name === 'Rarity marker'); if (marker) marker.fills = solid(C.legend);
  const boxes = leg.findAll((n) => n.type === 'FRAME' && /^(Elite|Epic|God|Legendary) • /.test(n.name));
  while (boxes.length > 3) boxes.pop().remove();
  while (boxes.length < 3 && boxes.length) { const extra = boxes[0].clone(); boxes[0].parent.appendChild(extra); boxes.push(extra); }
  boxes.forEach((box, i) => {
    const set = SETS[i];
    box.name = 'Legendary • ' + set[0];
    const name = box.findOne((n) => n.type === 'TEXT' && n.name === 'Scheme name'); if (name) name.characters = 'Legendary • ' + set[0];
    const rim = box.children.find((c) => c.name === 'Rarity rim'); if (rim) rim.fills = solid(C.legend);
    const chips = box.findOne((n) => n.type === 'FRAME' && n.name === 'Level chips');
    if (chips && chips.children.length) {
      const tpl = chips.children[0].clone();
      for (const c of [...chips.children]) c.remove();
      for (const lvl of set[1]) { const ch = tpl.clone(); chips.appendChild(ch); ch.fills = solid(C.legendPale); const t = ch.findOne((n) => n.type === 'TEXT'); if (t) t.characters = lvl; }
      tpl.remove();
    }
    const interval = box.findOne((n) => n.type === 'TEXT' && n.name === 'Level interval'); if (interval) interval.characters = 'God levels / shared look';
  });
}

// ---- spacers that hold room for the faction-wide cape bands ---------------------------------------
function ensureSpacer(lineage, name, where, nextTo) {
  let s = lineage.children.find((c) => c.name === name);
  if (!s) { s = figma.createFrame(); s.name = name; s.fills = []; s.resize(100, 100); }
  const anchor = lineage.children.find((c) => c.name === nextTo);
  if (!anchor) return;
  if (s.parent === lineage) lineage.appendChild(s); // detach to re-insert at the right place
  const at = lineage.children.indexOf(anchor);
  lineage.insertChild(where === 'before' ? at : at + 1, s);
  s.layoutSizingHorizontal = 'FILL';
}

// ---- a faction-wide cape band: one 1920 × 1080 image + notes ---------------------------------------
function ensureCapeBand(faction, name, title, rarityLabel, rimHex, chipHex, note, width, proto, report) {
  let band = faction.children.find((c) => c.name === name);
  if (band) { band.resize(width, band.height); band.layoutSizingVertical = 'HUG'; fitRim(band); return band; }
  band = col(name, { gap: 16, px: 32, py: 24, fill: C.paper, stroke: C.ink, strokeW: 3 });
  faction.appendChild(band);
  band.resize(width, 100); band.layoutSizingVertical = 'HUG';
  const rim = figma.createRectangle(); rim.name = 'Rarity rim'; rim.fills = solid(rimHex); band.appendChild(rim);
  rim.layoutPositioning = 'ABSOLUTE'; rim.x = 0; rim.y = 0; rim.resize(12, 100);
  const h = heading(title, 'Awaiting art'); band.appendChild(h); h.layoutSizingHorizontal = 'FILL';
  const body = col('Cape body', { dir: 'HORIZONTAL', gap: 32 });
  body.appendChild(dashedSlot(proto, 'Cape', 'Faction cape • 1920 × 1080', IMG_W, IMG_H));
  const side = col('Cape notes', { gap: 16 });
  const chips = col('Chips', { dir: 'HORIZONTAL', gap: 8 });
  chips.appendChild(chip(rarityLabel, chipHex)); chips.appendChild(chip('ONE PER FACTION', C.paper)); chips.appendChild(chip('BOTH LINEAGES', C.paper));
  side.appendChild(chips);
  side.appendChild(text(note, F.body, 24, C.ink, { width: Math.max(600, width - 64 - IMG_W - 32 - 32) }));
  body.appendChild(side);
  band.appendChild(body);
  fitRim(band);
  report.capeBands++;
  return band;
}

// ---- gear board guide, key and inventory ------------------------------------------------------------
function updateBoardGuide(factions, boardW) {
  const titleRow = factions.children.find((c) => c.name === 'Atlas title and board guide');
  if (!titleRow) return;
  titleRow.resize(boardW - 2 * 261, titleRow.height); titleRow.layoutSizingVertical = 'HUG';
  const guide = titleRow.children.find((c) => c.name === 'Board guide');
  if (guide) {
    guide.layoutSizingHorizontal = 'FILL';
    for (const c of guide.children) c.layoutSizingHorizontal = 'FILL';
    const heading = guide.children.find((c) => c.name === 'Guide heading');
    if (heading) for (const c of heading.children) if (c.type === 'TEXT') c.layoutSizingHorizontal = 'HUG';
    const hier = guide.children.find((c) => c.name === 'Hierarchy guide');
    if (hier) hier.characters = 'Faction → lineage → Rare / Epic / Legendary → shared-look section → art targets. Each faction has one Epic cape and one Legendary cape.';
    const key = guide.children.find((c) => c.name === 'Rarity key');
    if (key) {
      const eliteDef = key.children.find((c) => c.name === 'Elite definition' || c.name === 'Epic definition');
      if (eliteDef) { eliteDef.name = 'Epic definition'; eliteDef.characters = 'EPIC / 3 looks per lineage + faction cape'; }
      const eliteKey = key.children.find((c) => c.name === 'Elite key'); if (eliteKey) eliteKey.name = 'Epic key';
      let legKey = key.children.find((c) => c.name === 'God key' || c.name === 'Legendary key');
      let legDef = key.children.find((c) => c.name === 'God definition' || c.name === 'Legendary definition');
      const term = key.children.find((c) => c.name === 'Repository terminology');
      if (!legKey && eliteKey && eliteDef) {
        legKey = eliteKey.clone(); legDef = eliteDef.clone();
        const at = term ? key.children.indexOf(term) : key.children.length;
        key.insertChild(at, legKey); key.insertChild(at + 1, legDef);
      }
      if (legKey) { legKey.name = 'Legendary key'; legKey.fills = solid(C.legend); }
      if (legDef) { legDef.name = 'Legendary definition'; legDef.characters = 'LEGENDARY / God levels G1–G12 + faction cape'; }
      if (term) term.characters = 'Epic was “Elite”; Legendary is the God-level tier';
    }
  }
  const placement = titleRow.findOne((n) => n.type === 'TEXT' && n.name === 'Level and placement guide');
  if (placement) placement.characters = 'Level chips share one look within their group; only the listed M-levels and G-levels apply. Put the 1920 × 1080 concept sheet in the big dashed frame, the pieces in the six piece slots and the set’s three weapons in the weapon slots. Each faction’s cape goes in the band that spans both lineages.';
  const inv = titleRow.findOne((n) => n.type === 'TEXT' && n.name === 'Inventory');
  if (inv) inv.characters = '3 factions / 6 lineages / 60 shared looks / 6 faction capes';
  const title = titleRow.findOne((n) => n.type === 'TEXT' && n.name === 'Title');
  if (title) title.textAutoResize = 'WIDTH_AND_HEIGHT';
}

// ---- small builders ---------------------------------------------------------------------------------
function setLabel(slot, chars) { const l = slot.children.find((c) => c.type === 'TEXT' && c.name === 'Art target label'); if (l) l.characters = chars; }
function imageArea(w, h) { const a = figma.createFrame(); a.name = 'Image area ' + w + '×' + h; a.resize(w, h); a.fills = solid(C.cream); a.clipsContent = true; return a; }
function fitInto(img, area) {
  const s = Math.min(area.width / img.width, area.height / img.height);
  const w = Math.round(img.width * s), h = Math.round(img.height * s);
  area.appendChild(img); img.resize(w, h); img.x = Math.round((area.width - w) / 2); img.y = Math.round((area.height - h) / 2);
}
function text(chars, font, size, hex, opts = {}) {
  const t = figma.createText(); t.fontName = font; t.characters = chars; t.fontSize = size; t.fills = solid(hex);
  if (opts.width) { t.resize(opts.width, t.height); t.textAutoResize = 'HEIGHT'; }
  if (opts.name) t.name = opts.name;
  if (opts.align) t.textAlignHorizontal = opts.align;
  return t;
}
function col(name, opts = {}) {
  const f = figma.createFrame(); f.name = name; f.layoutMode = opts.dir || 'VERTICAL'; f.itemSpacing = opts.gap || 0;
  f.paddingLeft = f.paddingRight = opts.px || 0; f.paddingTop = f.paddingBottom = opts.py || 0;
  f.fills = opts.fill ? solid(opts.fill) : [];
  if (opts.stroke) { f.strokes = solid(opts.stroke); f.strokeWeight = opts.strokeW || 3; }
  if (opts.align) f.primaryAxisAlignItems = opts.align; if (opts.cross) f.counterAxisAlignItems = opts.cross;
  f.primaryAxisSizingMode = 'AUTO'; f.counterAxisSizingMode = 'AUTO';
  f.clipsContent = false; return f;
}
// fixed width, height that hugs the content (resize first: it makes both axes fixed)
function setW(node, w) { node.resize(w, Math.max(1, node.height)); node.layoutSizingHorizontal = 'FIXED'; node.layoutSizingVertical = 'HUG'; return node; }
function chip(label, hex) { const c = col('Chip', { dir: 'HORIZONTAL', px: 16, py: 8, fill: hex, stroke: C.ink, strokeW: 1 }); c.appendChild(text(label, F.bold, 20, C.ink)); return c; }
function dashedSlot(proto, name, label, imgW, imgH) {
  const slot = proto.clone(); slot.name = name; slot.itemSpacing = 8;
  slot.resize(imgW + 32, slot.height); slot.layoutSizingVertical = 'HUG';
  const l = slot.children.find((c) => c.type === 'TEXT' && c.name === 'Art target label'); if (l) { l.characters = label; l.layoutSizingHorizontal = 'FILL'; }
  slot.insertChild(1, imageArea(imgW, imgH));
  return slot;
}
function fitRim(box) { const rim = box.children.find((c) => c.name === 'Rarity rim'); if (rim) rim.resize(rim.width, box.height); }
function heading(name, status) {
  const h = col('Look heading', { dir: 'HORIZONTAL', align: 'SPACE_BETWEEN', cross: 'CENTER' });
  h.appendChild(text(name, F.head, 36, C.ink, { name: 'Scheme name' })); h.appendChild(text(status, F.body, 18, C.grey, { name: 'Section status' })); return h;
}
function sectionBand(width, title, direction, invTop, invBottom, bandHex, accentHex, accentProto) {
  const band = col('Section identity', { dir: 'HORIZONTAL', px: 32, py: 20, fill: bandHex, align: 'SPACE_BETWEEN', cross: 'CENTER' });
  band.clipsContent = true; setW(band, width);
  if (accentProto) { // behind the text, at the right-hand end
    const accent = accentProto.clone(); band.insertChild(0, accent); accent.layoutPositioning = 'ABSOLUTE';
    accent.fills = solid(accentHex); accent.y = 0; accent.x = width - accent.width + 95;
  }
  const left = col('Section title'); left.appendChild(text(title, F.title, 100, C.ink)); left.appendChild(text(direction, F.bold, 20, C.ink)); band.appendChild(left);
  const right = col('Section inventory', { gap: 8, cross: 'MAX' }); right.appendChild(text(invTop, F.head, 36, C.ink)); right.appendChild(text(invBottom, F.label, 20, C.ink)); band.appendChild(right);
  return band;
}
function rowBox(name, rimHex, width) {
  const box = col(name, { gap: 16, px: 24, py: 24, fill: C.paper, stroke: C.ink, strokeW: 3 }); setW(box, width);
  const rim = figma.createRectangle(); rim.name = 'Rarity rim'; rim.fills = solid(rimHex); box.appendChild(rim);
  rim.layoutPositioning = 'ABSOLUTE'; rim.x = 0; rim.y = 0; rim.resize(8, 100);
  return box;
}
function table(name, cols, rows) {
  const t = col(name);
  const mkRow = (cells, isHead) => {
    const r = col(isHead ? 'Header row' : 'Row', { dir: 'HORIZONTAL' });
    cells.forEach((cell, i) => {
      const w = cols[i].w;
      const f = col('Cell', { px: 10, py: 8, fill: isHead ? C.panel : C.paper, stroke: C.dash, strokeW: 1 });
      setW(f, w);
      const hot = !isHead && MAXES.includes(cell) && (i - 3) % 4 === 3;
      f.appendChild(text(cell, isHead || i < 2 ? F.label : F.body, 18, hot ? C.red : (cell === 'Rare' ? '#b8930f' : cell === 'Epic' ? '#7b4fc4' : C.ink), { width: w - 20, align: i < 3 ? 'LEFT' : 'CENTER' }));
      r.appendChild(f);
    });
    return r;
  };
  t.appendChild(mkRow(cols.map((c) => c.label), true));
  rows.forEach((cells) => t.appendChild(mkRow(cells, false)));
  return t;
}
function section(name, width) { return setW(col(name, { gap: 32, px: 32, py: 32, fill: C.panel, stroke: C.ink, strokeW: 6 }), width); }

// ---- the pets & mounts board --------------------------------------------------------------------------
function buildPetsAndMountsBoard(proto, accentProto, dotsProto) {
  const board = setW(col('Pets and Mounts', { gap: 64, px: 128, py: 128, fill: C.cream }), PET_BOARD_W);
  const inner = PET_BOX_W;

  // title row
  const titleRow = col('Atlas title and board guide', { dir: 'HORIZONTAL', gap: 80 });
  board.appendChild(titleRow); setW(titleRow, PET_SEC_W);
  const title = col('Atlas title', { gap: 12 });
  title.appendChild(text('KRYTICAL NOVA II / WORLD ATLAS', F.bold, 28, C.ink));
  title.appendChild(text('PETS & MOUNTS / ART BREAKDOWN', F.title, 144, C.ink));
  const inv = col('Board inventory', { dir: 'HORIZONTAL', gap: 32, cross: 'CENTER' });
  inv.appendChild(text('11 reference pets / 8 new pets × 5 evolutions / 4 mounts', F.label, 28, C.ink));
  const divider = figma.createRectangle(); divider.resize(2, 32); divider.fills = solid(C.ink); inv.appendChild(divider);
  inv.appendChild(text('All sections awaiting art', F.body, 24, C.grey));
  title.appendChild(inv); titleRow.appendChild(title);
  const guide = col('Board guide', { gap: 18, px: 32, py: 32, fill: C.ink });
  titleRow.appendChild(guide); guide.layoutSizingHorizontal = 'FILL';
  const gh = col('Guide heading', { dir: 'HORIZONTAL', align: 'SPACE_BETWEEN' });
  guide.appendChild(gh); gh.layoutSizingHorizontal = 'FILL';
  gh.appendChild(text('HOW TO USE THIS BOARD', F.head, 36, C.paper)); gh.appendChild(text('PRODUCTION ART / EDITABLE', F.bold, 18, C.cyan));
  for (const [s, size, hex] of [
    ['Reference tables → pet box (stat row, 1920 × 1080 reference image) → five evolution boxes. Mount → one 1920 × 1080 target.', 26, C.paper],
    ['Every new pet box carries one stat layout from the Twelve Sky 2 tables: its rarity, level requirement and bonus at 25 / 50 / 75 / 100 % growth, red at max. Put the pet’s big reference sheet in the 1920 × 1080 frame and one look per growth stage in the five evolution boxes (960 × 540: a 1920 × 1080 sheet drops in at 50 %).', 24, C.cyanPale]]) {
    const t = text(s, F.body, size, hex, { width: 1000 }); guide.appendChild(t); t.layoutSizingHorizontal = 'FILL';
  }
  const key = col('Key', { dir: 'HORIZONTAL', gap: 36, cross: 'CENTER' });
  for (const [hex, s] of [[C.yellow, 'RARE / Adept 1+'], [C.purple, 'EPIC / Adept 15+ and 30+'], [C.cyan, 'MOUNTS / 4 targets']]) {
    const k = figma.createRectangle(); k.resize(20, 20); k.fills = solid(hex); key.appendChild(k); key.appendChild(text(s, F.label, 22, C.paper));
  }
  key.appendChild(text('Force = Flux (Chi in code) • Adept = player level tier', F.body, 18, C.cyanPale));
  guide.appendChild(key);

  // reference tables
  const ref = section('Pet reference', PET_SEC_W); board.appendChild(ref);
  ref.appendChild(sectionBand(inner, 'PET REFERENCE', 'TWELVE SKY 2 PET SHEETS / TRANSCRIBED', 'RARE ×6 / EPIC ×4 / EPIC ×1', '11 PETS / 3 SHEETS', C.cyanPale, C.cyan, accentProto));
  ref.appendChild(text('Bonus to the owner at 25 / 50 / 75 / 100 % growth; red is the max. Additional Attack applies from 125 % growth on. Flux is the Krytical Nova name for Force.', F.body, 22, C.grey, { width: inner }));
  REF.forEach((sheet, i) => {
    const box = rowBox('Reference sheet ' + (i + 1), sheet.rarity === 'Rare' ? C.yellow : C.purple, inner); ref.appendChild(box);
    const h = heading(sheet.rarity.toUpperCase() + ' • ' + sheet.req.toUpperCase(), sheet.pets.length + ' pets'); box.appendChild(h); h.layoutSizingHorizontal = 'FILL';
    box.appendChild(table('Stats', STAT_COLS, sheet.pets.map(([n, hp, fo, at, de]) => [sheet.rarity, n, sheet.req, ...hp, ...fo, ...at, ...de, ...ADD])));
    fitRim(box);
  });

  // eight new pets
  const pets = section('Pets', PET_SEC_W); board.appendChild(pets);
  pets.appendChild(sectionBand(inner, 'NEW PETS', 'EIGHT PETS / STAT LAYOUT + REFERENCE IMAGE + FIVE EVOLUTIONS', 'PET 01 – PET 08', '8 PETS / 48 ART TARGETS', C.cyan, C.cyanPale, accentProto));
  pets.appendChild(text('Each box has its stat layout filled in from the reference tables. Name the pet, drop its 1920 × 1080 reference sheet in the big frame, then one look per growth stage in the five evolution boxes.', F.body, 22, C.grey, { width: inner }));
  NEW_PET_STATS.forEach(([rarity, req, hp, fo, at, de], i) => {
    const n = String(i + 1).padStart(2, '0');
    const box = rowBox('Pet ' + n, rarity === 'Rare' ? C.yellow : C.purple, inner); pets.appendChild(box);
    const h = heading('PET ' + n + ' • NAME TBD', 'Awaiting art'); box.appendChild(h); h.layoutSizingHorizontal = 'FILL';
    const chips = col('Chips', { dir: 'HORIZONTAL', gap: 8 });
    chips.appendChild(chip(rarity.toUpperCase(), rarity === 'Rare' ? C.yellow : C.purplePale));
    chips.appendChild(chip(req.toUpperCase(), C.paper));
    const grows = [['HEALTH', hp], ['FLUX', fo], ['ATTACK', at], ['DEFENSE', de]].filter(([, v]) => v !== NONE).map(([s]) => s);
    chips.appendChild(chip(grows.join(' + '), C.paper));
    box.appendChild(chips);
    const body = col('Reference and stats', { dir: 'HORIZONTAL', gap: 24 });
    body.appendChild(dashedSlot(proto, 'Reference image', 'Reference image • 1920 × 1080', IMG_W, IMG_H));
    const side = col('Stat layout', { gap: 12 });
    side.appendChild(text('STATS', F.head, 28, C.ink));
    side.appendChild(table('Stats', STAT_COLS, [[rarity, 'Pet ' + n + ' (TBD)', req, ...hp, ...fo, ...at, ...de, ...ADD]]));
    side.appendChild(text('Description and silhouette source (Synty POLYGON cluster).', F.body, 18, C.grey, { width: 1400, name: 'Pet description' }));
    body.appendChild(side);
    box.appendChild(body);
    const evo = col('Evolution slots', { dir: 'HORIZONTAL', gap: 24 });
    ['Stage 1 • Baseline (0 %)', 'Stage 2 • 25 %', 'Stage 3 • 50 %', 'Stage 4 • 75 %', 'Stage 5 • 100 %'].forEach((lbl, k) => evo.appendChild(dashedSlot(proto, 'Stage ' + (k + 1), lbl, EVO_IMG_W, EVO_IMG_H)));
    box.appendChild(evo);
    fitRim(box);
  });

  // mounts
  const mounts = section('Mounts', PET_SEC_W); board.appendChild(mounts);
  mounts.appendChild(sectionBand(inner, 'MOUNTS', 'MOBILITY + PASSIVE STATS / RIDE IT OR ABSORB IT', 'FOUR MOUNTS / NAMES TO BE DECIDED', '4 MOUNTS / 4 LOOKS', C.yellow, C.mint, accentProto));
  mounts.appendChild(text('A mount is movement and a stat system in one: base abilities rolled on acquisition, an extra ability on top, re-rolled to optimise. Legendary mounts are crafted from a mount that has reached 15 %. An absorbed mount disappears from view but keeps its abilities and adds +10. Some carry a special skill: a survival effect or a multi-target attack.', F.body, 22, C.grey, { width: inner }));
  const mbox = rowBox('Mount roster', C.yellow, inner); mounts.appendChild(mbox);
  const mh = heading('MOUNT ROSTER • 01–04', 'Awaiting art'); mbox.appendChild(mh); mh.layoutSizingHorizontal = 'FILL';
  const ml = col('Chips', { dir: 'HORIZONTAL', gap: 8 });
  ml.appendChild(chip('NORMAL → LEGENDARY', C.yellow)); ml.appendChild(chip('BASE ROLL + EXTRA ROLL', C.paper)); ml.appendChild(chip('ABSORB +10', C.paper));
  mbox.appendChild(ml);
  const mgrid = col('Mount slots', { gap: 24 });
  for (let r = 0; r < 2; r++) {
    const mrow = col('Mount row', { dir: 'HORIZONTAL', gap: 24 });
    for (let i = r * 2 + 1; i <= r * 2 + 2; i++) mrow.appendChild(dashedSlot(proto, 'Mount ' + i, 'Mount 0' + i + ' • name TBD • 1920 × 1080', IMG_W, IMG_H));
    mgrid.appendChild(mrow);
  }
  mbox.appendChild(mgrid);
  fitRim(mbox);

  if (dotsProto) for (const sec of [ref, pets, mounts]) { const d = dotsProto.clone(); sec.appendChild(d); d.layoutPositioning = 'ABSOLUTE'; d.x = 1888; d.y = 200; }
  const note = col('Source and production notes', { dir: 'HORIZONTAL', gap: 64 });
  note.appendChild(text('Pet reference: Twelve Sky 2 pet sheets (Rare, Epic Adept 15, Epic Adept 30) • Mounts: 05_mounts.md', F.body, 24, C.grey));
  note.appendChild(text('MATTE / FACETED / INKED / HALFTONE SHADOWS', F.body, 24, C.ink));
  board.appendChild(note);
  return board;
}

main().then((r) => { console.log('World Atlas board done', r); figma.closePlugin('World Atlas updated: ' + r.factions.join(', ') + ' • capes, Epic / Legendary tiers, pets & mounts board'); })
  .catch((e) => { console.error(e); figma.closePlugin('World Atlas script failed: ' + e.message); });
