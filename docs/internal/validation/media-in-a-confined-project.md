# A photo or a video in a confined project (media into a project, ADR 0066)

- **Question:** with a photo or a video placed in a new app's folder, what can an agent inside Halcyonic's closed
  Seatbelt profile do with it: run the tools a video needs, see a still, turn a HEIC into a picture, copy a playable
  copy into `public/`? And which tools keep where and on what a file was made?
- **Date:** 2026-11-16.
- **Versions:** main d53f69a32, and main 8d6e0944e for the last Claude Agent `iwsdk-vr` run, and main 1d0ff786f for the OpenCode `iwsdk-vr` run and the Codex attempt; macOS 26 (Darwin 25.6.0) on Apple silicon; ffmpeg and ffprobe 8.1.1 (Homebrew, with
  libx265 and libx264); the system's `sips`, `avconvert` and `osascript`; Node 24.15.0 (Homebrew).
- **Method:**
  - Part A, no model: a probe script built the closed profile exactly as the host does for a confined project
    (`confinedProfile` from `@halcyonic/seatbelt`, with `toolFoldersFor(node)` as `runtimes.ts` gives every runtime,
    the data folder hidden, no loopback port), on a scratch folder, and ran each tool inside it with
    `sandbox-exec`. Exit codes were captured straight after each step.
  - Part B, hosted: one turn per runtime on a control plane run for proving, in a new app made from a starter, with
    media placed at the paths ADR 0066 names. The answers were read back through the product's own file route.
  - All media was synthetic, made by the probe: colour bands (a 3024 by 4032 JPEG with grain, and a HEIC of it
    written by `sips` as a tile grid) and a 6 second HEVC 1376 by 1840 30 fps clip with AAC sound, one colour each
    second, carrying made-up location, make, model and creation time tags. Colours were drawn at random per run.
- **What Part A proves:** what runs under the agent's closed profile, and whether each tool runs under a Seatbelt
  profile at all. It does not prove the host's own tool profile (one input read by path, one temporary folder
  written).

## Inside the closed profile (Part A, observed)
- `ffprobe` reads the HEVC clip: `hevc,1376,1840` and `aac`.
- `ffmpeg` runs every step tried, each with exit 0:
  - a frame at 2.5 s, whose colour matched the drawn one;
  - one frame a second at 512 px (6 written);
  - decoding with the Mac's hardware decoder (`-hwaccel videotoolbox`);
  - the HEIC tile grid to one 3024 by 4032 JPEG, all four bands matching;
  - H.264 copies, and a remux of the video and sound streams.
- `cp` of a play copy into `public/` works.
- `sips` reads a picture's size, but its conversions (HEIC to JPEG, a resize) fail with "Error 13: an unknown error
  occurred" when the project is the working folder. One run of the same conversion under the profile succeeded with
  the folder above the project as the working folder. The cause was not found.
- `avconvert` fails under the profile with exit 205. The same command works outside it.
- `osascript -l JavaScript` fails under the profile: "no such component JavaScript". So no AVFoundation through a
  JavaScript for Automation script inside it.
- Refused, as they must be: reading a file outside the project (`cat`, "Operation not permitted"), reading the
  hidden data folder, writing into the read-only runtime folder (`touch`), and `ffmpeg` writing outside the project.

## Where, when and on what a file was made (Part A, observed outside the profile)
- `ffmpeg -map 0:v -map 0:a -c copy -map_metadata -1` drops the location, `creation_time`, make, model and every
  `com.apple.quicktime.*` tag, and leaves only the video and audio streams. A display rotation of 90 degrees stays:
  it is part of the video track, not a tag.
- An `ffmpeg` H.264 copy made without `-map_metadata -1` keeps the location tag. With the flag, a 1280 px copy of a
  rotated clip carries none of those tags, and the rotation is applied (1280 by 958, no rotation left).
- `ffmpeg` stills of the rotated clip with `-map_metadata -1` are upright (1600 by 1197) and carry no GPS.
- Photos with a GPS dictionary written by ImageIO: `sips` HEIC to JPEG keeps the GPS. `ffmpeg` JPEG re-encodes and
  its HEIC to JPEG with `-map_metadata -1` carry none.
- `avconvert` with `Preset1920x1080` drops the location, make, model and `com.apple.quicktime.*` tags, and keeps
  `creation_time`.
- A still made by AVFoundation (`AVAssetImageGenerator`, written by `CGImageDestination` with no properties) matched
  the drawn colour and carries no GPS.

## Hosted agents using the media (Part B, observed)
The probe ran on a control plane on its own data folder, on main d53f69a32, through that plane's script door. For
each run it made a new app from a starter, then placed the synthetic media outside any sandbox, as the host would, at
ADR 0066's paths: `media/video-<12 hex>.mov`, its `-play.mp4` (by `avconvert`), its `-stills/1.jpg` to `4.jpg` at 10,
35, 60 and 85% of its length, and `media/photo-<12 hex>.jpg` (3024 by 4032) with one still. The placing is the probe's,
not ADR 0066's (no strip, no temporary folder): it proves nothing about the host's placing. One instruction asked the
agent to write a still's colour, the colour of the frame at 4.5 s taken with any tool it has, and the photo's four band
colours into files, and to copy the play copy into `public/`. The files were read back through
`GET /api/projects/:id/file`, and only whether each matched was printed, never the agent's words.

| Runtime and model | Starter | Turn | Still | Frame | Photo | Play copy in `public/` | Approvals |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Claude Agent, `claude-opus-5-5[1m]` | `world` | 25 s | match | match | match | present | 4, all answered by the host |
| OpenCode, `meta/muse-wonder-1.0` (listed remote, takes pictures) | `world` | 45 s | match | match | match | present | 3, all answered by the host (`confined_command`) |
| Claude Agent, `claude-opus-5-5[1m]` | `iwsdk-vr` | 3 runs, each stopped (600 s at most) | unread | unread | unread | unread | 3: 2 answered by the host, 1 `Read` left for a person |
| Claude Agent, `claude-opus-5-5[1m]`, main 8d6e0944e | `iwsdk-vr` | 17 s | match | match | match | present | 4, all answered by the host (3 `confined_command`, 1 `run_temp_read`) |
| OpenCode, `meta/muse-wonder-1.0`, main 1d0ff786f | `iwsdk-vr` | 44 s | match | match | match | present | 3, all answered by the host (`confined_command`) |

- A first `world` run on Claude Agent matched the still, the frame and the play copy but not the photo; its bands
  were drawn with repeated colours, so neighbours could read as one band. The probe then drew four different colours.
- On `world` both agents took the frame from the HEVC `.mov` themselves, inside the closed profile, and the host
  answered every approval under the confined copy's own rule (ADR 0064).
- On `iwsdk-vr` the task works in a copy (ADR 0045). In two runs out of two, one approval for Claude Agent's `Read`
  tool was not answered by the host, and the turn waited for a person until the probe interrupted it. The answers
  were unread: the file route reads the project's folder, not the task's copy.
- A third `iwsdk-vr` run printed each named path's class only: the request left for a person was complete and named
  one path, a read of an image in the run's own temporary folder (`claude-sandbox-<key>/tmp` in the data folder). The
  host answers a read only inside the task's copy, the starter's shared files or a declared original, so a frame the
  agent writes to its temporary folder and then looks at reaches the person.
- On main 8d6e0944e the host answers a confined run's reads inside its own temporary folder, by its own record of that
  folder, and refuses one the run has made a link. One `iwsdk-vr` run there: the turn completed in 17 s with all four
  approvals answered by the host, one of them that read, and the still, the frame, the photo and the play copy all
  matched. The answers were read from the task's copy through the file route's `workstream_id`.
- The colours were drawn at random for each run, so a matching answer means the model saw the picture, not its name.
- OpenCode on `iwsdk-vr` worked in the task's copy as Claude Agent did on main 8d6e0944e: the turn completed with no
  approval left for a person, and all four answers matched.
- Codex did not run a turn. On main d53f69a32 the product's own choice for Codex was a model served on this Mac, which
  the probe refuses. On main 1d0ff786f it chose a model served remotely, `halcyonic_meta/muse-wonder-1.0`, for a new
  `world` app: the start was accepted, then the host's start of a confined Codex failed (`ENOENT` copying the Codex
  home's `config.toml`, which only the local model setup writes), the person's command failed as "The runtime adapter
  failed unexpectedly", and the execution was left unknown. No media step was reached.

## Inference
- `ffmpeg` is reachable inside the closed profile only because Node here is Homebrew's: `toolFoldersFor(node)` opens
  Homebrew's `Cellar`, `opt`, `lib`, `bin`, `libexec` and `share`. On a Mac whose Node is installed elsewhere, a
  confined agent would have no `ffmpeg`.

## Not verified
1. A timed metadata or data track in the input: the fixture had none.
2. EXIF make, model or date in a photo: only GPS was written.
3. Files from a real phone or glasses.
4. Why `sips` conversions depend on the working folder under the profile.
5. Why the agent put its frame in its temporary folder on `iwsdk-vr` but not on `world`.
6. Codex with the media: blocked by its confined start on a Mac whose Codex home has no `config.toml`.
