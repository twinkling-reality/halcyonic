// The pairing page (ADR 0047): reads what stands every second, draws it, and sends a press as one
// POST from this page. A press that asks first (turning pairing off, forgetting a headset) arms for
// six seconds and acts only on its second press.

/** Where pairing is in the headset, as the build that ships shows it: one place to change it. */
const ROUTE = 'In Headset · Settings · Pair Computer';
const ARM_MS = 6000;
/** How long "Paired" stands after a headset pairs, before the list alone says it. */
const PAIRED_MS = 5000;
const CLOSED = 'This page has closed. Open pairing again on this computer.';
const UNREACHABLE = "This computer's app isn't answering. Check that it is running.";

const $ = (id) => document.getElementById(id);
let state = null;
let armed = null;
let armTimer = null;
/** The pairing whose "Paired" line is standing, by its window, and until when. */
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

/** What the last window came to: "Paired" for a few seconds, or a window that ended without one. */
function outcomeOf(window) {
  if (window === null) return null;
  if (window.state === 'paired') {
    if (paired.window !== window.expires_at) {
      paired = { window: window.expires_at, until: Date.now() + PAIRED_MS };
    }
    return Date.now() < paired.until ? `Paired · ${window.device ?? 'Headset'}` : null;
  }
  if (window.state === 'expired') return 'Pairing Timed Out';
  if (window.state === 'locked') return 'No Tries Left · Pairing Closed';
  return null;
}

function draw() {
  if (state === null) return;
  const status = $('status');
  if (state.closed === true || state.unreachable === true) {
    status.textContent = state.closed === true ? 'Page Closed' : 'Not Answering';
    for (const id of ['off', 'on', 'window', 'off-row']) show(id, false);
    $('problem').textContent = state.closed === true ? CLOSED : UNREACHABLE;
    show('problem', true);
    return;
  }
  const open = state.window?.state === 'open' && state.shown === true;
  const on = state.pairing === 'on';
  status.textContent =
    state.turning === 'on'
      ? 'Turning On'
      : state.turning === 'off'
        ? 'Turning Off'
        : on
          ? 'Pairing On'
          : 'Pairing Off';
  show('off', !on);
  show('on', on && !open);
  show('window', on && open);
  show('off-row', on);
  for (const id of ['turn-on', 'pair', 'turn-off']) $(id).disabled = state.turning !== null;

  const outcome = outcomeOf(state.window);
  $('outcome').textContent = outcome ?? '';
  show('outcome', outcome !== null);

  const list = $('headsets');
  list.replaceChildren(
    ...state.headsets.map((headset) => {
      const row = document.createElement('li');
      const label = document.createElement('span');
      label.className = 'label';
      label.textContent = headset.label;
      const fact = document.createElement('span');
      fact.className = 'fact';
      fact.textContent = headset.connected ? 'Connected' : 'Not Connected';
      const forget = document.createElement('button');
      forget.type = 'button';
      forget.className = 'quiet-button';
      const asking = armed === `forget:${headset.id}`;
      forget.textContent = asking ? 'Yes, Forget' : 'Forget';
      forget.addEventListener('click', () =>
        asking ? send('/headsets/forget', { id: headset.id }) : arm(`forget:${headset.id}`),
      );
      row.append(label, fact, forget);
      return row;
    }),
  );
  show('none', state.headsets.length === 0);

  const turningOff = armed === 'off';
  $('turn-off').textContent = turningOff ? 'Yes, Turn Off' : 'Turn Off Pairing';
  show('off-note', turningOff);

  if (open) {
    // Only a name a headset can reach is shown, and the code only beside one.
    const reachable = state.names.length > 0 && state.code !== null;
    show('pairing', reachable);
    show('unreachable', !reachable);
    $('route').textContent = ROUTE;
    const [first, ...others] = state.names;
    $('name').textContent = first ?? '';
    $('others').textContent = others.length === 0 ? '' : `Or ${others.join(' · ')}`;
    show('others', others.length > 0);
    $('code').textContent = reachable ? `${state.code.slice(0, 4)} ${state.code.slice(4)}` : '';
    const tries = Math.max(0, state.window.max_failed_attempts - state.window.failed_attempts);
    $('facts').textContent =
      `${left(state.window.seconds_left)} Left · ${tries} ${tries === 1 ? 'Try' : 'Tries'} Left`;
  }

  $('problem').textContent = state.problem ?? '';
  show('problem', state.problem !== null);
}

$('turn-on').addEventListener('click', () => send('/pairing/on'));
$('pair').addEventListener('click', () => send('/window'));
$('turn-off').addEventListener('click', () =>
  armed === 'off' ? send('/pairing/off') : arm('off'),
);

read();
setInterval(read, 1000);
