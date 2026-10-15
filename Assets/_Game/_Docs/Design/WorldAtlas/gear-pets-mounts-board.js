// Krytical Nova II — World Atlas (Figma) — "Gear, pet, mount Concept art" page
// Run this inside Figma with the page open (see README.md next to this file).
//
// What it does, in order:
//   1. Gear: every "Rare • …" / "Elite • …" look box gets a 1920 × 1080 image area
//      (existing sheets are moved in and scaled to fit) and the six piece slots are
//      replaced by three weapon slots. Lineage columns, faction panels, the title row
//      and the board frame grow to fit; the loose sheets below the board move down.
//   2. Pets & Mounts: a second board to the right of the gear board, decorated the
//      same way: 8 pet rows × 5 evolution slots, and 4 mount slots, with text.
//
// Safe to run twice: everything it adds is looked up by name before it is created.

const PAGE_NAME = 'Gear, pet, mount Concept art';

// ---- palette (matches the gear board) ------------------------------------------
const C = {
  ink: '#14122b', cream: '#f0ebdd', paper: '#fffef8', panel: '#e5e1d8', grey: '#696578',
  dash: '#a9a3b4', cyan: '#4fe3f0', cyanPale: '#dce8f0', yellow: '#f2d35b', purple: '#b591ec',
  dots: '#4c52bf', mint: '#fff6c4',
};
function rgb(hex) {
  const n = parseInt(hex.slice(1), 16);
  return { r: ((n >> 16) & 255) / 255, g: ((n >> 8) & 255) / 255, b: (n & 255) / 255 };
}
const solid = (hex) => [{ type: 'SOLID', color: rgb(hex) }];

// ---- sizes ------------------------------------------------------------------------
const BOX_W = 2000, INNER_W = 1952, LIN_W = 2048, IMG_W = 1920, IMG_H = 1080;
const SLOT_W = 634, SLOT_H = 380;              // weapon slots
const FACTION_W = 32 + LIN_W * 2 + 32 + 32;    // 4192
const FACTION_GAP = 354, FACTIONS_PAD = 129;
const PET_IMG_W = 960, PET_IMG_H = 540, PET_SLOT_W = PET_IMG_W + 32, PET_ROW_W = PET_SLOT_W * 5 + 24 * 4; // 5056
const MOUNT_IMG_W = 1200, MOUNT_IMG_H = 675;

// ---- fonts --------------------------------------------------------------------------
const F = {
  label: { family: 'Inter', style: 'Semi Bold' },
  body: { family: 'Inter', style: 'Regular' },
  bold: { family: 'Inter', style: 'Bold' },
  head: { family: 'Barlow Condensed', style: 'Bold' },
  title: { family: 'Barlow Condensed', style: 'ExtraBold' },
};

async function main() {
  for (const f of Object.values(F)) await figma.loadFontAsync(f);

  const page = figma.root.children.find((p) => p.name === PAGE_NAME);
  if (!page) throw new Error('Page not found: ' + PAGE_NAME);
  await figma.setCurrentPageAsync(page);

  const factions = page.children.find((n) => n.name === 'Factions' && n.type === 'FRAME');
  if (!factions) throw new Error('Frame "Factions" not found on the page');

  const report = { gearBoxes: 0, sheetsMoved: 0, gridsRemoved: 0, weaponRows: 0 };

  // prototype dashed slot: any piece frame still on the board, else a weapon slot
  let proto = factions.findOne((n) => n.type === 'FRAME' && n.name === 'Armor' && n.parent && n.parent.name === 'Piece art row');
  if (!proto) proto = factions.findOne((n) => n.type === 'FRAME' && n.name === 'Weapon 1');
  if (!proto) throw new Error('No dashed art frame found to use as a template');
  proto = proto.clone();
  page.appendChild(proto);

  // ================= 1. GEAR =====================================================
  const factionFrames = factions.children
    .filter((n) => n.type === 'FRAME' && n.children.some((c) => c.name === 'Faction identity'))
    .sort((a, b) => a.x - b.x);

  for (const faction of factionFrames) {
    const lineages = faction.children
      .filter((n) => n.type === 'FRAME' && n.children.some((c) => c.name === 'Lineage identity'))
      .sort((a, b) => a.x - b.x);

    for (const lineage of lineages) {
      const boxes = lineage.findAll((n) => n.type === 'FRAME' && /^(Rare|Elite) • /.test(n.name));
      for (const box of boxes) convertLookBox(box, proto, report);

      lineage.resize(LIN_W, lineage.height);
      for (const c of lineage.children) {
        if (c.name === 'Lineage identity' || c.name === 'Rare' || c.name === 'Elite') c.layoutSizingHorizontal = 'FILL';
      }
      const ident = lineage.children.find((c) => c.name === 'Lineage identity');
      if (ident) for (const c of ident.children) c.layoutSizingHorizontal = 'FILL';
      for (const sec of ['Rare', 'Elite']) {
        const s = lineage.children.find((c) => c.name === sec);
        if (s) for (const c of s.children) c.layoutSizingHorizontal = 'FILL';
      }
      const dots = lineage.children.find((c) => c.name === 'Panel edge dot shadow');
      if (dots) { dots.x = Math.round((LIN_W - dots.width) / 2); dots.y = lineage.height - 184; }
    }

    // place the lineage columns and grow the faction panel
    let maxBottom = 0;
    lineages.forEach((l, i) => { l.x = 32 + i * (LIN_W + 32); l.y = 232; maxBottom = Math.max(maxBottom, l.y + l.height); });
    const identity = faction.children.find((c) => c.name === 'Faction identity');
    if (identity) {
      identity.resize(FACTION_W - 64, identity.height);
      const accent = identity.children.find((c) => c.name === 'Faceted header accent');
      if (accent) accent.x = identity.width - accent.width + 95;
    }
    const refs = faction.children.find((c) => c.name === 'Faction weapon references');
    if (refs) {
      refs.y = maxBottom + 32;
      refs.resize(FACTION_W - 64, refs.height);
      for (const c of refs.children) c.layoutSizingHorizontal = 'FILL';
      maxBottom = refs.y + refs.height;
    }
    faction.resize(FACTION_W, maxBottom + 32);
  }

  // place the three faction panels and grow the board
  let factionBottom = 0;
  factionFrames.forEach((f, i) => { f.x = FACTIONS_PAD + i * (FACTION_W + FACTION_GAP); factionBottom = Math.max(factionBottom, f.y + f.height); });
  const boardW = FACTIONS_PAD * 2 + FACTION_W * 3 + FACTION_GAP * 2;
  const notes = factions.children.find((c) => c.name === 'Source and production notes');
  if (notes) {
    notes.y = factionBottom + 100;
    notes.resize(boardW - 128, notes.height);
    const scope = notes.children.find((c) => c.name === 'Scope');
    if (scope) scope.x = notes.width - scope.width;
    const art = notes.children.find((c) => c.name === 'Art language');
    if (art) art.x = Math.round((notes.width - art.width) / 2);
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
    }
    const placement = titleRow.findOne((n) => n.type === 'TEXT' && n.name === 'Level and placement guide');
    if (placement) placement.characters = 'Level chips share one look within their group; only the listed M-levels apply. Place the 1920 × 1080 concept sheet in the big dashed frame and each of the set’s three weapons in the three slots under it.';
  }
  const oldBottom = factions.y + factions.height;
  factions.resize(boardW, (notes ? notes.y + notes.height : factionBottom) + 64);
  const newBottom = factions.y + factions.height;

  // loose sheets that sat under the old board move under the new one
  const loose = page.children.filter((n) => n !== factions && n.type === 'RECTANGLE' && n.y > oldBottom - 1200 && n.x + n.width > factions.x && n.x < factions.x + boardW);
  if (loose.length) {
    const minY = Math.min(...loose.map((n) => n.y));
    const shift = newBottom + 400 - minY;
    if (shift > 0) for (const n of loose) n.y += shift;
  }

  // ================= 2. PETS & MOUNTS ============================================
  const hegemonyIdentity = factionFrames[0] && factionFrames[0].children.find((c) => c.name === 'Faction identity');
  const accentProto = hegemonyIdentity && hegemonyIdentity.children.find((c) => c.name === 'Faceted header accent');
  const dotsProto = factionFrames[0] && factionFrames[0].children.find((c) => c.name === 'Faction header dot shadow');

  let board = page.children.find((n) => n.name === 'Pets and Mounts' && n.type === 'FRAME');
  if (!board) {
    board = buildPetsAndMountsBoard(proto, accentProto, dotsProto);
    page.appendChild(board);
  }
  board.x = factions.x + factions.width + 600;
  board.y = factions.y;

  proto.remove();
  report.boardSize = { w: Math.round(factions.width), h: Math.round(factions.height) };
  report.petsBoard = { id: board.id, w: Math.round(board.width), h: Math.round(board.height) };
  return report;
}

// ---- one gear look box ----------------------------------------------------------
function convertLookBox(box, proto, report) {
  report.gearBoxes++;
  box.resize(BOX_W, box.height);
  box.primaryAxisSizingMode = 'AUTO';
  for (const c of box.children) if (c.name === 'Look heading' || c.name === 'Shared-look levels') c.layoutSizingHorizontal = 'FILL';

  const areas = box.children.find((c) => c.name === 'Art insertion areas');
  if (!areas) return;
  areas.layoutMode = 'VERTICAL';
  areas.itemSpacing = 24;
  areas.layoutSizingHorizontal = 'FILL';
  areas.layoutSizingVertical = 'HUG';

  const grid = areas.children.find((c) => c.name === 'Piece art grid');
  if (grid) { grid.remove(); report.gridsRemoved++; }

  const full = areas.children.find((c) => c.name === 'Full set / concept art');
  if (full) {
    full.resize(INNER_W, full.height);
    full.itemSpacing = 8;
    full.primaryAxisSizingMode = 'AUTO';
    full.counterAxisSizingMode = 'FIXED';
    const label = full.children.find((c) => c.type === 'TEXT' && c.name === 'Art target label');
    if (label) { label.characters = 'Full set / concept sheet • 1920 × 1080'; label.layoutSizingHorizontal = 'FILL'; }
    let area = full.children.find((c) => c.name === 'Image area 1920×1080');
    if (!area) {
      area = figma.createFrame();
      area.name = 'Image area 1920×1080';
      area.resize(IMG_W, IMG_H);
      area.fills = solid(C.cream);
      area.clipsContent = true;
      full.insertChild(1, area);
    }
    const img = full.children.find((c) => c.type === 'RECTANGLE' && c.fills.length && c.fills[0].type === 'IMAGE');
    if (img) {
      const s = Math.min(IMG_W / img.width, IMG_H / img.height);
      const w = Math.round(img.width * s), h = Math.round(img.height * s);
      area.appendChild(img);
      img.resize(w, h);
      img.x = Math.round((IMG_W - w) / 2);
      img.y = Math.round((IMG_H - h) / 2);
      report.sheetsMoved++;
    }
  }

  let row = areas.children.find((c) => c.name === 'Weapon slots');
  if (!row) {
    row = figma.createFrame();
    row.name = 'Weapon slots';
    row.layoutMode = 'HORIZONTAL';
    row.itemSpacing = 24;
    row.fills = [];
    areas.appendChild(row);
    row.layoutSizingHorizontal = 'FILL';
    row.layoutSizingVertical = 'HUG';
    for (let i = 1; i <= 3; i++) {
      const slot = proto.clone();
      slot.name = 'Weapon ' + i;
      row.appendChild(slot);
      slot.resize(SLOT_W, SLOT_H);
      slot.layoutSizingHorizontal = 'FILL';
      const l = slot.children.find((c) => c.type === 'TEXT' && c.name === 'Art target label');
      if (l) l.characters = 'Weapon ' + i + ' • class';
    }
    report.weaponRows++;
  }
  const rim = box.children.find((c) => c.name === 'Rarity rim');
  if (rim) rim.resize(8, box.height);
}

// ---- small builders ------------------------------------------------------------------
function text(chars, font, size, hex, opts = {}) {
  const t = figma.createText();
  t.fontName = font;
  t.characters = chars;
  t.fontSize = size;
  t.fills = solid(hex);
  if (opts.width) { t.resize(opts.width, t.height); t.textAutoResize = 'HEIGHT'; }
  if (opts.name) t.name = opts.name;
  return t;
}
function col(name, opts = {}) {
  const f = figma.createFrame();
  f.name = name;
  f.layoutMode = opts.dir || 'VERTICAL';
  f.itemSpacing = opts.gap || 0;
  f.paddingLeft = f.paddingRight = opts.px || 0;
  f.paddingTop = f.paddingBottom = opts.py || 0;
  f.primaryAxisSizingMode = 'AUTO';
  f.counterAxisSizingMode = 'AUTO';
  f.fills = opts.fill ? solid(opts.fill) : [];
  if (opts.stroke) { f.strokes = solid(opts.stroke); f.strokeWeight = opts.strokeW || 3; }
  if (opts.align) f.primaryAxisAlignItems = opts.align;
  if (opts.cross) f.counterAxisAlignItems = opts.cross;
  f.clipsContent = false;
  return f;
}
function fillW(node) {
  if (node.type === 'TEXT' && node.textAutoResize === 'WIDTH_AND_HEIGHT') { node.resize(Math.max(node.width, 200), node.height); node.textAutoResize = 'HEIGHT'; }
  node.layoutSizingHorizontal = 'FILL';
  return node;
}
function chip(label, hex) {
  const c = col('Chip', { dir: 'HORIZONTAL', px: 16, py: 8, fill: hex, stroke: C.ink, strokeW: 1 });
  c.appendChild(text(label, F.bold, 20, C.ink));
  return c;
}
function dashedSlot(proto, name, label, imgW, imgH) {
  const slot = proto.clone();
  slot.name = name;
  slot.itemSpacing = 8;
  slot.primaryAxisSizingMode = 'AUTO';
  slot.counterAxisSizingMode = 'FIXED';
  slot.resize(imgW + 32, slot.height);
  const l = slot.children.find((c) => c.type === 'TEXT' && c.name === 'Art target label');
  if (l) { l.characters = label; l.layoutSizingHorizontal = 'FILL'; }
  const area = figma.createFrame();
  area.name = 'Image area ' + imgW + '×' + imgH;
  area.resize(imgW, imgH);
  area.fills = solid(C.cream);
  area.clipsContent = true;
  slot.insertChild(1, area);
  return slot;
}
function sectionBand(title, direction, inventoryTop, inventoryBottom, bandHex, accentHex, accentProto) {
  const band = col('Section identity', { dir: 'HORIZONTAL', px: 32, py: 20, fill: bandHex, align: 'SPACE_BETWEEN', cross: 'CENTER' });
  band.clipsContent = true;
  const left = col('Section title');
  left.appendChild(text(title, F.title, 100, C.ink));
  left.appendChild(text(direction, F.bold, 20, C.ink));
  band.appendChild(left);
  const right = col('Section inventory', { gap: 8, cross: 'MAX' });
  right.appendChild(text(inventoryTop, F.head, 36, C.ink));
  right.appendChild(text(inventoryBottom, F.label, 20, C.ink));
  band.appendChild(right);
  if (accentProto) {
    const accent = accentProto.clone();
    band.appendChild(accent);
    accent.layoutPositioning = 'ABSOLUTE';
    accent.fills = solid(accentHex);
    accent.y = 0;
    accent.x = 2400;
  }
  return band;
}
function rowBox(name, rimHex) {
  const box = col(name, { gap: 16, px: 24, py: 24, fill: C.paper, stroke: C.ink, strokeW: 3 });
  box.counterAxisSizingMode = 'FIXED';
  box.resize(PET_ROW_W + 48, box.height);
  const rim = figma.createRectangle();
  rim.name = 'Rarity rim';
  rim.fills = solid(rimHex);
  box.appendChild(rim);
  rim.layoutPositioning = 'ABSOLUTE';
  rim.x = 0; rim.y = 0;
  rim.resize(8, 100);
  return box;
}
function fitRim(box) {
  const rim = box.children.find((c) => c.name === 'Rarity rim');
  if (rim) rim.resize(8, box.height);
}
function heading(name, status) {
  const h = col('Look heading', { dir: 'HORIZONTAL', align: 'SPACE_BETWEEN', cross: 'CENTER' });
  h.appendChild(text(name, F.head, 36, C.ink, { name: 'Scheme name' }));
  h.appendChild(text(status, F.body, 18, C.grey, { name: 'Section status' }));
  return h;
}

// ---- the pets & mounts board ------------------------------------------------------
const PETS = [
  { n: 'EMBERWING', stat: 'FLUX +2,000 AT 100 %', id: 'Item 8001 • bird • own model', rim: C.cyan,
    desc: 'A fire-feathered bird that flies at head height beside its owner, sheds cinders from its wing tips and tail, and waits in the air on folded-wing idles. Its five stage looks are not yet decided: keep the same bird and make it burn brighter at each step.',
    stages: ['Baseline (0 %) • fledgling', '25 % • to be decided', '50 % • to be decided', '75 % • to be decided', '100 % • to be decided'] },
  { n: 'DRONE CORE', stat: 'HEALTH +2,200 AT 100 %', id: 'Item 8002 • drone • own model', rim: C.cyan,
    desc: 'A hovering cube of a drone with a painted texture of its own, dropping blinking data chips behind it as it moves. It flies apart into a swarm and reassembles; at 100 % it is a gunship.',
    stages: ['Baseline (0 %) • bare cube', '25 % • ring light + barrel lens', '50 % • cage frame + cannon', '75 % • plating + side pods', '100 % • the Gunship'] },
  { n: 'TIDECALLER', stat: 'DEFENSE +2,200 AT 100 %', id: 'Item 8003 • egg → swimmer • own model', rim: C.cyan,
    desc: 'An egg with a face that hops, naps and rams, then hatches into a swimmer that grows 0.1 m a stage and pulls ribbons of water behind it. Glow lines breathe from 50 %; the 100 % form carries a blue orb.',
    stages: ['Baseline (0 %) • the egg', '25 % • swimmer with fins', '50 % • stone shell plates', '75 % • spikes + blue band', '100 % • cracks + blue orb'] },
  { n: 'KITSUNE SPIRIT', stat: 'ATTACK +1,100 AT 100 %', id: 'Item 8004 • fox • own model', rim: C.cyan,
    desc: 'A fox spirit that stands on small clouds and gains tails as it grows: two at 25 %, three, five, then nine. From 75 % it stands on fire instead of clouds; at 100 % flame tufts burn on its body. Attack is the most valuable stat, so its max is half the others.',
    stages: ['Baseline (0 %) • one tail, clouds', '25 % • two tails', '50 % • three tails', '75 % • five tails, fire under paws', '100 % • nine tails, body foxfire'] },
];
for (let i = 5; i <= 8; i++) PETS.push({
  n: 'NEW PET ' + (i - 4), stat: 'STAT TO BE DECIDED', id: 'Item 800' + i + ' • creature TBD', rim: C.dash,
  desc: 'Reserved for the next roster round. Pick one stat the pet grows (Attack, Flux, Defense or Health), keep it usable from level 1, and borrow its silhouette from a Synty POLYGON cluster the first four do not use yet (monster-scale, relic, mechanical or winged).',
  stages: ['Baseline (0 %)', '25 %', '50 %', '75 %', '100 %'],
});

function buildPetsAndMountsBoard(proto, accentProto, dotsProto) {
  const board = col('Pets and Mounts', { gap: 64, px: 128, py: 128, fill: C.cream });

  // title row
  const titleRow = col('Atlas title and board guide', { dir: 'HORIZONTAL', gap: 80 });
  const title = col('Atlas title', { gap: 12 });
  title.appendChild(text('KRYTICAL NOVA II / WORLD ATLAS', F.bold, 28, C.ink));
  title.appendChild(text('PETS & MOUNTS / ART BREAKDOWN', F.title, 144, C.ink));
  const inv = col('Board inventory', { dir: 'HORIZONTAL', gap: 32, cross: 'CENTER' });
  inv.appendChild(text('8 pets / 5 evolutions each / 4 mounts / 44 art targets', F.label, 28, C.ink));
  const divider = figma.createRectangle(); divider.resize(2, 32); divider.fills = solid(C.ink); inv.appendChild(divider);
  inv.appendChild(text('All sections awaiting art', F.body, 24, C.grey));
  title.appendChild(inv);
  titleRow.appendChild(title);
  const guide = col('Board guide', { gap: 18, px: 32, py: 32, fill: C.ink });
  const gh = col('Guide heading', { dir: 'HORIZONTAL', align: 'SPACE_BETWEEN' });
  gh.appendChild(text('HOW TO USE THIS BOARD', F.head, 36, C.paper));
  gh.appendChild(text('PRODUCTION ART / EDITABLE', F.bold, 18, C.cyan));
  guide.appendChild(gh); fillW(gh);
  guide.appendChild(text('Pet → growth stage (0 / 25 / 50 / 75 / 100 %) → art target. Mount → one art target.', F.body, 26, C.paper));
  guide.appendChild(text('Place art in the dashed frames. Pet and mount sheets are 1920 × 1080 like the gear sheets; the image areas here are 960 × 540, so drop a sheet in at 50 %. A pet grows one stat and never changes level: the same creature matures through five looks.', F.body, 24, C.cyanPale, { width: 2000 }));
  const key = col('Key', { dir: 'HORIZONTAL', gap: 36, cross: 'CENTER' });
  const k1 = figma.createRectangle(); k1.resize(20, 20); k1.fills = solid(C.cyan); key.appendChild(k1);
  key.appendChild(text('PETS / 8 rows × 5 stages', F.label, 22, C.paper));
  const k2 = figma.createRectangle(); k2.resize(20, 20); k2.fills = solid(C.yellow); key.appendChild(k2);
  key.appendChild(text('MOUNTS / 4 targets', F.label, 22, C.paper));
  key.appendChild(text('Rules: 12_pet_evolution_system.md • 05_mounts.md', F.body, 18, C.cyanPale));
  guide.appendChild(key);
  titleRow.appendChild(guide);
  board.appendChild(titleRow);
  fillW(titleRow); fillW(guide);
  for (const c of guide.children) if (c.type === 'TEXT') fillW(c);

  // ---- pets section
  const pets = col('Pets', { gap: 32, px: 32, py: 32, fill: C.panel, stroke: C.ink, strokeW: 6 });
  pets.appendChild(sectionBand('PETS', 'ONE STAT PER PET / FIVE LOOKS / SYNTY POLYGON', 'EMBERWING / DRONE CORE / TIDECALLER / KITSUNE SPIRIT / + 4 NEW', '8 PETS / 40 LOOKS', C.cyan, C.cyanPale, accentProto));
  pets.appendChild(text('Pets grow on their own XP, separate from the player’s, from 0 to 100 % (100,000 XP). They evolve at 25, 50, 75 and 100 %: a new model or a stronger look at each step, the same creature throughout. Every 1 % of growth pays out 1 % of the stat. A pet at 0 % hunger gives no stat and gains no XP until it is fed a Pet Ration.', F.body, 24, C.grey, { width: 2000 }));
  PETS.forEach((p, i) => {
    const box = rowBox('Pet ' + String(i + 1).padStart(2, '0') + ' • ' + p.n, p.rim);
    box.appendChild(heading('PET ' + String(i + 1).padStart(2, '0') + ' • ' + p.n, 'Awaiting art'));
    const chips = col('Pet chips', { dir: 'HORIZONTAL', gap: 8, align: 'SPACE_BETWEEN', cross: 'CENTER' });
    const left = col('Chips', { dir: 'HORIZONTAL', gap: 8 });
    left.appendChild(chip(p.stat, p.rim === C.dash ? C.panel : C.yellow));
    left.appendChild(chip('LEVEL 1+', C.paper));
    left.appendChild(chip('COMMON', C.paper));
    chips.appendChild(left);
    chips.appendChild(text(p.id, F.body, 18, C.grey));
    box.appendChild(chips);
    box.appendChild(text(p.desc, F.body, 22, C.ink, { width: PET_ROW_W, name: 'Pet description' }));
    const row = col('Evolution slots', { dir: 'HORIZONTAL', gap: 24 });
    p.stages.forEach((s, k) => row.appendChild(dashedSlot(proto, 'Stage ' + (k + 1), 'Stage ' + (k + 1) + ' • ' + s, PET_IMG_W, PET_IMG_H)));
    box.appendChild(row);
    pets.appendChild(box);
    for (const c of box.children) if (c.type !== 'RECTANGLE') fillW(c);
    fitRim(box);
  });
  board.appendChild(pets);
  for (const c of pets.children) fillW(c);

  // ---- mounts section
  const mounts = col('Mounts', { gap: 32, px: 32, py: 32, fill: C.panel, stroke: C.ink, strokeW: 6 });
  mounts.appendChild(sectionBand('MOUNTS', 'MOBILITY + PASSIVE STATS / RIDE IT OR ABSORB IT', 'FOUR MOUNTS / NAMES TO BE DECIDED', '4 MOUNTS / 4 LOOKS', C.yellow, C.mint, accentProto));
  mounts.appendChild(text('A mount is movement and a stat system in one: it rolls base abilities on acquisition, an extra ability on top, and can be re-rolled to optimise. Legendary mounts are crafted from a mount that has reached 15 %. An absorbed mount disappears from view but keeps its abilities on and adds +10. Some carry a special skill of their own: a survival effect or a multi-target attack. Art: one 1920 × 1080 sheet per mount, the rider from the faction it belongs to.', F.body, 24, C.grey, { width: 2000 }));
  const mbox = rowBox('Mount roster', C.yellow);
  mbox.appendChild(heading('MOUNT ROSTER • 01–04', 'Awaiting art'));
  const mchips = col('Mount chips', { dir: 'HORIZONTAL', gap: 8, align: 'SPACE_BETWEEN', cross: 'CENTER' });
  const ml = col('Chips', { dir: 'HORIZONTAL', gap: 8 });
  ml.appendChild(chip('NORMAL → LEGENDARY', C.yellow));
  ml.appendChild(chip('BASE ROLL + EXTRA ROLL', C.paper));
  ml.appendChild(chip('ABSORB +10', C.paper));
  mchips.appendChild(ml);
  mchips.appendChild(text('One sheet per mount • 1920 × 1080', F.body, 18, C.grey));
  mbox.appendChild(mchips);
  const mrow = col('Mount slots', { dir: 'HORIZONTAL', gap: 24 });
  for (let i = 1; i <= 4; i++) mrow.appendChild(dashedSlot(proto, 'Mount ' + i, 'Mount 0' + i + ' • name TBD', MOUNT_IMG_W, MOUNT_IMG_H));
  mbox.appendChild(mrow);
  mounts.appendChild(mbox);
  for (const c of mbox.children) if (c.type !== 'RECTANGLE') fillW(c);
  fitRim(mbox);
  board.appendChild(mounts);
  for (const c of mounts.children) fillW(c);

  // faceted accent to the right edge of each band, and ben-day dot shadows under
  // the two section bands, like the faction headers
  for (const sec of [pets, mounts]) {
    const band = sec.children.find((c) => c.name === 'Section identity');
    const accent = band && band.children.find((c) => c.name === 'Faceted header accent');
    if (accent) accent.x = band.width - accent.width + 95;
    if (dotsProto) {
      const d = dotsProto.clone();
      sec.appendChild(d);
      d.layoutPositioning = 'ABSOLUTE';
      d.x = 1888; d.y = 200;
    }
  }

  // production note
  const note = col('Source and production notes', { dir: 'HORIZONTAL', gap: 64 });
  note.appendChild(text('Pets: Pet Evolution System doc (5 Oct 2026) and 12_pet_evolution_system.md • Mounts: 05_mounts.md', F.body, 24, C.grey));
  note.appendChild(text('MATTE / FACETED / INKED / HALFTONE SHADOWS', F.body, 24, C.ink));
  board.appendChild(note);
  return board;
}

main().then((r) => { console.log('World Atlas board done', r); figma.closePlugin('World Atlas: gear boxes rebuilt, pets & mounts board added'); })
  .catch((e) => { console.error(e); figma.closePlugin('World Atlas script failed: ' + e.message); });
