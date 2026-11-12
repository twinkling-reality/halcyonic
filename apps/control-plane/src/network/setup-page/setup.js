// The setup page (ADR 0053): reads what stands every two seconds, draws every step with its presses,
// and sends a press as one POST from this page. A press asks first, with what it will do; only the
// second press acts. One download runs at a time, with its last lines and a Stop. Above the steps,
// each game shared now (ADR 0054) with Copy, which puts its address on this Mac's clipboard, and Stop.
// A link's address is never drawn, only copied. Drawn in the headset's card language (A17): one
// accent, on the page's one main action; each step a row that opens, one open at a time unless the
// person opens more; a press's question stands right under the press. Below the steps, each
// project's secrets by name with Remove, and a name and a hidden field to set one (ADR 0057): a
// value is sent once and never drawn.

const CLOSED = 'This page has closed. Open pairing again on this computer.';
const UNREACHABLE = "This computer's app isn't answering. Check that it is running.";
const RESTARTING = 'Restarting. A new page opens when Halcyonic is running again.';
/** Each step's status: its note's words and its mark's glyph. */
const STATUS = {
  ready: ['Ready', 'tick'],
  todo: ['To Do', 'alert'],
  optional: ['Optional', 'minus'],
  look: ['Look at This', 'eye'],
  unknown: ["Can't Tell Yet", 'question'],
};
const JOB = {
  running: ['Downloading', 'working', 'down'],
  done: ['Done', 'good', 'tick'],
  not_set_up: ['Not Set Up', 'bad', 'alert'],
  failed: ["Didn't Finish", 'bad', 'alert'],
  stopped: ['Stopped', 'quiet', 'stop'],
};
const WAY = { link: ['Link', 'link'], wifi: ['This Wi-Fi', 'wifi'] };
/** Words a title keeps in lower case, but first. */
const SMALL = new Set([
  'a',
  'an',
  'and',
  'as',
  'at',
  'by',
  'for',
  'in',
  'of',
  'on',
  'or',
  'the',
  'to',
  'with',
]);

const $ = (id) => document.getElementById(id);
let state = null;
let problem = null;
/** The steps the person opened or closed, by id; any other stands as the page opens it. */
const opened = new Map();

async function call(path, body) {
  try {
    const response = await fetch(path, {
      method: body === undefined ? 'GET' : 'POST',
      headers: body === undefined ? {} : { 'content-type': 'application/json' },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    if (response.status === 403) {
      problem = CLOSED;
      draw();
      return;
    }
    state = await response.json();
    problem = null;
  } catch {
    problem = state?.restarting ? RESTARTING : UNREACHABLE;
  }
  draw();
}

/** A step's title in title case, as every title on the headset is: setup's own words, cased. */
function titled(words) {
  return words
    .split(' ')
    .map((word, at) =>
      at > 0 && SMALL.has(word.toLowerCase())
        ? word.toLowerCase()
        : word.charAt(0).toUpperCase() + word.slice(1),
    )
    .join(' ');
}

function glyph(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('class', 'glyph');
  svg.setAttribute('aria-hidden', 'true');
  const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
  use.setAttribute('href', `#g-${name}`);
  svg.append(use);
  return svg;
}

function element(tag, className, text) {
  const made = document.createElement(tag);
  if (className) made.className = className;
  if (text !== undefined) made.textContent = text;
  return made;
}

function button(label, onClick, main, icon = null) {
  const made = element('button', main ? 'main' : 'rest');
  made.type = 'button';
  if (icon !== null) made.append(glyph(icon));
  made.append(element('span', 'words', label));
  made.addEventListener('click', onClick);
  return made;
}

function press(action, arg, confirmed) {
  void call('/setup/act', { action, arg, confirmed });
}

/** The status: its tone, its glyph, its title word and its quieter line. */
function status(tone, name, title, line) {
  $('state').dataset.tone = tone;
  $('state-glyph').setAttribute('href', `#g-${name}`);
  $('state-title').textContent = title;
  $('state-line').textContent = line ?? '';
  $('state-line').hidden = line == null;
}

function standing() {
  if (problem === CLOSED) return ['bad', 'alert', 'Page Closed', CLOSED];
  if (problem === RESTARTING || state?.restarting)
    return [
      'working',
      'hourglass',
      'Restarting',
      'A new page opens when Halcyonic is running again.',
    ];
  if (problem !== null) return ['bad', 'alert', 'Not Answering', problem];
  if (state === null) return ['quiet', 'hourglass', 'Reading', null];
  const todo = state.steps.filter((step) => step.status === 'todo').length;
  const look = state.steps.filter((step) => step.status === 'look').length;
  if (todo > 0)
    return [
      'attention',
      'alert',
      `${todo} ${todo === 1 ? 'Step' : 'Steps'} to Do`,
      'The optional steps can wait.',
    ];
  if (look > 0) return ['attention', 'eye', `${look} to Look At`, 'Every step it needs is done.'];
  return [
    'good',
    'tick',
    'Ready for You',
    'Every step it needs is done. The optional steps can wait.',
  ];
}

/** The first step still to do with a press: opened first, and its first press is the page's main action. */
function firstToDo() {
  return state.steps.find((step) => step.status === 'todo' && step.actions.length > 0) ?? null;
}

/**
 * The page's one main action, which alone wears the accent: Go On while a press asks first, else Restart to Apply,
 * else the first step still to do's first press.
 */
function mainAction(running) {
  if (state.restarting) return null;
  if (state.confirm !== null) return 'confirm';
  if (state.restart_needed && state.can_restart && !state.restarting) return 'restart';
  const first = firstToDo();
  if (first === null) return null;
  if (running && first.actions[0].action === 'download') return null;
  return `${first.id}:0`;
}

function asks(action) {
  return (
    state.confirm !== null &&
    state.confirm.action === action.action &&
    state.confirm.arg === action.arg
  );
}

/** The step whose press asks now, so its question stands under that press; null for a restart. */
function askingStep() {
  if (state.confirm === null) return null;
  return state.steps.find((step) => step.actions.some(asks)) ?? null;
}

/** A press that asks first: the thing it acts on as the title, what it will do, then Go On and Cancel in one row. */
/**
 * Text that wraps only at its spaces, never at a hyphen inside a word ("--ignore-scripts", "mac-setup"): each word in a
 * span that keeps it whole, and a word too long for any line (a long path) broken only after its slashes. Adds no
 * characters, so a copied command is the command.
 */
function breakable(made, text) {
  const whole = (words) => element('span', 'whole', words);
  text.split(/( +)/).forEach((word) => {
    if (/^ *$/.test(word)) {
      made.append(word);
      return;
    }
    if (word.length <= 24 || !word.includes('/')) {
      made.append(whole(word));
      return;
    }
    const parts = word.split('/');
    parts.forEach((part, at) => {
      made.append(whole(at < parts.length - 1 ? `${part}/` : part));
      if (at < parts.length - 1) made.append(document.createElement('wbr'));
    });
  });
  return made;
}

/** What a press acts on, for its question's title: the download's own name and version, or the press's words. */
function thing(action) {
  if (action.fetches !== undefined) return action.fetches.split(/ \(|, /)[0];
  return action.label;
}

function question(title) {
  const panel = element('div', 'question');
  const words = element('div', 'question-words');
  words.append(
    element('p', 'question-title', title),
    element('p', 'question-line', state.confirm.text),
  );
  const actions = element('div', 'question-actions');
  actions.append(
    button('Go On', () => press(state.confirm.action, state.confirm.arg, true), true, 'next'),
    button(
      'Cancel',
      () => {
        if (state !== null) state = { ...state, confirm: null };
        drawnSteps = '';
        draw();
      },
      false,
      'close',
    ),
  );
  panel.append(words, actions);
  return panel;
}

/**
 * What setup said, as it printed it for a terminal, read back into its parts. A line it did not indent is a sentence.
 * Lines its `wrap()` broke (indented two or three spaces) are joined back into the sentence they carry on, so it is never
 * wrapped twice: a line joins the one before it at the same indent only while that one is prose (it holds a space) and
 * has not ended its sentence, so a list of names, one a line, stays a list. A line indented four or more is a command,
 * kept exactly as printed, each on its own line, since the person may copy it. Pure, so a test reads it.
 */
function saidParts(lines) {
  const parts = [];
  for (const raw of lines) {
    const indent = raw.length - raw.trimStart().length;
    const text = raw.trim();
    if (text === '') continue;
    const kind = indent === 0 ? 'line' : indent >= 4 ? 'command' : 'quiet';
    const last = parts.at(-1);
    const carriesOn =
      (indent === 2 || indent === 3) &&
      last?.indent === indent &&
      last.text.includes(' ') &&
      !/[.:!?]$/.test(last.text);
    if (carriesOn) last.text = `${last.text} ${text}`;
    else parts.push({ text, kind, indent });
  }
  return parts.map(({ text, kind }) => ({ text, kind }));
}

/** What setup said, drawn: sentences, quieter wrapped ones, and each run of commands in its own well. */
function said(lines) {
  const shown = [];
  let well = null;
  for (const { text, kind } of saidParts(lines)) {
    if (kind === 'command') {
      if (well === null) {
        well = element('div', 'commands');
        shown.push(well);
      }
      well.append(breakable(element('code', 'command'), text));
      continue;
    }
    well = null;
    shown.push(element('p', kind === 'quiet' ? 'said-line quiet-line' : 'said-line', text));
  }
  return shown;
}

let drawnShared = '';
let drawnSecrets = '';
function drawShared() {
  const shared = state.shared ?? [];
  $('shared').hidden = shared.length === 0;
  // Drawn again only when what is shared changes, so a poll never swaps a button under a press.
  const drawn = JSON.stringify(shared);
  if (drawn === drawnShared) return;
  drawnShared = drawn;
  $('shared-list').replaceChildren(
    ...shared.map((share) => {
      const [way, icon] = WAY[share.way] ?? [share.way, 'link'];
      const item = element('li', 'share');
      const tile = element('span', 'share-tile');
      tile.append(glyph(icon));
      const words = element('span', 'share-words');
      words.append(element('span', 'share-name', share.name), element('span', 'share-way', way));
      if (share.address_text !== null)
        words.append(element('span', 'share-address', share.address_text));
      const actions = element('span', 'share-actions');
      const copy = button(
        'Copy',
        async () => {
          const label = copy.querySelector('.words');
          try {
            await navigator.clipboard.writeText(share.url);
            label.textContent = 'Copied';
          } catch {
            label.textContent = "Couldn't Copy";
          }
          setTimeout(() => {
            label.textContent = 'Copy';
          }, 2000);
        },
        false,
        'copy',
      );
      const stop = button(
        'Stop',
        () => void call('/setup/shares/stop', { project_id: share.project_id }),
        false,
        'stop',
      );
      actions.append(copy, stop);
      item.append(tile, words, actions);
      return item;
    }),
  );
}

/** Names as one phrase: "A", "A and B", "A, B and C". */
function listed(names) {
  if (names.length <= 1) return names.join('');
  return `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`;
}

/** Each project card's status line and each secret's question, by project and name, updated in place. */
const secretParts = new Map();

/** Whether the person opened Project Secrets; a refusal or a Remove that asks keeps it open, where they pressed. */
let secretsOpen = false;
/**
 * The one project whose form stands open, so the page has one place to type at a time: the only project when there is
 * one (no press needed), else the one whose Add Secret was pressed last, or where a refusal was just said.
 */
let formOpenFor = null;
$('secrets-row').addEventListener('click', () => {
  secretsOpen = !secretsOpen;
  drawSecrets();
});

function drawSecrets() {
  const secrets = state.secrets ?? null;
  $('secrets').hidden = secrets === null || secrets.projects.length === 0;
  if (secrets === null) return;
  const open = secretsOpen || secrets.problem != null || secrets.confirm != null;
  $('secrets-row').setAttribute('aria-expanded', String(open));
  $('secrets-body').hidden = !open;
  const without = secrets.without ?? [];
  $('secrets-some').hidden = without.length === 0;
  $('secrets-some').textContent =
    `${listed(without)} ${without.length === 1 ? 'starts' : 'start'} without these secrets. ` +
    'Start a task that needs one on another agent.';
  const outside = secrets.outside ?? [];
  $('secrets-open').hidden = outside.length === 0;
  $('secrets-open-line').textContent =
    `${listed(outside)}, on a folder you connected, ${outside.length === 1 ? 'runs' : 'run'} ` +
    "outside the sandbox and can read every project's secrets on this computer. Give it test keys only.";
  // Drawn again only when the names change, so a poll never clears a field being typed in.
  const drawn = JSON.stringify(secrets.projects);
  if (drawn !== drawnSecrets) {
    drawnSecrets = drawn;
    secretParts.clear();
    $('secrets-list').replaceChildren(...secrets.projects.map(secretProject));
  }
  // What a press said, and a Remove that asks first: in place, never by drawing the card again. While a Remove
  // asks, it is the one decision on the page: every card's other Removes and forms wait.
  const asking = secrets.confirm != null;
  const only = secrets.projects.length === 1 ? secrets.projects[0].project_id : null;
  if (secrets.problem?.project_id) formOpenFor = secrets.problem.project_id;
  const formFor = only ?? formOpenFor;
  for (const [projectId, parts] of secretParts) {
    const problem = secrets.problem?.project_id === projectId ? secrets.problem.text : null;
    parts.status.hidden = problem === null;
    parts.status.textContent = problem ?? '';
    for (const [name, asked] of parts.rows) {
      const confirm =
        secrets.confirm?.project_id === projectId && secrets.confirm.name === name
          ? secrets.confirm
          : null;
      asked.panel.hidden = confirm === null;
      // The name as the rows show it, in mono, inside the host's own sentence.
      asked.line.replaceChildren(...namedIn(confirm?.text ?? '', name));
      asked.remove.hidden = asking;
    }
    const formHere = !asking && formFor === projectId;
    parts.form.hidden = !formHere;
    // An open form says what "No Secrets Yet" would: one or the other.
    parts.none.hidden = !parts.empty || formHere;
    parts.add.hidden = asking || formHere || only !== null;
  }
}

/** A sentence with each mention of a secret's name set as the rows set it. */
function namedIn(text, name) {
  const parts = [];
  text.split(name).forEach((piece, at) => {
    if (at > 0) parts.push(secretName(name));
    if (piece.length > 0) parts.push(piece);
  });
  return parts;
}

/** A secret's name, which may break after each underscore and nowhere else. */
function secretName(name) {
  const made = element('code', 'secret-name');
  name.split(/(?<=_)/).forEach((part, at) => {
    if (at > 0) made.append(document.createElement('wbr'));
    made.append(part);
  });
  return made;
}

function secretProject(project) {
  const item = element('li', 'secret-project');
  item.append(element('p', 'secret-project-name', project.name));
  const rows = new Map();
  const none = element('p', 'secret-none', 'No Secrets Yet');
  none.hidden = project.secrets.length > 0;
  item.append(none);
  for (const name of project.secrets) {
    // A list row: the key mark, the name, and a quiet Remove at the right.
    const row = element('div', 'secret-row');
    const tile = element('span', 'secret-tile');
    tile.append(glyph('key'));
    const remove = button(
      'Remove',
      () =>
        void call('/setup/secrets/remove', {
          project_id: project.project_id,
          name,
          confirmed: false,
        }),
      false,
      'close',
    );
    remove.classList.add('secret-remove');
    row.append(tile, secretName(name), remove);
    item.append(row);
    // The question stands right under the press, as the steps' do.
    const panel = element('div', 'question');
    panel.hidden = true;
    const words = element('div', 'question-words');
    const line = element('p', 'question-line', '');
    words.append(element('p', 'question-title', 'Remove This Secret?'), line);
    const actions = element('div', 'question-actions');
    // The second press reads as the consequence it is, armed in the failing tone, as Forget's and Turn Off's do.
    const really = button(
      'Yes, Remove',
      () =>
        void call('/setup/secrets/remove', {
          project_id: project.project_id,
          name,
          confirmed: true,
        }),
      false,
      'close',
    );
    really.dataset.armed = '';
    actions.append(
      really,
      button('Cancel', () => void call('/setup/secrets/cancel', {}), false),
    );
    panel.append(words, actions);
    item.append(panel);
    rows.set(name, { panel, line, remove });
  }
  // Each field under its own caption; the name's example is only an example, the value shows nothing.
  const form = element('form', 'secret-form');
  const field = (caption, input) => {
    const label = element('label', 'secret-field');
    label.append(element('span', 'secret-caption', caption), input);
    return label;
  };
  const named = element('input');
  named.type = 'text';
  named.placeholder = 'API_KEY';
  named.autocomplete = 'off';
  named.spellcheck = false;
  named.className = 'secret-typed-name';
  const value = element('input');
  value.type = 'password';
  value.autocomplete = 'new-password';
  // The open form's Set is the page's one accent there: the one place to start.
  const set = button('Set', () => undefined, true, 'key');
  set.type = 'submit';
  form.append(field('Name', named), field('Value', value), set);
  form.hidden = true;
  // Escape closes a form opened by its Add Secret, where several projects share the page; nothing is sent.
  form.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape' || formOpenFor !== project.project_id) return;
    formOpenFor = null;
    drawSecrets();
  });
  // With several projects, a card's form opens by its own press, and only one stands open at a time.
  const add = button(
    'Add Secret',
    () => {
      formOpenFor = project.project_id;
      drawSecrets();
      named.focus();
    },
    false,
    'key',
  );
  add.classList.add('secret-add');
  item.append(add);
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    const typed = value.value;
    // Cleared before it is sent, whatever the answer: the page never keeps a value. The name stays.
    value.value = '';
    void call('/setup/secrets/set', {
      project_id: project.project_id,
      name: named.value.trim(),
      value: typed,
    }).then(() => {
      // Kept: the form closes to its quiet Add Secret where several projects share the page. Refused: it stays.
      if (
        state?.secrets?.problem?.project_id !== project.project_id &&
        formOpenFor === project.project_id
      ) {
        formOpenFor = null;
        drawSecrets();
      }
    });
  });
  item.append(form);
  // A refusal is said here, under the fields, where the person pressed Set.
  const status = element('p', 'secret-status', '');
  status.hidden = true;
  status.setAttribute('role', 'status');
  item.append(status);
  secretParts.set(project.project_id, {
    status,
    rows,
    form,
    add,
    none,
    empty: project.secrets.length === 0,
  });
  return item;
}

function stepItem(step, running, main, open) {
  const [note, icon] = STATUS[step.status] ?? [step.status, 'question'];
  // Under the Optional heading, an optional step's note would only say it again.
  const shownNote = step.status === 'optional' ? '' : note;
  const item = element('li', 'step');
  item.dataset.status = step.status;
  const row = element('button', 'step-row');
  row.type = 'button';
  row.setAttribute('aria-expanded', String(open));
  const tile = element('span', 'step-tile');
  tile.append(glyph(icon));
  const chevron = glyph('next');
  chevron.setAttribute('class', 'glyph chevron');
  row.append(
    tile,
    element('span', 'step-name', titled(step.title)),
    element('span', 'step-note', shownNote),
    chevron,
  );
  row.addEventListener('click', () => {
    opened.set(step.id, !open);
    drawnSteps = '';
    draw();
  });
  item.append(row);
  if (!open) return item;
  const body = element('div', 'step-body');
  for (const line of step.lines) body.append(breakable(element('p', 'step-line'), line));
  if ((step.next ?? []).length > 0) {
    // Commands for the person to run in Terminal, as setup prints them: text, never run from here.
    const label = element('p', 'panel-label step-next');
    label.append(glyph('terminal'), 'In Terminal');
    const commands = element('div', 'commands');
    for (const command of step.next)
      commands.append(breakable(element('code', 'command'), command));
    body.append(label, commands);
  }
  if (step.actions.length > 0) {
    const actions = element('div', 'step-actions');
    step.actions.forEach((action, index) => {
      const block = element('div', 'step-action');
      const made = button(
        action.label,
        () => press(action.action, action.arg, false),
        main === `${step.id}:${index}`,
        action.action === 'download' ? 'down' : 'next',
      );
      made.disabled = (running && action.action === 'download') || state.restarting;
      if (asks(action)) made.dataset.pressed = '';
      block.append(made);
      // While its question stands, the question says what the caption would.
      if (action.fetches !== undefined && !asks(action))
        block.append(element('span', 'fetches', action.fetches));
      // The question stands right under the press that asked it.
      if (asks(action)) block.append(question(thing(action)));
      actions.append(block);
    });
    body.append(actions);
  }
  item.append(body);
  return item;
}

let drawnSteps = '';
function draw() {
  const gone = problem !== null;
  const running = !gone && state !== null && state.job !== null && state.job.state === 'running';
  status(...standing());
  for (const id of ['restart', 'confirm-top', 'job', 'said', 'shared', 'check', 'pairing-link'])
    $(id).hidden = gone;
  $('steps-block').hidden = gone || state === null;
  $('optional-block').hidden = gone || state === null;
  if (gone || state === null) return;

  const main = mainAction(running);
  $('check').hidden = state.restarting;
  // A restart that asks stands in the restart's own panel, in place of its button.
  const restartAsks = state.confirm?.action === 'restart';
  $('restart').hidden = !state.restart_needed || state.restarting;
  $('restart-button').hidden = !state.can_restart || restartAsks;
  $('restart-button').className = main === 'restart' ? 'main' : 'rest';
  $('restart-by-hand').hidden = state.can_restart;
  $('restart-asks').hidden = !restartAsks;
  if (restartAsks) $('restart-asks').replaceChildren(question('Restart Halcyonic'));

  const step = askingStep();
  const top = $('confirm-top');
  top.hidden = state.confirm === null || step !== null || restartAsks;
  if (!top.hidden) top.replaceChildren(question('This Computer'));

  $('job').hidden = state.job === null;
  if (state.job !== null) {
    const [words, tone, icon] = JOB[state.job.state] ?? [state.job.state, 'quiet', 'down'];
    $('job-mark').dataset.tone = tone;
    $('job-glyph').setAttribute('href', `#g-${icon}`);
    $('job-title').textContent = state.job.title;
    $('job-state').textContent = words;
    $('job-time').textContent = `${state.job.seconds} s`;
    $('job-lines').textContent = state.job.lines.join('\n');
    $('job-lines').hidden = state.job.lines.length === 0;
    $('job-stop').hidden = !running;
    // A download that did not finish can be asked for again, as its own step's press would.
    const again = state.job.state === 'failed' || state.job.state === 'stopped';
    $('job-again').hidden = !again;
    $('job-actions').hidden = !running && !again;
  }
  $('said').hidden = state.said.length === 0;
  $('said-lines').replaceChildren(...said(state.said));
  drawShared();
  drawSecrets();
  // Optional holds the optional steps and Project Secrets: it stands while either does.
  $('optional-block').hidden =
    !state.steps.some((each) => each.status === 'optional') && $('secrets').hidden;

  // Drawn again only when the steps change, so a poll never swaps a button under a press.
  const drawn = JSON.stringify([state.steps, running, state.confirm, main, [...opened]]);
  if (drawn === drawnSteps) return;
  drawnSteps = drawn;
  const first = firstToDo();
  const isOpen = (each) =>
    opened.get(each.id) ?? (each === step || (step === null && each === first));
  const needed = state.steps.filter((each) => each.status !== 'optional');
  const optional = state.steps.filter((each) => each.status === 'optional');
  $('steps').replaceChildren(...needed.map((each) => stepItem(each, running, main, isOpen(each))));
  $('optional-steps').replaceChildren(
    ...optional.map((each) => stepItem(each, running, main, isOpen(each))),
  );
  $('steps-block').hidden = needed.length === 0;
}

$('job-stop').addEventListener('click', () => void call('/setup/stop', {}));
$('job-again').addEventListener('click', () => {
  if (state?.job) press('download', state.job.id, false);
});
$('restart-button').addEventListener('click', () => press('restart', null, false));
$('check').addEventListener('click', () => press('check', null, false));

void call('/setup/state');
setInterval(() => void call('/setup/state'), 2000);
