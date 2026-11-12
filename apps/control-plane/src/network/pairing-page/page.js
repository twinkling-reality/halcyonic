// The pairing page (ADR 0047): reads what stands every second, draws it, and sends a press as one
// POST from this page. A press that asks first (turning pairing off, forgetting a headset) arms for
// six seconds and acts only on its second press. Drawn in the headset's card language (A17): a
// status as a title word over a quieter line, facts as labelled slots, tries as pips, list rows.

const ARM_MS = 6000;
/** How long "Paired" stands after a headset pairs, before the list alone says it. */
const PAIRED_MS = 5000;
const CLOSED = 'This page has closed. Open pairing again on this computer.';
const UNREACHABLE = "This computer's app isn't answering. Check that it is running.";

const $ = (id) => document.getElementById(id);
let state = null;
let armed = null;
let armTimer = null;
/** The pairing whose "Paired" status is standing, by its window, and until when. */
let paired = { window: null, until: 0 };

function arm(what) {
  armed = what;
  clearTimeout(armTimer);
  armTimer = setTimeout(() => {
    armed = null;
    draw();
  }, ARM_MS);
  draw();
}

function disarm() {
  armed = null;
  clearTimeout(armTimer);
}

async function send(path, body) {
  disarm();
  try {
    const response = await fetch(path, {
      method: 'POST',
      headers: body === undefined ? {} : { 'content-type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: 'same-origin',
    });
    if (response.ok) state = await response.json();
    else state = { ...(state ?? {}), problem: CLOSED };
  } catch {
    state = { ...(state ?? {}), problem: UNREACHABLE };
  }
  draw();
}

async function read() {
  try {
    const response = await fetch('/state', { credentials: 'same-origin' });
    if (response.ok) state = await response.json();
    else state = { ...(state ?? {}), closed: true };
  } catch {
    state = { ...(state ?? {}), unreachable: true };
  }
  draw();
}

function left(seconds) {
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

function show(id, visible) {
  $(id).hidden = !visible;
}

/** Pips, a game's lives: filled where used, hollow where left. */
function pips(element, of, used) {
  element.replaceChildren(
    ...Array.from({ length: of }, (_, at) => {
      const pip = document.createElement('span');
      pip.className = at < used ? 'pip used' : 'pip';
      return pip;
    }),
  );
  const tries = of - used;
  element.setAttribute('aria-label', `${tries} ${tries === 1 ? 'try' : 'tries'} left`);
}

/** The status: its tone, its glyph, its title word and its quieter line; tries as pips where they ran out. */
function status(tone, glyph, title, line, tries = null) {
  $('state').dataset.tone = tone;
  $('state-glyph').setAttribute('href', `#g-${glyph}`);
  $('state-title').textContent = title;
  $('state-line').textContent = line ?? '';
  show('state-line', line != null);
  if (tries !== null) pips($('state-pips'), tries.of, tries.used);
  show('state-pips', tries !== null);
}

/**
 * What the last press came to when it did not do what it said: the computer's own sentence as the status's line,
 * under a title word of the page's. A sentence the page does not know keeps its words under a plain title.
 */
const PROBLEMS = {
  "Pairing couldn't turn on. Another app may be using its connection. Try again.": [
    "Couldn't Turn On",
    'Another app may be using its connection. Try again.',
  ],
  "Pairing couldn't turn off. Quit the app on this computer and start it again.": [
    "Couldn't Turn Off",
    'Quit the app on this computer and start it again.',
  ],
  "That headset isn't paired with this computer.": [
    'Not Paired Here',
    "That headset isn't paired with this computer.",
  ],
  [CLOSED]: ['Page Closed', CLOSED],
  [UNREACHABLE]: ['Not Answering', UNREACHABLE],
};

/** What the last window came to: "Paired" for a few seconds, or a window that ended without one. */
function outcomeOf(window) {
  if (window === null || window === undefined) return null;
  if (window.state === 'paired') {
    if (paired.window !== window.expires_at) {
      paired = { window: window.expires_at, until: Date.now() + PAIRED_MS };
    }
    return Date.now() < paired.until ? 'paired' : null;
  }
  if (window.state === 'expired') return 'expired';
  if (window.state === 'locked') return 'locked';
  return null;
}

/** A count in words, as a sentence says it: the window's own number of tries, never a fixed one. */
function count(number) {
  const words = [
    'No',
    'One',
    'Two',
    'Three',
    'Four',
    'Five',
    'Six',
    'Seven',
    'Eight',
    'Nine',
    'Ten',
  ];
  return words[number] ?? String(number);
}

/**
 * Where pairing stands, as the status says it: its tone, glyph, title word, quieter line and, where the tries ran out,
 * its pips. The last press's problem first, then a turn under way, then the listener and its window.
 */
function standing(on, open, reachable, outcome) {
  const problem = state.problem ?? null;
  if (problem !== null) return ['bad', 'alert', ...(PROBLEMS[problem] ?? ["Didn't Work", problem])];
  if (state.turning === 'on')
    return [
      'working',
      'hourglass',
      'Turning On',
      'Opening this computer to headsets on your Wi-Fi.',
    ];
  if (state.turning === 'off')
    return ['working', 'hourglass', 'Turning Off', 'Paired headsets stay paired.'];
  if (!on)
    return [
      'quiet',
      'off',
      'Pairing Off',
      state.headsets.length > 0
        ? 'Turn On Pairing lets your paired headsets back in, with no new code.'
        : 'No headset can join until you pair one.',
    ];
  if (open && !reachable)
    return ['bad', 'alert', 'No Wi-Fi Address', 'Join Wi-Fi on this computer, then pair.'];
  if (open)
    return [
      'working',
      'hourglass',
      'Waiting for Your Headset',
      'The code works once, for one headset.',
    ];
  if (outcome === 'paired')
    return [
      'good',
      'tick',
      'Paired',
      `${state.window.device ?? 'A headset'} joined this computer.`,
    ];
  if (outcome === 'locked') {
    const tries = state.window.max_failed_attempts;
    return [
      'bad',
      'alert',
      'No Tries Left',
      `${count(tries)} wrong ${tries === 1 ? 'code' : 'codes'} closed pairing. Pair again for a new code.`,
      { of: tries, used: tries },
    ];
  }
  if (outcome === 'expired')
    return ['quiet', 'clock', 'Code Timed Out', 'The code ran out. Pair again for a new one.'];
  return ['good', 'tick', 'Pairing On', 'Pair Headset gives a code for one headset on your Wi-Fi.'];
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

function headsetRow(headset) {
  const row = document.createElement('li');
  row.className = 'item';
  const tile = document.createElement('span');
  tile.className = 'item-tile';
  tile.append(glyph('headset'));
  const words = document.createElement('span');
  words.className = 'item-words';
  const name = document.createElement('span');
  name.className = 'item-name';
  name.textContent = headset.label;
  const note = document.createElement('span');
  note.className = 'item-note';
  note.textContent = headset.connected ? 'Connected' : 'Not Connected';
  if (headset.connected) note.dataset.tone = 'good';
  words.append(name, note);
  const forget = document.createElement('button');
  forget.type = 'button';
  forget.className = 'rest';
  const asking = armed === `forget:${headset.id}`;
  forget.append(glyph('close'), asking ? 'Yes, Forget' : 'Forget');
  if (asking) forget.dataset.armed = '';
  forget.disabled = state.turning !== null;
  forget.addEventListener('click', () =>
    asking ? send('/headsets/forget', { id: headset.id }) : arm(`forget:${headset.id}`),
  );
  row.append(tile, words, forget);
  return row;
}

function draw() {
  if (state === null) return;
  const gone = state.closed === true || state.unreachable === true;
  show('setup-link', !gone);
  if (gone) {
    status(
      'bad',
      'alert',
      state.closed === true ? 'Page Closed' : 'Not Answering',
      state.closed === true ? CLOSED : UNREACHABLE,
    );
    for (const id of ['window', 'main-actions', 'hint', 'headsets-block', 'foot']) show(id, false);
    return;
  }
  const on = state.pairing === 'on';
  const open = on && state.window?.state === 'open' && state.shown === true;
  const reachable = state.names.length > 0 && state.code !== null;
  const outcome = open ? null : outcomeOf(state.window);

  status(...standing(on, open, reachable, outcome));

  const busy = state.turning !== null;
  show('window', open && reachable && !busy);
  show('main-actions', !busy && (!on || !open));
  // Pair Headset turns pairing on by itself, so from off it is the one press; Turn On Pairing stands
  // beside it only to bring back headsets already paired, without a new code.
  show('turn-on', !on && state.headsets.length > 0);
  show('pair', !open);
  show('hint', !on && !busy);
  for (const id of ['turn-on', 'pair', 'turn-off']) $(id).disabled = busy;
  // While Turn Off asks again, nothing else wears a colour: Pair Headset waits, and Keep On takes it back.
  if (armed === 'off') $('pair').disabled = true;

  show('headsets-block', on && !open);
  $('headsets').replaceChildren(...state.headsets.map(headsetRow));
  show('none', state.headsets.length === 0);

  show('foot', on || state.turning === 'off');
  const turningOff = armed === 'off';
  $('turn-off-words').textContent = turningOff ? 'Yes, Turn Off' : 'Turn Off Pairing';
  if (turningOff) $('turn-off').dataset.armed = '';
  else delete $('turn-off').dataset.armed;
  show('off-note', turningOff);
  show('keep-on', turningOff);

  if (open && reachable) {
    // Only a name a headset can reach is shown, and the code only beside one.
    const [first, ...others] = state.names;
    $('name').textContent = first;
    $('others-list').replaceChildren(
      ...others.map((name) => {
        const item = document.createElement('li');
        item.textContent = name;
        return item;
      }),
    );
    show('others', others.length > 0);
    $('code').textContent = `${state.code.slice(0, 4)} ${state.code.slice(4)}`;
    $('time').textContent = left(state.window.seconds_left);
    const of = state.window.max_failed_attempts;
    pips($('pips'), of, Math.min(of, state.window.failed_attempts));
  }
}

$('turn-on').addEventListener('click', () => send('/pairing/on'));
$('pair').addEventListener('click', () => send('/window'));
$('keep-on').addEventListener('click', () => {
  disarm();
  draw();
});
$('turn-off').addEventListener('click', () =>
  armed === 'off' ? send('/pairing/off') : arm('off'),
);

read();
setInterval(read, 1000);
