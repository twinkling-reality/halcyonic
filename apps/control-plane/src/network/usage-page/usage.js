// The Mac's Usage page: draws the host's state as it is, every word from it; nothing is counted or priced here.
// Every name and title is set as text, never as markup.

const projects = document.getElementById('projects');
const nothing = document.getElementById('nothing');
const assistant = document.getElementById('assistant');
const problem = document.getElementById('problem');
let poll = null;

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

/** A figure: its value, then each small line under it. */
function figure(into, value) {
  const cell = el('div', value.known ? 'figure' : 'figure figure-word');
  cell.append(el('span', 'figure-value', value.value));
  for (const note of value.notes) cell.append(el('span', 'figure-note', note));
  into.append(cell);
}

function slot(label, value) {
  const node = el('div', 'slot');
  node.append(el('p', 'slot-label', label));
  figure(node, value);
  return node;
}

function card(project) {
  const section = el('section', 'block usage-project');
  section.append(el('h2', 'usage-name', project.name));
  const slots = el('div', 'slots');
  slots.append(slot('Tokens Used', project.tokens), slot('Estimated Cost', project.cost));
  section.append(slots);

  const wrap = el('div', 'usage-scroll');
  const table = el('table', 'usage-tasks');
  const head = el('tr');
  const heads = ['Task', 'Agent App', 'Tokens Used', 'Estimated Cost'];
  if (project.limits) heads.push('Limit');
  for (const name of heads) head.append(el('th', '', name));
  const thead = el('thead');
  thead.append(head);
  table.append(thead);
  const body = el('tbody');
  for (const task of project.tasks) {
    const row = el('tr');
    // Each cell carries its column's name, so a narrow window draws each task as its own card with its labels.
    const labelled = (cell, index) => {
      cell.dataset.label = heads[index];
      return cell;
    };
    row.append(
      labelled(el('td', 'usage-task', task.title), 0),
      labelled(el('td', 'usage-app', task.app ?? ''), 1),
    );
    [task.tokens, task.cost].forEach((value, index) => {
      const cell = labelled(el('td', 'usage-figure'), index + 2);
      figure(cell, value);
      row.append(cell);
    });
    if (project.limits) {
      const cell = labelled(el('td', 'usage-figure'), 4);
      if (task.limit !== null) figure(cell, task.limit);
      row.append(cell);
    }
    body.append(row);
  }
  table.append(body);
  wrap.append(table);
  section.append(wrap);
  if (project.estimates !== null) section.append(el('p', 'usage-estimates', project.estimates));

  // The project's tokens by kind, under its tasks: each part with "At least this much" where a task left it out.
  if (project.breakdown !== null) {
    const details = el('details', 'usage-breakdown');
    details.append(el('summary', '', "The Project's Tokens by Kind"));
    const table = el('table', 'usage-parts');
    const partsHead = el('tr');
    partsHead.append(el('th', '', 'Kind'), el('th', 'usage-number', 'Tokens'));
    const partsThead = el('thead');
    partsThead.append(partsHead);
    table.append(partsThead);
    for (const part of project.breakdown) {
      const row = el('tr');
      const value = el('td', 'usage-number');
      value.append(el('span', 'figure-value', part.value));
      if (part.note !== null) value.append(el('span', 'figure-note', part.note));
      row.append(el('th', '', part.label), value);
      table.append(row);
    }
    details.append(table);
    section.append(details);
  }
  return section;
}

/** Keep each open Breakdown open across a redraw. */
function openNames() {
  return new Set(
    [...projects.querySelectorAll('details[open]')].map((node) => node.dataset.project),
  );
}

function draw(state) {
  const open = openNames();
  projects.replaceChildren(
    ...state.projects.map((project) => {
      const section = card(project);
      const details = section.querySelector('details');
      if (details) {
        details.dataset.project = project.project_id;
        details.open = open.has(project.project_id);
      }
      return section;
    }),
  );
  nothing.hidden = state.projects.length > 0;
  assistant.hidden = state.assistant === null;
  assistant.textContent = state.assistant ?? '';
}

// The setup page's own words for the same two cases.
const CLOSED = 'This page has closed. Open pairing again on this computer.';
const UNREACHABLE = "This computer's app isn't answering. Check that it is running.";

function say(words) {
  problem.textContent = words ?? '';
  problem.hidden = words === null;
}

async function read() {
  let response;
  try {
    response = await fetch('/setup/usage/state');
  } catch {
    say(UNREACHABLE);
    return;
  }
  if (response.status === 403) {
    // Its session is gone for good: say so once and ask nothing more.
    say(CLOSED);
    if (poll !== null) clearInterval(poll);
    poll = null;
    return;
  }
  try {
    if (!response.ok) throw new Error(String(response.status));
    draw(await response.json());
    say(null);
  } catch {
    say(UNREACHABLE);
  }
}

void read();
poll = setInterval(() => void read(), 3000);
