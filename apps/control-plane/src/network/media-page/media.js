// The Mac's Photos and Videos page (ADR 0066): draws the host's state as it is, every word from it. A file the person
// chooses or drops goes to the host as raw bytes; the host decides what it is. Names are set as text, never markup.

const projects = document.getElementById('projects');
const nothing = document.getElementById('nothing');
let poll = null;
let drawn = '';

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

async function send(projectId, file) {
  try {
    const response = await fetch(`/setup/media/add?project=${encodeURIComponent(projectId)}`, {
      method: 'POST',
      headers: { 'content-type': 'application/octet-stream' },
      body: file,
    });
    if (response.status === 413) return show(projectId, 'That file is too large to add.');
    const answer = await response.json().catch(() => null);
    await refresh();
    if (answer && typeof answer.said === 'string') show(projectId, answer.said);
    return;
  } catch {
    return show(projectId, "That file couldn't be sent. Try again.");
  }
}

/** A line under the project's head until the next state. */
function show(projectId, words) {
  const line = document.querySelector(`[data-said="${CSS.escape(projectId)}"]`);
  if (line) {
    line.hidden = false;
    line.lastChild.textContent = words;
  }
}

function tile(project, medium) {
  const node = el('div', medium.video ? 'media-tile media-video' : 'media-tile');
  if (medium.still !== null) {
    const image = el('img', 'media-still');
    image.alt = '';
    image.decoding = 'async';
    // Shown only once whole, so a tile never draws half a picture.
    image.addEventListener('load', () => node.classList.add('media-shown'));
    image.src = `/setup/media/still?project=${encodeURIComponent(project.project_id)}&path=${encodeURIComponent(medium.still)}`;
    node.append(image);
  } else {
    node.append(glyph(medium.prepared ? 'g-photo' : 'g-hourglass'));
    node.classList.add('media-shown', 'media-bare');
  }
  if (medium.video) {
    const mark = el('span', 'media-mark');
    mark.append(glyph('g-play'));
    if (medium.seconds !== null) mark.append(el('span', 'media-seconds', `${medium.seconds} s`));
    node.append(mark);
  }
  return node;
}

function card(project) {
  const section = el('section', 'block media-project');
  section.dataset.project = project.project_id;
  const head = el('div', 'media-head');
  head.append(el('h2', 'media-name', project.name));
  const input = el('input', 'media-file');
  input.type = 'file';
  input.accept = 'image/png,image/jpeg,image/webp,image/heic,.heic,video/mp4,video/quicktime,.mov';
  input.hidden = true;
  input.addEventListener('change', () => {
    const file = input.files?.[0];
    if (file) send(project.project_id, file);
    input.value = '';
  });
  const add = el('button', 'main');
  add.type = 'button';
  add.disabled = project.adding;
  add.append(
    glyph(project.adding ? 'g-hourglass' : 'g-plus'),
    project.adding ? 'Adding' : 'Add Media',
  );
  add.addEventListener('click', () => input.click());
  head.append(input, add);
  section.append(head);

  const said = el('p', 'media-said');
  said.dataset.said = project.project_id;
  said.append(glyph('g-alert'), el('span', '', project.said ?? ''));
  said.hidden = project.said === null;
  section.append(said);

  if (project.media.length === 0) {
    section.append(el('p', 'media-line', 'Choose a photo or video, or drop one here.'));
  } else {
    const grid = el('div', 'media-grid');
    for (const medium of project.media) grid.append(tile(project, medium));
    section.append(grid);
  }

  // The whole card takes a dropped file.
  section.addEventListener('dragover', (event) => {
    event.preventDefault();
    section.classList.add('media-drop');
  });
  section.addEventListener('dragleave', () => section.classList.remove('media-drop'));
  section.addEventListener('drop', (event) => {
    event.preventDefault();
    section.classList.remove('media-drop');
    const file = event.dataTransfer?.files?.[0];
    if (file && !project.adding) send(project.project_id, file);
  });
  return section;
}

function draw(state) {
  const key = JSON.stringify(state);
  if (key === drawn) return;
  drawn = key;
  nothing.hidden = state.projects.length > 0;
  projects.replaceChildren(...state.projects.map(card));
}

async function refresh() {
  try {
    const response = await fetch('/setup/media/state');
    if (response.ok) draw(await response.json());
  } catch {
    // The next poll tries again.
  }
  clearTimeout(poll);
  poll = setTimeout(refresh, 1500);
}

refresh();
