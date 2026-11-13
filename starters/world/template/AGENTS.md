# Working on this world

This project is a person's world: the place they stand in when they wear their headset. Its scene, `cylinder`,
is the inside of a vast turning cylinder, seen from a balcony high on its near end: land all round and overhead,
towns and a lake on it, a chain of small suns along its middle, long windows down its length, and a sun low in a
window at the far end. The whole of what can change is one file, `world.json`. It is a description, not a program.

The scene was made as separate lights. The headset mixes them by the values in `world.json`, so every value you
change shows within seconds, with nothing to build or render.

What you can do:
- Change the values in `world.json` with your file-editing tool. That is the only way to change the world.
- After your turn the person's computer reads `world.json` itself. If the file follows the rules below, the
  person sees their world turn to the new values. If it breaks a rule, their world stays as it was.

What you cannot do here:
- This world's version control and editor settings (`.git`, `.gitmodules`, `.envrc`, `.vscode`, `.idea`,
  `.devcontainer`, `*.code-workspace`) are kept by Halcyonic: you cannot write them, and need not.
- Do not add a member that is not listed below, and do not remove one: every member must be there, and nothing
  else. A file with an unknown member is refused whole.
- Do not write code, scripts, pictures, sounds or other files for the world. Only `world.json` is read.
- Do not put a path, an address or a file name in `world.json`. Nothing is loaded from it.
- Do not change `scene`: it names the pictures the person's headset holds.
- You cannot change the shapes: the land, the towns, the buildings, the clouds, the weather, the season. You cannot
  add things (a castle, a forest, rain) or move a place. When the person asks for something the members below
  cannot say, tell them plainly that this world cannot do it yet, and offer the nearest thing it can do.
- Keep `world.json` valid JSON: double quotes, no comments, no trailing commas.

## The members of `world.json`

Colours are `"#RRGGBB"`: a `#` and six hexadecimal digits, nothing else (no names, no `rgb()`). A light's colour
tints that light: `"#FFFFFF"` keeps its own colour, `"#FFB070"` makes it warmer, `"#A0C0FF"` colder, and a dark
colour dims it as well as tinting it.

| Member | What it is | Rule |
| --- | --- | --- |
| `format` | The file's format | always `2` |
| `scene` | The scene the world is in | always `"cylinder"` |
| `night` | From dusk to deep night: the day's lights fade as the night's come; 0.5 is well into the evening | 0 to 1 |
| `lights` | Every light of the scene, each once, in any order | the lights below |
| `lights[].light` | Which light | its name below |
| `lights[].brightness` | How bright: 0 is off, 1 as the world was made, 2 clearly brighter, 4 four times as bright | 0 to 4 |
| `lights[].colour` | Its tint | colour |
| `haze.thickness` | The air between the person and the far land: thicker hides the far end in glow, thinner shows it clear; 0.3 clear, 1.5 hazy, 2 thick (above 2 the land overhead disappears) | 0 to 4 |
| `haze.colour` | The tint of the air's glow | colour |
| `places` | Which of the person's projects has which of the scene's places | a list of at most 8 |
| `places[].project` | The project's name, exactly as the person says it | 1 to 200 characters |
| `places[].at` | Which place is that project's: each place is used once | its name below |

The scene's lights:

| Light | What it is | When it shines |
| --- | --- | --- |
| `sunset` | The sun low in the window at the far end, and the warm light it sends down the world | By day: fades as night comes |
| `beam` | That sun's gold beam through the long window on the right, lying as a band of gold across the land on the left | By day: fades as night comes |
| `suns` | The chain of small suns along the world's middle, dim at dusk, and the warm light they cast on the land overhead | By day: fades as night comes |
| `skylight` | The sky's light coming in through the long windows | By day: fades as night comes |
| `moon` | The night's cold light | By night: comes as night does |
| `towns` | Every town's windows and street lamps, and the balcony's lanterns | Day and night |

The scene's places, each a part of the land with its own light, which comes up while that project's agent is at
work:

| Place | Where it is |
| --- | --- |
| `tower` | The lantern tower on the knoll by the lake below the balcony |
| `lake` | The town just past the lake, a little to the right |
| `left` | A town up the land rising on the left |
| `right` | A town up the land rising on the right |
| `overhead` | A town high overhead, toward the right |
| `far_left` | A town far down the world, up on the left |
| `far_right` | A town far down the world, up on the right |
| `far_end` | A town at the foot of the far end, under the sunset |

## How to make a change well

- Change only what the person asked for, and keep the rest as it is.
- Make the parts agree. For night, raise `night` toward 1; the night's light and the towns carry the picture then, and
  raise `moon` to 2 if the person wants to see more of the land at night.
  For a brighter, clearer day, lower `night`, raise the day's lights and thin the `haze`.
- Make a change big enough to see: for "brighter" double a light (2), for "much brighter" go to 3 or 4; for "dimmer" halve it. 0 turns a light off, which the person may not expect.
- Say in one or two plain sentences what you changed.
