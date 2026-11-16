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

- **No clipping:** 0 samples at full scale in either piece; peaks of -8.1 dBFS (Prelude) and -11.4 dBFS
  (Gymnopedie).
- **Tuning:** a struck A4 sounds at 440 Hz to under 0.1 cent (the Node test, on the dry tone). Through the room the
  measured fundamentals sit within 0.9 cent, inside the blur a decaying tone has through a reverberant room.
  Partials 2 to 5 are sharp of whole multiples by 1.5, 4.2, 8.1 and 13.2 cents at A4 and by up to 35 cents at A5,
  as on real strings.
- **Decay by register,** 20 dB down after: C2 4.9 s, C3 4.8, C4 2.7, C5 1.4, C6 0.7, C7 0.4.
- **Timing:** each note is first heard within 1.2 ms after its scheduled time, its 1.5 ms attack. Chrome's
  `DynamicsCompressorNode` delayed everything by a fixed 6 ms (its look-ahead), measured in the first render; the
  instruments use a soft clip instead, which adds no delay.
- **Light and sound together:** the light reads the player's time, the audio clock less the output's delay
  (`getOutputTimestamp`). Over about 73 notes in each of three views at 60 frames a second, each note's light first
  landed a median 8 to 9 ms after its heard time, and at most 17 to 18 ms in most runs: within one frame, or the one
  after it where that frame itself came late (43 ms in the run whose longest frame was 121 ms).
- **Seeing a note coming, with the head level:** the keys stand 1.1 m from the person and 1.0 m up, 25 to 29 degrees
  below eye level; a note falls 1.7 m straight down above its key in 2.8 s, and in a Quest 3S's level field (96 by 90
  degrees, less 1.5 at each edge) all of its fall is in view for an eye 1.62 m up: the field's top edge stands 1.70 m
  above the key (about 1.68 m for an eye 1.60 m up, so the fall's first 2 cm sit just above it). It is
  drawn at a quarter of its full brightness or more, as the eye sees it, for its last 1.2 m, 2.0 s. The keyboard spans
  about 89 degrees across, inside the field's 93.
- **Room for the person:** the keys' front edge is 1.1 m from them all round; nothing is drawn nearer than about
  1.0 m (sparks drifting in from the keys), and nothing above them. Nothing asks them to play, and nothing stands on
  a key before its note arrives.
- **Cost:** 16 draw calls (20 with the welcome card) and about 10,000 triangles; the light's own work per frame under
  0.1 ms at the 95th percentile on this Mac, and 1.2 ms with the CPU slowed six times.
- **Start:** the keys are made from their partials on a worker, off the page's thread, opening keys first: 40 keys
  in 0.73 s on this Mac; a worker that fails makes them on the page instead. The room is made in slices between
  frames, the music starting dry until it is ready. With the CPU slowed six times, sound began 0.25 s after the press
  and the first note was heard 1.26 s after it; frames held 16.7 ms (median, 18.6 ms at the 95th percentile), the
  longest 149 ms. Unslowed on this Mac, while other work ran beside it, the 95th percentile was 17.3 to 17.5 ms and
  the longest frame of each run 35 to 121 ms; which of those are this page's own is not known.

## A place from a person's own picture (2026-11-16)

What it is: an agent asked for a place to stand in from a photo or video the person added reads the picture's colours
in the browser (`coloursOfPicture`, the sky from its top rows, the horizon where it darkens most going down, the ground
below; moved to dusk on request; only the app's own files, never another site's), stands the person on the crest of a
dune over open land in those colours (`openLand`: a field of dunes or soft hills to the horizon, lit by a low sun with
long shadows worked out once when it is built, the near sand lit at every pixel from a picture of its light made then,
wind ripples and grain in its shader, the far sand hazed into the sky's own colour), and floats the picture itself on a
window over the sand (`pictureWindow`, hidden until its first frame is ready, a video playing muted by itself, its own
light and a soft shade on the sand under it). The colour reading and the land's shape are pure and tested
(`apps/control-plane/src/starter-memory.test.ts`).

Measured in headless Chrome on this Mac (ANGLE Metal), a scratch app made from the template, 1832 by 1920, the eye at
1.62 m (its world height read back from the camera), with a synthetic 8 s clip made by ffmpeg, never a person's own:
8 to 12 draw calls; about 114,000 triangles; frames 16.7 ms median and 16.8 to 18.3 ms at the 95th percentile; no page
errors; the window ready within 2 s. Building the place took 0.3 to 0.55 s unslowed and 1.9 s with the CPU slowed six
times (measured before its last changes): the headset may show only the sky's colour for that long when the app
opens. In the shadowed lower half of a view the luma's spread in 32 px tiles rose from a median of 0.57 to 4.7 to 8.7
once the person stood on a crest with ripples at their feet. Fresh readers judged its frames in rounds against the
film's bar: 4 of 10 for the first eight, 6 of 10 for the last seven (it reads as a calm place at dusk with nothing
half drawn; what they still named: the window reads as pasted on, the sand at the feet soft, far dune lines, an empty
sky).

## What is not known

- How it sounds and how fast it runs on the Quest 3 and 3S: the slowed-CPU run is an estimate, not the headset.
- How a place from a picture looks and how long it takes to build in the headset, and how it looks with a person's
  own clip; whether the Quest's browser plays a phone's HEVC video (the host's H.264 play copy is the route taken).
- Whether a person hears the piano as a piano: only a person can say.
