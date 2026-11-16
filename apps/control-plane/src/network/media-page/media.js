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
/** The file a project's incoming tile stands for, its name and whether it is a video, by project id. */
const chosen = new Map();
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
  chosen.set(projectId, {
    name: file.name,
    video: file.type.startsWith('video/') || /\.(mov|mp4)$/i.test(file.name),
  });
  told.delete(projectId);
  redraw();
  const request = new XMLHttpRequest();
  request.open('POST', `/setup/media/add?project=${encodeURIComponent(projectId)}`);
  request.setRequestHeader('content-type', 'application/octet-stream');
  request.upload.addEventListener('progress', (event) => {
    if (!event.lengthComputable || !sending.has(projectId)) return;
    sending.set(projectId, event.loaded / event.total);
    const bar = document.querySelector(`[data-project="${CSS.escape(projectId)}"] .media-bar-fill`);
    if (bar) bar.style.transform = `scaleX(${sending.get(projectId)})`;
  });
  const finish = async (words) => {
    // The incoming tile stays while the host reads the state, so it never blinks out before the host's own does.
    await refresh();
    sending.delete(projectId);
    if (words !== null) told.set(projectId, words);
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
  const bar = el('span', 'media-bar');
  bar.append(el('span', 'media-bar-fill'));
  node.append(sheen, mark, words, name, bar);
  return node;
}

function setIncoming(node, fraction, file) {
  const sent = fraction !== null;
  const kind = file?.video === true ? 'g-video' : 'g-photo';
  const mark = node.querySelector('.media-incoming-mark');
  if (mark.dataset.kind !== kind) {
    mark.dataset.kind = kind;
    mark.replaceChildren(glyph(kind));
  }
  node.querySelector('.media-incoming-name').textContent = file?.name ?? '';
  node.classList.toggle('media-sending', sent);
  node.querySelector('.media-incoming-words').textContent = sent ? 'Sending' : 'Preparing';
  node.querySelector('.media-bar-fill').style.transform = `scaleX(${sent ? fraction : 1})`;
}

function tile(projectId, medium) {
  const node = el('div', medium.video ? 'media-tile media-video' : 'media-tile');
  node.style.setProperty('--ratio', '0.75');
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
  media.forEach((medium, index) => {
    if (index === waiting) return;
    const key = `${medium.path}|${medium.still}|${medium.play}|${medium.prepared}`;
    const node = kept.tiles.get(key) ?? tile(project.project_id, medium);
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
