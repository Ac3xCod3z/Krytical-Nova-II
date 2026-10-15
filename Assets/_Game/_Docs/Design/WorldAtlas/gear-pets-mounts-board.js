// Krytical Nova II — World Atlas (Figma) — page "Gear, pet, mount Concept art"
// Run inside Figma with that file open (README.md next to this file says how).
//
// What it does:
//   GEAR (every lineage column, every Rare / Elite look box)
//     • the concept-sheet frame gets a 1920 × 1080 image area; a sheet already in the box
//       is moved in and scaled to fit
//     • the six piece slots stay; three weapon slots are added beside them
//     • a FACTION CAPES section (3 image slots) goes between Rare and Elite
//     • a GOD section with three sets (G1–G4, G5–G8, G9–G12) goes after Elite
//     • columns, faction panels, title row and board grow to fit; loose sheets move down
//   PETS & MOUNTS (new board to the right of the gear board, same decoration)
//     • the three Twelve Sky 2 pet reference tables, transcribed
//     • eight new pet boxes, each with a 1920 × 1080 reference image slot, a stat strip and
//       five evolution slots (0 / 25 / 50 / 75 / 100 %)
//     • a mount roster with four 1920 × 1080 slots
//
// Safe to run twice: everything it adds is looked up by name before it is created.

const PAGE_NAME = 'Gear, pet, mount Concept art';

// ---- palette (the gear board's own) --------------------------------------------------
const C = {
  ink: '#14122b', cream: '#f0ebdd', paper: '#fffef8', panel: '#e5e1d8', grey: '#696578',
  dash: '#a9a3b4', cyan: '#4fe3f0', cyanPale: '#dce8f0', yellow: '#f2d35b', purple: '#b591ec',
  god: '#ff7a3d', godPale: '#ffc49c', dots: '#4c52bf', mint: '#fff6c4',
};
function rgb(hex) { const n = parseInt(hex.slice(1), 16); return { r: ((n >> 16) & 255) / 255, g: ((n >> 8) & 255) / 255, b: (n & 255) / 255 }; }
const solid = (hex) => [{ type: 'SOLID', color: rgb(hex) }];

// ---- sizes ---------------------------------------------------------------------------------
const BOX_W = 2000, INNER_W = 1952, LIN_W = 2048, IMG_W = 1920, IMG_H = 1080;
const PIECE_GRID_W = 528, PIECE_ROW_H = 204;
const WEAPON_H = PIECE_ROW_H * 3 + 24 * 2;          // 660: as tall as the piece grid
const CAPE_IMG_W = 624, CAPE_IMG_H = 351;             // 1920 × 1080 at 32.5 %
const FACTION_W = 32 + LIN_W * 2 + 32 + 32;           // 4192
const FACTION_GAP = 354, FACTIONS_PAD = 129;
const PET_INNER_W = 2800;                             // reference table width
const MOUNT_IMG_W = 1920, MOUNT_IMG_H = 1080;
const EVO_IMG_W = 960, EVO_IMG_H = 540, PET_ROW_W = (EVO_IMG_W + 32) * 5 + 24 * 4; // five evolution slots across

const F = {
  label: { family: 'Inter', style: 'Semi Bold' }, body: { family: 'Inter', style: 'Regular' },
  bold: { family: 'Inter', style: 'Bold' }, head: { family: 'Barlow Condensed', style: 'Bold' },
  title: { family: 'Barlow Condensed', style: 'ExtraBold' },
};

async function main() {
  for (const f of Object.values(F)) await figma.loadFontAsync(f);
  const page = figma.root.children.find((p) => p.name === PAGE_NAME);
  if (!page) throw new Error('Page not found: ' + PAGE_NAME);
  await figma.setCurrentPageAsync(page);
  const factions = page.children.find((n) => n.name === 'Factions' && n.type === 'FRAME');
  if (!factions) throw new Error('Frame "Factions" not found on the page');
  const report = { gearBoxes: 0, sheetsMoved: 0, weaponRows: 0, capeSections: 0, godSections: 0 };

  // a dashed art frame to clone for every new slot
  let proto = factions.findOne((n) => n.type === 'FRAME' && n.name === 'Armor' && n.parent && n.parent.name === 'Piece art row');
  if (!proto) throw new Error('No dashed "Armor" frame found to use as a template');
  proto = proto.clone();
  page.appendChild(proto);

  // ================= 1. GEAR ============================================================
  const factionFrames = factions.children
    .filter((n) => n.type === 'FRAME' && n.children.some((c) => c.name === 'Faction identity'))
    .sort((a, b) => a.x - b.x);

  for (const faction of factionFrames) {
    const factionName = (faction.findOne((n) => n.type === 'TEXT' && n.name === 'Faction name') || { characters: faction.name }).characters;
    const lineages = faction.children
      .filter((n) => n.type === 'FRAME' && n.children.some((c) => c.name === 'Lineage identity'))
      .sort((a, b) => a.x - b.x);

    for (const lineage of lineages) {
      const boxes = lineage.findAll((n) => n.type === 'FRAME' && /^(Rare|Elite|God) • /.test(n.name));
      for (const box of boxes) convertLookBox(box, proto, report);

      lineage.resize(LIN_W, lineage.height);
      for (const c of lineage.children) if (['Lineage identity', 'Rare', 'Elite', 'God', 'Faction capes'].includes(c.name)) c.layoutSizingHorizontal = 'FILL';
      const ident = lineage.children.find((c) => c.name === 'Lineage identity');
      if (ident) for (const c of ident.children) c.layoutSizingHorizontal = 'FILL';
      for (const sec of ['Rare', 'Elite']) { const s = lineage.children.find((c) => c.name === sec); if (s) for (const c of s.children) c.layoutSizingHorizontal = 'FILL'; }

      const marker = lineage.findOne((n) => n.type === 'RECTANGLE' && n.name === 'Faction marker');
      const factionHex = marker && marker.fills.length && marker.fills[0].type === 'SOLID' ? toHex(marker.fills[0].color) : C.cyan;
      addCapeSection(lineage, factionName, factionHex, proto, report);
      addGodSection(lineage, proto, report);

      const dots = lineage.children.find((c) => c.name === 'Panel edge dot shadow');
      if (dots) { dots.x = Math.round((LIN_W - dots.width) / 2); dots.y = lineage.height - 184; }
    }

    let maxBottom = 0;
    lineages.forEach((l, i) => { l.x = 32 + i * (LIN_W + 32); l.y = 232; maxBottom = Math.max(maxBottom, l.y + l.height); });
    const identity = faction.children.find((c) => c.name === 'Faction identity');
    if (identity) {
      identity.resize(FACTION_W - 64, identity.height);
      const accent = identity.children.find((c) => c.name === 'Faceted header accent');
      if (accent) accent.x = identity.width - accent.width + 95;
      const lookCount = identity.findOne((n) => n.type === 'TEXT' && n.name === 'Look count');
      if (lookCount) lookCount.characters = lineages.length + ' LINEAGES / ' + (lineages.length * 10) + ' LOOKS / CAPES';
    }
    const refs = faction.children.find((c) => c.name === 'Faction weapon references');
    if (refs) { refs.y = maxBottom + 32; refs.resize(FACTION_W - 64, refs.height); for (const c of refs.children) c.layoutSizingHorizontal = 'FILL'; maxBottom = refs.y + refs.height; }
    faction.resize(FACTION_W, maxBottom + 32);
  }

  let factionBottom = 0;
  factionFrames.forEach((f, i) => { f.x = FACTIONS_PAD + i * (FACTION_W + FACTION_GAP); factionBottom = Math.max(factionBottom, f.y + f.height); });
  const boardW = FACTIONS_PAD * 2 + FACTION_W * 3 + FACTION_GAP * 2;
  const notes = factions.children.find((c) => c.name === 'Source and production notes');
  if (notes) {
    notes.y = factionBottom + 100; notes.resize(boardW - 128, notes.height);
    const scope = notes.children.find((c) => c.name === 'Scope'); if (scope) scope.x = notes.width - scope.width;
    const art = notes.children.find((c) => c.name === 'Art language'); if (art) art.x = Math.round((notes.width - art.width) / 2);
  }
  const titleRow = factions.children.find((c) => c.name === 'Atlas title and board guide');
  if (titleRow) {
    titleRow.resize(boardW - 2 * 261, titleRow.height);
    const guide = titleRow.children.find((c) => c.name === 'Board guide');
    if (guide) {
      guide.resize(titleRow.width - 3260 - 80, guide.height);
      for (const c of guide.children) c.layoutSizingHorizontal = 'FILL';
      const heading = guide.children.find((c) => c.name === 'Guide heading');
      if (heading) for (const c of heading.children) if (c.type === 'TEXT') c.layoutSizingHorizontal = 'HUG';
      const key = guide.children.find((c) => c.name === 'Rarity key');
      if (key && !key.children.some((c) => c.name === 'God key')) {
        const eliteKey = key.children.find((c) => c.name === 'Elite key');
        const eliteDef = key.children.find((c) => c.name === 'Elite definition');
        const term = key.children.find((c) => c.name === 'Repository terminology');
        if (eliteKey && eliteDef) {
          const k = eliteKey.clone(); k.name = 'God key'; k.fills = solid(C.god);
          const d = eliteDef.clone(); d.name = 'God definition'; d.characters = 'GOD / 3 looks per lineage (G1–G12)';
          const at = term ? key.children.indexOf(term) : key.children.length;
          key.insertChild(at, k); key.insertChild(at + 1, d);
        }
      }
    }
    const placement = titleRow.findOne((n) => n.type === 'TEXT' && n.name === 'Level and placement guide');
    if (placement) placement.characters = 'Level chips share one look within their group; only the listed M-levels and G-levels apply. Place the 1920 × 1080 concept sheet in the big dashed frame, the pieces in the six piece slots, and the set’s three weapons in the three weapon slots. Each faction’s three capes go in the capes strip between Rare and Elite.';
    const inv = titleRow.findOne((n) => n.type === 'TEXT' && n.name === 'Inventory');
    if (inv) inv.characters = '3 factions / 6 lineages / 60 shared looks / 3 capes each';
  }
  const oldBottom = factions.y + factions.height;
  factions.resize(boardW, (notes ? notes.y + notes.height : factionBottom) + 64);
  const newBottom = factions.y + factions.height;
  const loose = page.children.filter((n) => n !== factions && n.type === 'RECTANGLE' && n.y > oldBottom - 1200 && n.x + n.width > factions.x && n.x < factions.x + boardW);
  if (loose.length) { const minY = Math.min(...loose.map((n) => n.y)); const shift = newBottom + 400 - minY; if (shift > 0) for (const n of loose) n.y += shift; }

  // ================= 2. PETS & MOUNTS ====================================================
  const hegemonyIdentity = factionFrames[0] && factionFrames[0].children.find((c) => c.name === 'Faction identity');
  const accentProto = hegemonyIdentity && hegemonyIdentity.children.find((c) => c.name === 'Faceted header accent');
  const dotsProto = factionFrames[0] && factionFrames[0].children.find((c) => c.name === 'Faction header dot shadow');
  let board = page.children.find((n) => n.name === 'Pets and Mounts' && n.type === 'FRAME');
  if (!board) { board = buildPetsAndMountsBoard(proto, accentProto, dotsProto); page.appendChild(board); }
  board.x = factions.x + factions.width + 600; board.y = factions.y;

  proto.remove();
  report.gearBoard = { w: Math.round(factions.width), h: Math.round(factions.height) };
  report.petsBoard = { id: board.id, w: Math.round(board.width), h: Math.round(board.height) };
  return report;
}

function toHex(c) { return '#' + [c.r, c.g, c.b].map((v) => Math.round(v * 255).toString(16).padStart(2, '0')).join(''); }

// ---- one gear look box: 1920 × 1080 sheet area, piece slots kept, three weapon slots added --
function convertLookBox(box, proto, report) {
  report.gearBoxes++;
  box.resize(BOX_W, box.height);
  box.primaryAxisSizingMode = 'AUTO';
  for (const c of box.children) if (c.name === 'Look heading' || c.name === 'Shared-look levels') c.layoutSizingHorizontal = 'FILL';
  const areas = box.children.find((c) => c.name === 'Art insertion areas');
  if (!areas) return;
  areas.layoutMode = 'VERTICAL'; areas.itemSpacing = 24; areas.layoutSizingHorizontal = 'FILL'; areas.layoutSizingVertical = 'HUG';

  const full = areas.children.find((c) => c.name === 'Full set / concept art');
  if (full) {
    full.resize(INNER_W, full.height); full.itemSpacing = 8; full.primaryAxisSizingMode = 'AUTO'; full.counterAxisSizingMode = 'FIXED';
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

function setLabel(slot, chars) { const l = slot.children.find((c) => c.type === 'TEXT' && c.name === 'Art target label'); if (l) l.characters = chars; }
function imageArea(w, h) {
  const a = figma.createFrame(); a.name = 'Image area ' + w + '×' + h; a.resize(w, h); a.fills = solid(C.cream); a.clipsContent = true; return a;
}
function fitInto(img, area) {
  const s = Math.min(area.width / img.width, area.height / img.height);
  const w = Math.round(img.width * s), h = Math.round(img.height * s);
  area.appendChild(img); img.resize(w, h); img.x = Math.round((area.width - w) / 2); img.y = Math.round((area.height - h) / 2);
}

// ---- capes strip between Rare and Elite ------------------------------------------------------
function addCapeSection(lineage, factionName, factionHex, proto, report) {
  if (lineage.children.some((c) => c.name === 'Faction capes')) return;
  const elite = lineage.children.find((c) => c.name === 'Elite');
  const sec = col('Faction capes', { gap: 16, px: 24, py: 24, fill: C.paper, stroke: C.ink, strokeW: 3 });
  sec.counterAxisSizingMode = 'FIXED'; sec.resize(BOX_W, sec.height);
  const rim = figma.createRectangle(); rim.name = 'Rarity rim'; rim.fills = solid(factionHex); sec.appendChild(rim);
  rim.layoutPositioning = 'ABSOLUTE'; rim.x = 0; rim.y = 0; rim.resize(8, 100);
  sec.appendChild(heading('FACTION CAPES • ' + factionName.toUpperCase(), 'Awaiting art'));
  sec.appendChild(text('Three cape images for the ' + factionName + ' faction. Worn over any Rare or Elite set; the same three capes for both of the faction’s lineages. Each image is 1920 × 1080, shown here at a third.', F.body, 18, C.grey, { width: INNER_W, name: 'Cape note' }));
  const row = col('Cape slots', { dir: 'HORIZONTAL', gap: 24 });
  for (let i = 1; i <= 3; i++) row.appendChild(dashedSlot(proto, 'Cape ' + i, 'Cape ' + i + ' • 1920 × 1080', CAPE_IMG_W, CAPE_IMG_H));
  sec.appendChild(row);
  lineage.insertChild(elite ? lineage.children.indexOf(elite) : lineage.children.length, sec);
  sec.layoutSizingHorizontal = 'FILL';
  for (const c of sec.children) if (c.type !== 'RECTANGLE') fillW(c);
  fitRim(sec);
  report.capeSections++;
}

// ---- God section: a copy of Elite with three sets G1–G4, G5–G8, G9–G12 -------------------------
function addGodSection(lineage, proto, report) {
  if (lineage.children.some((c) => c.name === 'God')) return;
  const elite = lineage.children.find((c) => c.name === 'Elite');
  if (!elite) return;
  const god = elite.clone();
  god.name = 'God';
  lineage.insertChild(lineage.children.indexOf(elite) + 1, god);
  god.layoutSizingHorizontal = 'FILL';
  // heading
  const rarity = god.findOne((n) => n.type === 'TEXT' && n.name === 'Rarity'); if (rarity) rarity.characters = 'GOD';
  const count = god.findOne((n) => n.type === 'TEXT' && n.name === 'Art set count'); if (count) count.characters = '3 SHARED LOOKS / 12 LEVEL LABELS';
  const marker = god.findOne((n) => n.type === 'RECTANGLE' && n.name === 'Rarity marker'); if (marker) marker.fills = solid(C.god);
  // sets
  const sets = [['G1–G4', ['G1', 'G2', 'G3', 'G4']], ['G5–G8', ['G5', 'G6', 'G7', 'G8']], ['G9–G12', ['G9', 'G10', 'G11', 'G12']]];
  const boxes = god.findAll((n) => n.type === 'FRAME' && /^(Elite|God) • /.test(n.name));
  boxes.forEach((box, i) => {
    const set = sets[i] || sets[sets.length - 1];
    box.name = 'God • ' + set[0];
    const name = box.findOne((n) => n.type === 'TEXT' && n.name === 'Scheme name'); if (name) name.characters = 'God • ' + set[0];
    const rim = box.children.find((c) => c.name === 'Rarity rim'); if (rim) rim.fills = solid(C.god);
    const chips = box.findOne((n) => n.type === 'FRAME' && n.name === 'Level chips');
    if (chips && chips.children.length) {
      const tpl = chips.children[0].clone();
      for (const c of [...chips.children]) c.remove();
      for (const lvl of set[1]) {
        const chip = tpl.clone(); chips.appendChild(chip); chip.fills = solid(C.godPale);
        const t = chip.findOne((n) => n.type === 'TEXT'); if (t) t.characters = lvl;
      }
      tpl.remove();
    }
    const interval = box.findOne((n) => n.type === 'TEXT' && n.name === 'Level interval'); if (interval) interval.characters = '+1 level steps / shared look';
    for (const s of box.findAll((n) => n.type === 'TEXT' && (n.name === 'Section status' || n.name === 'Art status'))) s.characters = 'Awaiting art';
    for (const img of box.findAll((n) => n.type === 'RECTANGLE' && n.fills.length && n.fills[0].type === 'IMAGE')) img.remove();
  });
  while (boxes.length > 3) boxes.pop().remove();
  for (let i = boxes.length; i < 3; i++) {
    const extra = boxes[0].clone(); boxes[0].parent.appendChild(extra); boxes.push(extra);
    extra.name = 'God • ' + sets[i][0];
    const name = extra.findOne((n) => n.type === 'TEXT' && n.name === 'Scheme name'); if (name) name.characters = 'God • ' + sets[i][0];
    const chips = extra.findOne((n) => n.type === 'FRAME' && n.name === 'Level chips');
    if (chips) chips.children.forEach((chip, k) => { const t = chip.findOne((n) => n.type === 'TEXT'); if (t) t.characters = sets[i][1][k] || ''; });
  }
  report.godSections++;
}

// ---- small builders ----------------------------------------------------------------------------
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
  f.primaryAxisSizingMode = 'AUTO'; f.counterAxisSizingMode = 'AUTO'; f.fills = opts.fill ? solid(opts.fill) : [];
  if (opts.stroke) { f.strokes = solid(opts.stroke); f.strokeWeight = opts.strokeW || 3; }
  if (opts.align) f.primaryAxisAlignItems = opts.align; if (opts.cross) f.counterAxisAlignItems = opts.cross;
  f.clipsContent = false; return f;
}
function fillW(node) {
  if (node.type === 'TEXT' && node.textAutoResize === 'WIDTH_AND_HEIGHT') { node.resize(Math.max(node.width, 200), node.height); node.textAutoResize = 'HEIGHT'; }
  node.layoutSizingHorizontal = 'FILL'; return node;
}
function chip(label, hex) { const c = col('Chip', { dir: 'HORIZONTAL', px: 16, py: 8, fill: hex, stroke: C.ink, strokeW: 1 }); c.appendChild(text(label, F.bold, 20, C.ink)); return c; }
function dashedSlot(proto, name, label, imgW, imgH) {
  const slot = proto.clone(); slot.name = name; slot.itemSpacing = 8; slot.primaryAxisSizingMode = 'AUTO'; slot.counterAxisSizingMode = 'FIXED'; slot.resize(imgW + 32, slot.height);
  const l = slot.children.find((c) => c.type === 'TEXT' && c.name === 'Art target label'); if (l) { l.characters = label; l.layoutSizingHorizontal = 'FILL'; }
  slot.insertChild(1, imageArea(imgW, imgH));
  return slot;
}
function sectionBand(title, direction, inventoryTop, inventoryBottom, bandHex, accentHex, accentProto) {
  const band = col('Section identity', { dir: 'HORIZONTAL', px: 32, py: 20, fill: bandHex, align: 'SPACE_BETWEEN', cross: 'CENTER' }); band.clipsContent = true;
  const left = col('Section title'); left.appendChild(text(title, F.title, 100, C.ink)); left.appendChild(text(direction, F.bold, 20, C.ink)); band.appendChild(left);
  const right = col('Section inventory', { gap: 8, cross: 'MAX' }); right.appendChild(text(inventoryTop, F.head, 36, C.ink)); right.appendChild(text(inventoryBottom, F.label, 20, C.ink)); band.appendChild(right);
  if (accentProto) { const accent = accentProto.clone(); band.appendChild(accent); accent.layoutPositioning = 'ABSOLUTE'; accent.fills = solid(accentHex); accent.y = 0; accent.x = 2400; }
  return band;
}
function rowBox(name, rimHex, width) {
  const box = col(name, { gap: 16, px: 24, py: 24, fill: C.paper, stroke: C.ink, strokeW: 3 }); box.counterAxisSizingMode = 'FIXED'; box.resize(width, box.height);
  const rim = figma.createRectangle(); rim.name = 'Rarity rim'; rim.fills = solid(rimHex); box.appendChild(rim); rim.layoutPositioning = 'ABSOLUTE'; rim.x = 0; rim.y = 0; rim.resize(8, 100);
  return box;
}
function fitRim(box) { const rim = box.children.find((c) => c.name === 'Rarity rim'); if (rim) rim.resize(8, box.height); }
function heading(name, status) {
  const h = col('Look heading', { dir: 'HORIZONTAL', align: 'SPACE_BETWEEN', cross: 'CENTER' });
  h.appendChild(text(name, F.head, 36, C.ink, { name: 'Scheme name' })); h.appendChild(text(status, F.body, 18, C.grey, { name: 'Section status' })); return h;
}
// a simple table: cols = [{label, w}], rows = [[cell, ...]]
function table(name, cols, rows, opts = {}) {
  const t = col(name, { gap: 0 });
  const mkRow = (cells, isHead) => {
    const r = col(isHead ? 'Header row' : 'Row', { dir: 'HORIZONTAL', gap: 0 });
    cells.forEach((cell, i) => {
      const w = cols[i].w;
      const f = col('Cell', { px: 10, py: 8, fill: isHead ? C.panel : C.paper, stroke: C.dash, strokeW: 1 });
      f.counterAxisSizingMode = 'FIXED'; f.resize(w, f.height);
      const hot = !isHead && /^[0-9,]+$/.test(cell) && cols[i].max && cell.replace(/,/g, '') === String(cols[i].max);
      f.appendChild(text(cell, isHead ? F.label : (i < 2 ? F.label : F.body), opts.fontSize || 18, hot ? '#d8343c' : C.ink, { width: w - 20, align: i < 3 ? 'LEFT' : 'CENTER' }));
      r.appendChild(f);
    });
    return r;
  };
  t.appendChild(mkRow(cols.map((c) => c.label), true));
  rows.forEach((cells) => t.appendChild(mkRow(cells, false)));
  return t;
}

// ---- Twelve Sky 2 pet reference (the three sheets) -------------------------------------------
const HP = ['500', '1,000', '1,500', '2,200'], FORCE = ['450', '900', '1,350', '2,000'], ATK = ['250', '500', '750', '1,100'], DEF = ['500', '1,000', '1,500', '2,200'];
const NONE = ['–', '–', '–', '–'], ADD = ['100', '150', '200', '250'];
const REF = [
  { rarity: 'Rare', req: 'Adept 1 or higher', pets: [
    ['Horn Beetle', NONE, NONE, ATK, DEF], ['Solitary Owl', HP, NONE, ATK, NONE], ['Blood Bat', NONE, FORCE, ATK, NONE],
    ['Ancient Turtle', HP, NONE, NONE, DEF], ['Sacred Kirin', NONE, FORCE, NONE, DEF], ['Tribal Lion', HP, FORCE, NONE, NONE] ] },
  { rarity: 'Epic', req: 'Adept 15 or higher', pets: [
    ['Azure Magpie', HP, NONE, ATK, DEF], ['Flying Hawk', NONE, FORCE, ATK, DEF], ['Crown Seahorse', HP, FORCE, ATK, NONE], ['Tyran Kingbird', HP, FORCE, NONE, DEF] ] },
  { rarity: 'Epic', req: 'Adept 30 or higher', pets: [ ['Mystic Dragon', HP, FORCE, ATK, DEF] ] },
];
const REF_COLS = [{ label: 'Level', w: 120 }, { label: 'Name', w: 260 }, { label: 'Level requirement', w: 220 },
  ...['Health', 'Flux (Force)', 'Attack (ATK)', 'Defense (DEF)'].flatMap((s) => ['25%', '50%', '75%', '100%'].map((p, i) => ({ label: s.split(' ')[0] + ' ' + p, w: 110, max: i === 3 ? (s.startsWith('Health') || s.startsWith('Defense') ? 2200 : s.startsWith('Flux') ? 2000 : 1100) : 0 }))),
  ...['125%', '150%', '175%', '200%'].map((p, i) => ({ label: 'Add. ATK ' + p, w: 110, max: i === 3 ? 250 : 0 }))];

function buildPetsAndMountsBoard(proto, accentProto, dotsProto) {
  const board = col('Pets and Mounts', { gap: 64, px: 128, py: 128, fill: C.cream });
  const rowW = PET_ROW_W + 48;

  // title row
  const titleRow = col('Atlas title and board guide', { dir: 'HORIZONTAL', gap: 80 });
  const title = col('Atlas title', { gap: 12 });
  title.appendChild(text('KRYTICAL NOVA II / WORLD ATLAS', F.bold, 28, C.ink));
  title.appendChild(text('PETS & MOUNTS / ART BREAKDOWN', F.title, 144, C.ink));
  const inv = col('Board inventory', { dir: 'HORIZONTAL', gap: 32, cross: 'CENTER' });
  inv.appendChild(text('11 reference pets / 8 new pets × 5 evolutions / 4 mounts', F.label, 28, C.ink));
  const divider = figma.createRectangle(); divider.resize(2, 32); divider.fills = solid(C.ink); inv.appendChild(divider);
  inv.appendChild(text('All sections awaiting art', F.body, 24, C.grey));
  title.appendChild(inv); titleRow.appendChild(title);
  const guide = col('Board guide', { gap: 18, px: 32, py: 32, fill: C.ink });
  const gh = col('Guide heading', { dir: 'HORIZONTAL', align: 'SPACE_BETWEEN' });
  gh.appendChild(text('HOW TO USE THIS BOARD', F.head, 36, C.paper)); gh.appendChild(text('PRODUCTION ART / EDITABLE', F.bold, 18, C.cyan));
  guide.appendChild(gh); fillW(gh);
  guide.appendChild(text('Reference tables → new pet box (stats, level, one 1920 × 1080 reference image) → five evolution boxes. Mount → one art target.', F.body, 26, C.paper));
  guide.appendChild(text('The three tables are the Twelve Sky 2 pet sheets, transcribed. Each new pet box has its own stat strip to fill from them, a 1920 × 1080 dashed frame for the big reference image, and five evolution boxes under it: baseline, 25, 50, 75 and 100 % growth. The evolution areas are 960 × 540, so a 1920 × 1080 sheet drops in at 50 %.', F.body, 24, C.cyanPale, { width: 1600 }));
  const key = col('Key', { dir: 'HORIZONTAL', gap: 36, cross: 'CENTER' });
  const k1 = figma.createRectangle(); k1.resize(20, 20); k1.fills = solid(C.cyan); key.appendChild(k1);
  key.appendChild(text('PETS / 8 boxes × 5 stages', F.label, 22, C.paper));
  const k2 = figma.createRectangle(); k2.resize(20, 20); k2.fills = solid(C.yellow); key.appendChild(k2);
  key.appendChild(text('MOUNTS / 4 targets', F.label, 22, C.paper));
  key.appendChild(text('Force = Flux (Chi in code) • Adept = player level tier', F.body, 18, C.cyanPale));
  guide.appendChild(key); titleRow.appendChild(guide); board.appendChild(titleRow);
  fillW(titleRow); fillW(guide);
  for (const c of guide.children) if (c.type === 'TEXT') fillW(c);

  // ---- reference tables
  const ref = col('Pet reference', { gap: 32, px: 32, py: 32, fill: C.panel, stroke: C.ink, strokeW: 6 });
  ref.appendChild(sectionBand('PET REFERENCE', 'TWELVE SKY 2 PET SHEETS / TRANSCRIBED', 'RARE ×6 / EPIC ×4 / EPIC ×1', '11 PETS / 3 SHEETS', C.cyanPale, C.cyan, accentProto));
  ref.appendChild(text('Stats are the pet’s bonus to its owner at 25 / 50 / 75 / 100 % growth; the red figure is the max. Additional Attack applies from 125 % on. Flux is the Krytical Nova name for Force.', F.body, 22, C.grey, { width: PET_INNER_W }));
  REF.forEach((sheet, i) => {
    const box = rowBox('Reference sheet ' + (i + 1), i === 0 ? C.yellow : C.purple, rowW);
    box.appendChild(heading(sheet.rarity.toUpperCase() + ' • ' + sheet.req.toUpperCase(), sheet.pets.length + ' pets'));
    const rows = sheet.pets.map(([name, hp, force, atk, def]) => [sheet.rarity, name, sheet.req, ...hp, ...force, ...atk, ...def, ...ADD]);
    box.appendChild(table('Stats', REF_COLS, rows));
    ref.appendChild(box);
    for (const c of box.children) if (c.type !== 'RECTANGLE') fillW(c);
    fitRim(box);
  });
  board.appendChild(ref); for (const c of ref.children) fillW(c);

  // ---- eight new pets
  const pets = col('Pets', { gap: 32, px: 32, py: 32, fill: C.panel, stroke: C.ink, strokeW: 6 });
  pets.appendChild(sectionBand('NEW PETS', 'EIGHT PETS / ONE REFERENCE IMAGE + FIVE EVOLUTIONS EACH', 'PET 01 – PET 08', '8 PETS / 48 ART TARGETS', C.cyan, C.cyanPale, accentProto));
  pets.appendChild(text('One box per pet. Fill the stat strip from the reference tables above, name the pet, and drop its 1920 × 1080 reference sheet in the big dashed frame, then one look per growth stage in the five evolution boxes under it.', F.body, 22, C.grey, { width: PET_INNER_W }));
  const stripCols = [{ label: 'Stat', w: 260 }, { label: '25%', w: 160 }, { label: '50%', w: 160 }, { label: '75%', w: 160 }, { label: '100%', w: 160 }];
  for (let i = 1; i <= 8; i++) {
    const n = String(i).padStart(2, '0');
    const box = rowBox('Pet ' + n, C.cyan, rowW);
    box.appendChild(heading('PET ' + n + ' • NAME TBD', 'Awaiting art'));
    const chips = col('Pet chips', { dir: 'HORIZONTAL', gap: 8, align: 'SPACE_BETWEEN', cross: 'CENTER' });
    const left = col('Chips', { dir: 'HORIZONTAL', gap: 8 });
    left.appendChild(chip('REFERENCE: TBD', C.paper)); left.appendChild(chip('RARITY: TBD', C.paper)); left.appendChild(chip('LEVEL: TBD', C.paper));
    chips.appendChild(left); chips.appendChild(text('Item 80' + n + ' • creature TBD', F.body, 18, C.grey)); box.appendChild(chips);
    const body = col('Reference and stats', { dir: 'HORIZONTAL', gap: 24 });
    body.appendChild(dashedSlot(proto, 'Reference image', 'Reference image • 1920 × 1080', IMG_W, IMG_H));
    const side = col('Stat strip', { gap: 12 });
    side.appendChild(text('STAT STRIP', F.head, 28, C.ink));
    side.appendChild(table('Stats', stripCols, [['Health', '–', '–', '–', '–'], ['Flux (Force)', '–', '–', '–', '–'], ['Attack (ATK)', '–', '–', '–', '–'], ['Defense (DEF)', '–', '–', '–', '–'], ['Add. ATK 125–200%', '–', '–', '–', '–']], { fontSize: 18 }));
    side.appendChild(text('Description, silhouette source (Synty POLYGON cluster) and the one stat it grows.', F.body, 18, C.grey, { width: 900, name: 'Pet description' }));
    body.appendChild(side);
    box.appendChild(body);
    const evo = col('Evolution slots', { dir: 'HORIZONTAL', gap: 24 });
    ['Stage 1 • Baseline (0 %)', 'Stage 2 • 25 %', 'Stage 3 • 50 %', 'Stage 4 • 75 %', 'Stage 5 • 100 %'].forEach((lbl, k) => evo.appendChild(dashedSlot(proto, 'Stage ' + (k + 1), lbl, EVO_IMG_W, EVO_IMG_H)));
    box.appendChild(evo);
    pets.appendChild(box);
    for (const c of box.children) if (c.type !== 'RECTANGLE') fillW(c);
    fitRim(box);
  }
  board.appendChild(pets); for (const c of pets.children) fillW(c);

  // ---- mounts
  const mounts = col('Mounts', { gap: 32, px: 32, py: 32, fill: C.panel, stroke: C.ink, strokeW: 6 });
  mounts.appendChild(sectionBand('MOUNTS', 'MOBILITY + PASSIVE STATS / RIDE IT OR ABSORB IT', 'FOUR MOUNTS / NAMES TO BE DECIDED', '4 MOUNTS / 4 LOOKS', C.yellow, C.mint, accentProto));
  mounts.appendChild(text('A mount is movement and a stat system in one: base abilities rolled on acquisition, an extra ability on top, re-rolled to optimise. Legendary mounts are crafted from a mount that has reached 15 %. An absorbed mount disappears from view but keeps its abilities on and adds +10. Some carry a special skill: a survival effect or a multi-target attack.', F.body, 22, C.grey, { width: PET_INNER_W }));
  const mbox = rowBox('Mount roster', C.yellow, rowW);
  mbox.appendChild(heading('MOUNT ROSTER • 01–04', 'Awaiting art'));
  const ml = col('Chips', { dir: 'HORIZONTAL', gap: 8 });
  ml.appendChild(chip('NORMAL → LEGENDARY', C.yellow)); ml.appendChild(chip('BASE ROLL + EXTRA ROLL', C.paper)); ml.appendChild(chip('ABSORB +10', C.paper));
  mbox.appendChild(ml);
  const mgrid = col('Mount slots', { gap: 24 });
  for (let r = 0; r < 2; r++) {
    const mrow = col('Mount row', { dir: 'HORIZONTAL', gap: 24 });
    for (let i = r * 2 + 1; i <= r * 2 + 2; i++) mrow.appendChild(dashedSlot(proto, 'Mount ' + i, 'Mount 0' + i + ' • name TBD • 1920 × 1080', MOUNT_IMG_W, MOUNT_IMG_H));
    mgrid.appendChild(mrow);
  }
  mbox.appendChild(mgrid); mounts.appendChild(mbox);
  for (const c of mbox.children) if (c.type !== 'RECTANGLE') fillW(c);
  fitRim(mbox); board.appendChild(mounts); for (const c of mounts.children) fillW(c);

  for (const sec of [ref, pets, mounts]) {
    const band = sec.children.find((c) => c.name === 'Section identity');
    const accent = band && band.children.find((c) => c.name === 'Faceted header accent');
    if (accent) accent.x = band.width - accent.width + 95;
    if (dotsProto) { const d = dotsProto.clone(); sec.appendChild(d); d.layoutPositioning = 'ABSOLUTE'; d.x = 1888; d.y = 200; }
  }
  const note = col('Source and production notes', { dir: 'HORIZONTAL', gap: 64 });
  note.appendChild(text('Pet reference: Twelve Sky 2 pet sheets (Rare, Epic 15, Epic 30) • Mounts: 05_mounts.md', F.body, 24, C.grey));
  note.appendChild(text('MATTE / FACETED / INKED / HALFTONE SHADOWS', F.body, 24, C.ink));
  board.appendChild(note);
  return board;
}

main().then((r) => { console.log('World Atlas board done', r); figma.closePlugin('World Atlas: gear boxes rebuilt, capes + God tier added, pets & mounts board added'); })
  .catch((e) => { console.error(e); figma.closePlugin('World Atlas script failed: ' + e.message); });
