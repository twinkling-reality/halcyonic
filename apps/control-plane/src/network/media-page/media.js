// The Mac's Photos and Videos page (ADR 0066): draws the host's state as it is, every word from it or from this file.
// A file the person chooses or drops goes to the host as raw bytes; the host decides what it is. Names are set as
// text, never markup. Each project's block and each tile is kept while it stands, so a playing video is never
// rebuilt by a later poll; a new tile is inserted beside the ones already there.

const projects = document.getElementById('projects');
const nothing = document.getElementById('nothing');
let poll = null;
let drawn = '';
/** Each project's block, by project id. */
const blocks = new Map();
/** A file this page is sending, by project id: how much of it has gone, 0 to 1. */
const sending = new Map();
/**
 * The file a project's incoming tile stands for, by project id: its name, whether it is a video, and once this page has
 * read it here, its first frame (a canvas at the file's own shape). The frame never leaves this page.
 */
const chosen = new Map();
/** The shape of the file a project last sent, held until the host's own tile for it is drawn, by project id. */
const sentShape = new Map();
/** The longest side a first frame is drawn at; the tile is never wider than this on the page's widest row. */
const FRAME_SIDE = 640;
/** Where in a video the host takes the still its tile shows (STILL_AT's first, in media-tool.ts), as a share of it. */
const STILL_SHARE = 0.1;
/** The progress ring's circle: its radius and the length of its edge. */
const RING_R = 24;
const RING_EDGE = 2 * Math.PI * RING_R;
/** Words this page itself has for a project's last add (the file never reached the host), by project id. */
const told = new Map();

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function glyph(id) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('class', 'glyph');
  svg.setAttribute('aria-hidden', 'true');
  const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
  use.setAttribute('href', `#${id}`);
  svg.append(use);
  return svg;
}

/** Sends the file's bytes, showing how much has gone; the host's answer says whether it was taken. */
function send(projectId, file) {
  sending.set(projectId, 0);
  const shown = {
    name: file.name,
    video: file.type.startsWith('video/') || /\.(mov|mp4)$/i.test(file.name),
    frame: null,
  };
  chosen.set(projectId, shown);
  sentShape.delete(projectId);
  told.delete(projectId);
  redraw();
  firstFrame(file, shown.video).then((frame) => {
    if (frame === null || chosen.get(projectId) !== shown) return;
    shown.frame = frame;
    sentShape.set(projectId, frame.width / frame.height);
    redraw();
  });
  const request = new XMLHttpRequest();
  request.open('POST', `/setup/media/add?project=${encodeURIComponent(projectId)}`);
  request.setRequestHeader('content-type', 'application/octet-stream');
  request.upload.addEventListener('progress', (event) => {
    if (!event.lengthComputable || !sending.has(projectId)) return;
    sending.set(projectId, event.loaded / event.total);
    const tile = document.querySelector(
      `[data-project="${CSS.escape(projectId)}"] .media-incoming`,
    );
    if (tile) progress(tile, sending.get(projectId));
  });
  const finish = async (words) => {
    // The incoming tile stays while the host reads the state, so it never blinks out before the host's own does.
    await refresh();
    sending.delete(projectId);
    if (words !== null) {
      told.set(projectId, words);
      sentShape.delete(projectId);
    }
    redraw();
  };
  request.addEventListener('load', () => {
    if (request.status === 413) return finish('That file is too large to add.');
    let answer = null;
    try {
      answer = JSON.parse(request.responseText);
    } catch {
      answer = null;
    }
    return finish(answer && typeof answer.said === 'string' ? answer.said : null);
  });
  request.addEventListener('error', () => finish("That file couldn't be sent. Try again."));
  request.send(file);
}

/**
 * Reads the chosen file's first frame here in the browser, before the host has it: a photo decoded as it is turned, a
 * video's first drawn frame. Null when this browser cannot read it (a HEIC in Chrome, say); the tile keeps its mark.
 */
async function firstFrame(file, video) {
  let source = null;
  let url = null;
  try {
    if (video) {
      url = URL.createObjectURL(file);
      const player = document.createElement('video');
      player.muted = true;
      player.playsInline = true;
      player.preload = 'auto';
      await new Promise((resolve, reject) => {
        const timer = setTimeout(reject, 10000);
        player.addEventListener('loadeddata', () => resolve(clearTimeout(timer)), { once: true });
        player.addEventListener('error', () => reject(clearTimeout(timer)), { once: true });
        player.src = url;
      });
      // The frame the host's still will show, so the picture is the same from its first byte to its own tile.
      if (Number.isFinite(player.duration) && player.duration > 0) {
        await new Promise((resolve) => {
          const timer = setTimeout(resolve, 3000);
          player.addEventListener('seeked', () => resolve(clearTimeout(timer)), { once: true });
          player.currentTime = player.duration * STILL_SHARE;
        });
      }
      source = await createImageBitmap(player);
      player.removeAttribute('src');
      player.load();
    } else {
      source = await createImageBitmap(file, { imageOrientation: 'from-image' });
    }
    if (source.width === 0 || source.height === 0) return null;
    const scale = Math.min(1, FRAME_SIDE / Math.max(source.width, source.height));
    const canvas = el('canvas', 'media-incoming-frame');
    canvas.width = Math.max(1, Math.round(source.width * scale));
    canvas.height = Math.max(1, Math.round(source.height * scale));
    canvas.setAttribute('aria-hidden', 'true');
    canvas.getContext('2d')?.drawImage(source, 0, 0, canvas.width, canvas.height);
    return canvas;
  } catch {
    return null;
  } finally {
    source?.close?.();
    if (url !== null) URL.revokeObjectURL(url);
  }
}

function choose(block) {
  if (!block.adding && !sending.has(block.id)) block.input.click();
}

/** The tile for a file still on its way: sent, then made ready by the host. */
function incomingTile() {
  const node = el('div', 'media-tile media-incoming media-shown');
  node.style.setProperty('--ratio', '0.75');
  const sheen = el('span', 'media-sheen');
  const mark = el('span', 'media-incoming-mark');
  const words = el('span', 'media-incoming-words');
  const name = el('span', 'media-incoming-name');
  // The words, name and bar stand together; over a first frame they sit on a dark glass caption of their own.
  const caption = el('span', 'media-incoming-caption');
  const bar = el('span', 'media-bar');
  bar.append(el('span', 'media-bar-fill'));
  caption.append(words, name, bar);
  node.append(sheen, mark, ring(), caption);
  return node;
}

/**
 * The progress ring drawn on a first frame, as a chat app draws it on a picture it is sending: one dark glass disc,
 * the same over any picture, with its track and arc.
 */
function ring() {
  const disc = el('span', 'media-ring');
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 56 56');
  svg.setAttribute('aria-hidden', 'true');
  disc.append(svg);
  for (const kind of ['media-ring-track', 'media-ring-arc', 'media-ring-spark']) {
    const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    circle.setAttribute('class', kind);
    circle.setAttribute('cx', '28');
    circle.setAttribute('cy', '28');
    circle.setAttribute('r', String(RING_R));
    circle.setAttribute('pathLength', String(RING_EDGE));
    circle.style.strokeDasharray = String(RING_EDGE);
    svg.append(circle);
  }
  return disc;
}

/** Shows how much of the file has gone, on the bar and on the ring alike. */
function progress(node, fraction) {
  node.querySelector('.media-bar-fill').style.transform = `scaleX(${fraction})`;
  node.querySelector('.media-ring-arc').style.strokeDashoffset = String(RING_EDGE * (1 - fraction));
}

function setIncoming(node, fraction, file) {
  const sent = fraction !== null;
  const kind = file?.video === true ? 'g-video' : 'g-photo';
  // Once its first frame is read, the tile stands at the file's own shape with the frame under its words.
  const frame = file?.frame ?? null;
  const held = node.querySelector('.media-incoming-frame');
  if (held !== frame) {
    held?.remove();
    if (frame !== null) node.prepend(frame);
  }
  node.classList.toggle('media-framed', frame !== null);
  node.style.setProperty('--ratio', frame === null ? '0.75' : String(frame.width / frame.height));
  const mark = node.querySelector('.media-incoming-mark');
  if (mark.dataset.kind !== kind) {
    mark.dataset.kind = kind;
    mark.replaceChildren(glyph(kind));
  }
  node.querySelector('.media-incoming-name').textContent = file?.name ?? '';
  node.classList.toggle('media-sending', sent);
  const words = sent ? 'Sending' : 'Preparing';
  node.querySelector('.media-incoming-words').textContent = words;
  // Over a first frame the ring alone shows the state, so the tile carries its word for a screen reader.
  node.setAttribute('role', 'img');
  node.setAttribute('aria-label', file?.name ? `${words} ${file.name}` : words);
  // Preparing has no measure: the bar's short run travels, and the ring stays full while a bright spark circles it,
  // so it never reads as less than every byte sent.
  progress(node, sent ? fraction : 1);
}

function tile(projectId, medium, shape) {
  const node = el('div', medium.video ? 'media-tile media-video' : 'media-tile');
  // Takes the shape of the file sent from this page while its still loads, so the row does not move as it lands.
  node.style.setProperty('--ratio', String(shape ?? 0.75));
  if (medium.still !== null) {
    const image = el('img', 'media-still');
    image.alt = '';
    image.decoding = 'async';
    // Shown only once whole, at the picture's own shape, so a tile never draws half a picture or a cropped one.
    image.addEventListener('load', () => {
      if (image.naturalWidth > 0 && image.naturalHeight > 0)
        node.style.setProperty('--ratio', String(image.naturalWidth / image.naturalHeight));
      node.classList.add('media-shown');
    });
    image.src = `/setup/media/still?project=${encodeURIComponent(projectId)}&path=${encodeURIComponent(medium.still)}`;
    node.append(image);
  } else {
    node.append(glyph('g-photo'));
    node.classList.add('media-shown', 'media-bare');
  }
  // A video plays its own copy, muted and looping, over its still, shown only once its first frame is drawn.
  if (medium.video && medium.play !== null) {
    const video = el('video', 'media-play');
    video.muted = true;
    video.loop = true;
    video.autoplay = true;
    video.playsInline = true;
    video.preload = 'auto';
    video.addEventListener('playing', () => node.classList.add('media-moving'), { once: true });
    video.src = `/setup/media/play?project=${encodeURIComponent(projectId)}&path=${encodeURIComponent(medium.play)}`;
    node.append(video);
  }
  if (medium.video) {
    const mark = el('span', 'media-mark');
    mark.append(glyph('g-play'));
    if (medium.seconds !== null) mark.append(el('span', 'media-seconds', `${medium.seconds} s`));
    node.append(mark);
  }
  return node;
}

function block(project) {
  const section = el('section', 'block media-project');
  section.dataset.project = project.project_id;
  const head = el('div', 'media-head');
  const name = el('h2', 'media-name');
  const input = el('input', 'media-file');
  input.type = 'file';
  input.accept = 'image/png,image/jpeg,image/webp,image/heic,.heic,video/mp4,video/quicktime,.mov';
  input.hidden = true;
  const add = el('button', 'rest');
  add.type = 'button';
  head.append(name, input, add);

  const said = el('p', 'media-said');
  said.append(glyph('g-alert'), el('span', ''));
  said.hidden = true;

  // An empty project's whole well is a place to drop a file, and a press on it chooses one.
  const well = el('button', 'media-well');
  well.type = 'button';
  const wellMark = el('span', 'media-well-mark');
  wellMark.append(glyph('g-drop'));
  well.append(
    wellMark,
    el('span', 'media-well-title', 'Drop a Photo or Video'),
    el('span', 'media-well-line', 'Or choose one with Add Media.'),
  );

  const grid = el('div', 'media-grid');
  section.append(head, said, well, grid);

  const kept = {
    id: project.project_id,
    section,
    name,
    add,
    input,
    said,
    well,
    grid,
    tiles: new Map(),
    adding: false,
  };
  input.addEventListener('change', () => {
    const file = input.files?.[0];
    if (file) send(project.project_id, file);
    input.value = '';
  });
  add.addEventListener('click', () => choose(kept));
  well.addEventListener('click', () => choose(kept));

  // The whole block takes a dropped file; the well lights while one is held over it.
  let held = 0;
  section.addEventListener('dragenter', (event) => {
    event.preventDefault();
    held += 1;
    section.classList.add('media-drop');
  });
  section.addEventListener('dragover', (event) => event.preventDefault());
  section.addEventListener('dragleave', () => {
    held = Math.max(0, held - 1);
    if (held === 0) section.classList.remove('media-drop');
  });
  section.addEventListener('drop', (event) => {
    event.preventDefault();
    held = 0;
    section.classList.remove('media-drop');
    const file = event.dataTransfer?.files?.[0];
    if (file && !kept.adding && !sending.has(project.project_id)) send(project.project_id, file);
  });
  return kept;
}

/** Puts `wanted` in `parent` in order, moving no node already where it should be, so a playing video keeps playing. */
function place(parent, wanted) {
  const keep = new Set(wanted);
  for (const child of [...parent.children]) if (!keep.has(child)) child.remove();
  wanted.forEach((node, index) => {
    if (parent.children[index] !== node) parent.insertBefore(node, parent.children[index] ?? null);
  });
}

function update(kept, project) {
  kept.adding = project.adding;
  kept.name.textContent = project.name;
  const busy = project.adding || sending.has(project.project_id);
  kept.add.disabled = busy;
  kept.add.replaceChildren(glyph(busy ? 'g-hourglass' : 'g-plus'), busy ? 'Adding' : 'Add Media');
  // What went wrong with the last add, from this page or the host, until the next add starts.
  const words = sending.has(project.project_id)
    ? null
    : (told.get(project.project_id) ?? project.said);
  kept.said.hidden = words === null;
  if (words !== null) kept.said.lastChild.textContent = words;

  // Newest first. A file on its way is one tile from its first byte until the host has made it ready, then its own.
  const media = [...project.media].reverse();
  const waiting = media.findIndex((medium) => !medium.prepared);
  const fraction = sending.get(project.project_id);
  const incoming = fraction !== undefined || project.adding || waiting !== -1;
  const wanted = [];
  if (incoming) {
    const node = kept.tiles.get('incoming') ?? incomingTile();
    kept.tiles.set('incoming', node);
    const file =
      chosen.get(project.project_id) ??
      (waiting === -1 ? undefined : { name: '', video: media[waiting].video });
    setIncoming(node, fraction ?? null, file);
    wanted.push(node);
  } else {
    chosen.delete(project.project_id);
  }
  const newest = media.findIndex((_medium, index) => index !== waiting);
  media.forEach((medium, index) => {
    if (index === waiting) return;
    const key = `${medium.path}|${medium.still}|${medium.play}|${medium.prepared}`;
    let node = kept.tiles.get(key);
    if (node === undefined) {
      // A new tile in the newest place is the one this page sent (one add at a time): it takes that file's shape.
      const shape = index === newest ? sentShape.get(project.project_id) : undefined;
      node = tile(project.project_id, medium, shape);
      if (shape !== undefined) sentShape.delete(project.project_id);
    }
    kept.tiles.set(key, node);
    wanted.push(node);
  });
  for (const [key, node] of kept.tiles) if (!wanted.includes(node)) kept.tiles.delete(key);
  place(kept.grid, wanted);
  kept.well.hidden = wanted.length > 0;
  kept.grid.hidden = wanted.length === 0;
  kept.section.classList.toggle('media-has', wanted.length > 0);
}

function draw(state) {
  nothing.hidden = state.projects.length > 0;
  const wanted = state.projects.map((project) => {
    const kept = blocks.get(project.project_id) ?? block(project);
    blocks.set(project.project_id, kept);
    update(kept, project);
    return kept.section;
  });
  for (const [id, kept] of blocks) if (!wanted.includes(kept.section)) blocks.delete(id);
  place(projects, wanted);
}

let last = null;
function redraw() {
  if (last !== null) draw(last);
}

async function refresh() {
  try {
    const response = await fetch('/setup/media/state');
    if (response.ok) {
      const state = await response.json();
      const key = JSON.stringify(state);
      last = state;
      if (key !== drawn) {
        drawn = key;
        draw(state);
      }
    }
  } catch {
    // The next poll tries again.
  }
  clearTimeout(poll);
  poll = setTimeout(refresh, 1000);
}

refresh();
