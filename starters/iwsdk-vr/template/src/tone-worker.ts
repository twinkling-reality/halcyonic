import { type StruckKind, strikeKey } from './tone.js';

/**
 * Makes struck keys' samples off the page's own thread, so a headset never drops a frame while an instrument is
 * prepared. `src/instruments.ts` starts it; it reads only the messages it is sent.
 */
self.onmessage = (event: MessageEvent<{ id: number; kind: StruckKind; midi: number; rate: number }>) => {
  const { id, kind, midi, rate } = event.data;
  const samples = strikeKey(kind, midi, rate);
  (self as unknown as Worker).postMessage({ id, samples }, [samples.buffer]);
};
