// The setup page (ADR 0053): reads what stands every two seconds, draws every step with its presses,
// and sends a press as one POST from this page. A press asks first, with what it will do; only the
// second press acts. One download runs at a time, with its last lines and a Stop. Above the steps,
// each game shared now (ADR 0054) with Copy, which puts its address on this Mac's clipboard, and Stop.
// A link's address is never drawn, only copied.

const CLOSED = 'This page has closed. Open pairing again on this computer.';
const UNREACHABLE = "This computer's app isn't answering. Check that it is running.";
const STATUS = {
  ready: 'Ready',
  todo: 'To Do',
  optional: 'Optional',
  look: 'Look at This',
  unknown: "Can't Tell Yet",
};

const $ = (id) => document.getElementById(id);
let state = null;
let problem = null;

async function call(path, body) {
  try {
    const response = await fetch(path, {
      method: body === undefined ? 'GET' : 'POST',
      headers: body === undefined ? {} : { 'content-type': 'application/json' },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    if (response.status === 403) {
      problem = CLOSED;
      return;
    }
    state = await response.json();
    problem = null;
  } catch {
    problem = state?.restarting
      ? 'Restarting. A new page opens when Halcyonic is running again.'
      : UNREACHABLE;
  }
  draw();
}

let drawnSteps = '';
let drawnShared = '';

const WAY = { link: 'Link', wifi: 'This Wi-Fi' };

function drawShared() {
  const shared = state.shared ?? [];
  $('shared').hidden = shared.length === 0;
  // Drawn again only when what is shared changes, so a poll never swaps a button under a press.
  const drawn = JSON.stringify(shared);
  if (drawn === drawnShared) return;
  drawnShared = drawn;
  const list = $('shared-list');
  list.replaceChildren();
  for (const share of shared) {
    const item = document.createElement('li');
    const name = document.createElement('span');
    name.className = 'share-name';
    name.textContent = share.name;
    const way = document.createElement('span');
    way.className = 'share-way';
    way.textContent = WAY[share.way] ?? share.way;
    item.append(name, way);
    if (share.address_text !== null) {
      const address = document.createElement('span');
      address.className = 'share-address';
      address.textContent = share.address_text;
      item.append(address);
    }
    const copy = button(
      'Copy',
      async () => {
        try {
          await navigator.clipboard.writeText(share.url);
          copy.textContent = 'Copied';
        } catch {
          copy.textContent = "Couldn't Copy";
        }
        setTimeout(() => {
          copy.textContent = 'Copy';
        }, 2000);
      },
      false,
    );
    const stop = button(
      'Stop',
      () => void call('/setup/shares/stop', { project_id: share.project_id }),
      false,
    );
    item.append(copy, stop);
    list.append(item);
  }
}

function press(action, arg, confirmed) {
  void call('/setup/act', { action, arg, confirmed });
}

function button(label, onClick, primary) {
  const element = document.createElement('button');
  element.type = 'button';
  element.className = primary ? 'primary' : 'quiet-button';
  element.textContent = label;
  element.addEventListener('click', onClick);
  return element;
}

function draw() {
  $('problem').hidden = problem === null;
  $('problem').textContent = problem ?? '';
  if (state === null) return;
  const running = state.job !== null && state.job.state === 'running';
  $('status').textContent = state.restarting
    ? 'Restarting'
    : running
      ? 'Downloading'
      : 'Ready for You';
  $('restart').hidden = !state.restart_needed || state.restarting;
  $('restart-button').hidden = !state.can_restart;
  $('restart-by-hand').hidden = state.can_restart;
  $('confirm').hidden = state.confirm === null;
  $('confirm-text').textContent = state.confirm?.text ?? '';
  $('job').hidden = state.job === null;
  if (state.job !== null) {
    const words = {
      running: 'Downloading',
      done: 'Done',
      not_set_up: 'Not Set Up',
      failed: "Didn't Finish",
      stopped: 'Stopped',
    };
    $('job-title').textContent = state.job.title;
    $('job-state').textContent = words[state.job.state];
    $('job-state').className = `job-state ${state.job.state}`;
    $('job-time').textContent = `${state.job.seconds} s`;
    $('job-lines').textContent = state.job.lines.join('\n');
    $('job-stop').hidden = !running;
  }
  $('said').hidden = state.said.length === 0;
  $('said-lines').textContent = state.said.join('\n');
  drawShared();
  // Drawn again only when the steps change, so a poll never swaps a button under a press.
  const drawn = JSON.stringify([state.steps, running]);
  if (drawn === drawnSteps) return;
  drawnSteps = drawn;
  const list = $('steps');
  list.replaceChildren();
  for (const step of state.steps) {
    const item = document.createElement('li');
    const head = document.createElement('div');
    head.className = 'step-head';
    const title = document.createElement('span');
    title.textContent = step.title;
    const status = document.createElement('span');
    status.className = `step-status ${step.status}`;
    status.textContent = STATUS[step.status] ?? step.status;
    head.append(title, status);
    item.append(head);
    const lines = document.createElement('p');
    lines.className = 'step-lines';
    lines.textContent = step.lines.join(' ');
    item.append(lines);
    if ((step.next ?? []).length > 0) {
      // Commands for the person to run in Terminal, as setup prints them: text, never run from here.
      const commands = document.createElement('pre');
      commands.className = 'step-next';
      commands.textContent = step.next.join('\n');
      item.append(commands);
    }
    if (step.actions.length > 0) {
      const actions = document.createElement('div');
      actions.className = 'actions';
      step.actions.forEach((action, index) => {
        const block = document.createElement('div');
        block.className = 'action';
        // One accent per step: its first action, while the step is still to do.
        const element = button(
          action.label,
          () => press(action.action, action.arg, false),
          step.status === 'todo' && index === 0,
        );
        element.disabled = running && action.action === 'download';
        block.append(element);
        if (action.fetches !== undefined) {
          const fetches = document.createElement('span');
          fetches.className = 'fetches';
          fetches.textContent = action.fetches;
          block.append(fetches);
        }
        actions.append(block);
      });
      item.append(actions);
    }
    list.append(item);
  }
}

$('confirm-yes').addEventListener('click', () => {
  if (state?.confirm) press(state.confirm.action, state.confirm.arg, true);
});
$('confirm-no').addEventListener('click', () => {
  if (state !== null) state = { ...state, confirm: null };
  draw();
});
$('job-stop').addEventListener('click', () => void call('/setup/stop', {}));
$('restart-button').addEventListener('click', () => press('restart', null, false));
$('check').addEventListener('click', () => press('check', null, false));

void call('/setup/state');
setInterval(() => void call('/setup/state'), 2000);
