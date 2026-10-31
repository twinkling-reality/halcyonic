// Halcyonic's viewer of a game playing on this computer (P-B). It shows the frames the host streams
// and sends back only mouse and key input; the game itself runs in the host's contained browser.

const WIDTH = 1280;
const HEIGHT = 720;
/** How long the viewer tries to reconnect after its stream drops; the host keeps the game this long. */
const GRACE_MS = 5000;
const BUTTONS = ['left', 'middle', 'right'];

/** The words for why a game ended, by the host's reason. */
const ENDED = {
  closed: 'Game Closed',
  preview_stopped: 'Game Ended · Preview Stopped',
  newer_check: 'Game Ended · Newer Check',
  host_stopped: 'Game Ended · Computer Stopped',
  failed: 'Game Ended · Browser Failed',
};

/** The words for the game's sound, as the host observes it on the computer's speakers. */
const SOUND = {
  playing: 'Sound On',
  quiet: 'Sound Paused',
  none: 'No Sound · Silent Game',
};

const status = document.getElementById('status');
const sound = document.getElementById('sound');
const screen = document.getElementById('screen');
const start = document.getElementById('start');
const ended = document.getElementById('ended');

let socket = null;
let over = false;
let playing = false;
let lostAt = 0;
let shown = null;
let pendingMove = null;

function say(words, tone) {
  status.textContent = words;
  status.className = tone === undefined ? 'status' : `status ${tone}`;
}

function end(words) {
  over = true;
  ended.textContent = words;
  ended.hidden = false;
  start.hidden = true;
  say('Ended');
  sound.hidden = true;
}

function send(message) {
  if (socket !== null && socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify(message));
}

function connect() {
  const ws = new WebSocket(`ws://${location.host}/stream`);
  ws.binaryType = 'blob';
  socket = ws;
  ws.addEventListener('open', () => {
    lostAt = 0;
    say(playing ? 'Live' : 'Ready');
  });
  ws.addEventListener('message', (event) => {
    if (typeof event.data !== 'string') {
      const url = URL.createObjectURL(event.data);
      const previous = shown;
      shown = url;
      screen.src = url;
      if (previous !== null) URL.revokeObjectURL(previous);
      return;
    }
    let message;
    try {
      message = JSON.parse(event.data);
    } catch {
      return;
    }
    if (message.type === 'ended') end(ENDED[message.reason] ?? 'Game Ended');
    if (message.type === 'sound' && SOUND[message.state] !== undefined) {
      sound.textContent = SOUND[message.state];
      sound.hidden = false;
    }
  });
  ws.addEventListener('close', () => {
    if (socket !== ws || over) return;
    socket = null;
    if (lostAt === 0) lostAt = Date.now();
    if (Date.now() - lostAt >= GRACE_MS) {
      end('Game Closed · Connection Lost');
      return;
    }
    say('Reconnecting', 'waiting');
    setTimeout(connect, 1000);
  });
}

/** Where a pointer event falls on the game's 1280 by 720 page, or null when off it. */
function onPage(event) {
  const box = screen.getBoundingClientRect();
  const scale = Math.min(box.width / WIDTH, box.height / HEIGHT);
  const left = box.left + (box.width - WIDTH * scale) / 2;
  const top = box.top + (box.height - HEIGHT * scale) / 2;
  const x = Math.round((event.clientX - left) / scale);
  const y = Math.round((event.clientY - top) / scale);
  if (x < 0 || y < 0 || x > WIDTH || y > HEIGHT) return null;
  return { x, y };
}

function mouse(action, event) {
  const at = onPage(event);
  if (at === null) return;
  if (action === 'move') {
    // At most one move a frame: the newest wins.
    if (pendingMove === null) requestAnimationFrame(flushMove);
    pendingMove = { type: 'mouse', action: 'move', x: at.x, y: at.y };
    return;
  }
  const button = BUTTONS[event.button];
  if (button === undefined) return;
  send({ type: 'mouse', action, x: at.x, y: at.y, button });
}

function flushMove() {
  if (pendingMove !== null) send(pendingMove);
  pendingMove = null;
}

function key(action, event) {
  if (!playing || over) return;
  event.preventDefault();
  send({ type: 'key', action, key: event.key, code: event.code });
}

start.addEventListener('click', () => {
  playing = true;
  start.hidden = true;
  say('Live');
});
screen.addEventListener('mousemove', (event) => playing && mouse('move', event));
screen.addEventListener('mousedown', (event) => {
  event.preventDefault();
  if (playing) mouse('down', event);
});
screen.addEventListener('mouseup', (event) => playing && mouse('up', event));
screen.addEventListener('contextmenu', (event) => event.preventDefault());
window.addEventListener('keydown', (event) => key('down', event));
window.addEventListener('keyup', (event) => key('up', event));

connect();
