# Music and light in the VR starter

- **Question:** can the starter's stack make music worth listening to and light that lands with it, in a headset's
  own browser, with no file and no network, and which pieces may it carry?
- **Date:** 2026-11-14.
- **Method:** the starter's kit (`starters/iwsdk-vr/template/src/`: `tone.ts`, `instruments.ts`, `score.ts`,
  `pieces.ts`, `glow.ts`, `places.ts`) in a scratch app made from the template, served by the host's own Vite config
  on loopback and opened in headless Chrome on this Mac's GPU (ANGLE on Metal). The sound was rendered through an
  `OfflineAudioContext` with the same instruments, room and scheduling the page plays, and measured from the
  samples; `apps/control-plane/src/starter-music.test.ts` measures the dry tone in Node. Nobody listened: this Mac
  has no one to hear it, so these are measurements, and a person's ears on the headset are the judge.
- **Status:** measured on this Mac, including with Chrome's CPU slowed six times; not yet run in the headset.

## The pieces and why they may be carried

| Piece | Composer, year | Why it is in the public domain | Edition read | Its licence, as the edition states it |
| --- | --- | --- | --- | --- |
| Prelude in C major, BWV 846 (The Well-Tempered Clavier, Book 1) | Johann Sebastian Bach, 1722 | published long before 1929; the composer died in 1750 | Mutopia Project, `BachJS/BWV846/wtk1-prelude1/wtk1-prelude1.ly` | `copyright = "Public Domain"`; "This sheet music has been placed in the public domain by the typesetter" |
| Gymnopedie No. 1 | Erik Satie, 1888 | published in 1888; the composer died in 1925 | Mutopia Project, `SatieE/gymnopedie_1/gymnopedie_1.ly` (from the Dover edition) | `license = "Public Domain"`; its page: "Copyright: Public Domain" |

- Only pitches and lengths were typed by hand from those editions; the touch, the pedal and the tempo are the
  starter's own playing. No MIDI file, recording or arrangement was used. Mutopia's other Satie editions are under
  CC BY-SA and were not used.
- `pieces.ts` says so in its own words, and the template's `NOTICE` tells a person who ships an app made from the
  template that they are public-domain compositions.

## What was measured

- **No clipping:** 0 samples at full scale in either piece; peaks of -10.4 dBFS (Prelude) and -13.7 dBFS
  (Gymnopedie) before the last +2.2 dB of master level.
- **Tuning:** a struck A4 sounds at 440 Hz to under 0.1 cent (the Node test, on the dry tone). Through the room the
  measured fundamentals sit within 0.9 cent, inside the blur a decaying tone has through a reverberant room.
  Partials 2 to 5 are sharp of whole multiples by 1.5, 4.2, 8.1 and 13.2 cents at A4 and by up to 35 cents at A5,
  as on real strings.
- **Decay by register,** 20 dB down after: C2 4.9 s, C3 4.8, C4 2.7, C5 1.4, C6 0.7, C7 0.4.
- **Timing:** each note is first heard 0.5 to 1.2 ms after its scheduled time, its 1.5 ms attack. Chrome's
  `DynamicsCompressorNode` delayed everything by a fixed 6 ms (its look-ahead), measured in the first render; the
  instruments use a soft clip instead, which adds no delay.
- **Light and sound together:** the light reads the player's time, the audio clock less the output's delay
  (`getOutputTimestamp`). Over 138 notes at 60 frames a second, each note's light first landed a median 8.1 ms and at
  most 16.3 ms after its heard time: within one frame.
- **Cost:** 16 draw calls (20 with the welcome card) and about 6,500 triangles; the light's own work per frame under
  0.1 ms at the 95th percentile on this Mac, and 1.2 ms with the CPU slowed six times.
- **Start:** the keys are made from their partials on a worker, off the page's thread, opening keys first: 40 keys
  in 1.27 s on this Mac. With the CPU slowed six times, sound began 0.25 s after the press and the first note was
  heard 1.26 s after it; one frame took 134 ms as sound started, the others held 16.7 ms (median, 18.3 ms at the
  95th percentile).

## What is not known

- How it sounds and how fast it runs on the Quest 3 and 3S: the slowed-CPU run is an estimate, not the headset.
- Whether a person hears the piano as a piano: only a person can say.
