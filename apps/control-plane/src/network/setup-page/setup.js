// The setup page (ADR 0053): reads what stands every two seconds, draws every step with its presses,
// and sends a press as one POST from this page. A press asks first, with what it will do; only the
// second press acts. One download runs at a time, with its last lines and a Stop. Above the steps,
// each game shared now (ADR 0054) with Copy, which puts its address on this Mac's clipboard, and Stop.
// A link's address is never drawn, only copied. Drawn in the headset's card language (A17): one
// accent, on the page's one main action; each step a row that opens, one open at a time unless the
// person opens more; a press's question stands right under the press.

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
 * What setup said, as it printed it for a terminal: lines it indented carry on the sentence before them, so they are
 * joined back into one paragraph each, never wrapped twice.
 */
function said(lines) {
  const paragraphs = [];
  for (const raw of lines) {
    const indented = /^\s/.test(raw);
    const line = raw.trim();
    if (line === '') continue;
    const last = paragraphs.at(-1);
    if (indented && last?.indented && !/[.:!?]$/.test(last.text))
      last.text = `${last.text} ${line}`;
    else paragraphs.push({ text: line, indented });
  }
  return paragraphs.map(({ text, indented }) =>
    element('p', indented ? 'said-line quiet-line' : 'said-line', text),
  );
}

let drawnShared = '';
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
  $('optional-block').hidden = optional.length === 0;
}

$('job-stop').addEventListener('click', () => void call('/setup/stop', {}));
$('job-again').addEventListener('click', () => {
  if (state?.job) press('download', state.job.id, false);
});
$('restart-button').addEventListener('click', () => press('restart', null, false));
$('check').addEventListener('click', () => press('check', null, false));

void call('/setup/state');
setInterval(() => void call('/setup/state'), 2000);
