# Working on this world

This project is a person's world: the place they stand in when they wear their headset. The whole world is one
file, `world.json`. The headset draws it from that file. It is a description, not a program.

What you can do:
- Change the values in `world.json` with your file-editing tool. That is the only way to change the world.
- After your turn the person's computer reads `world.json` itself. If the file follows the rules below, the
  person sees their world turn to the new values. If it breaks a rule, their world stays as it was.

What you cannot do here:
- Do not add a member that is not listed below, and do not remove one: every member must be there, and nothing
  else. A file with an unknown member is refused whole.
- Do not write code, scripts, pictures, sounds or other files for the world. Only `world.json` is read.
- Do not put a path, an address or a file name in `world.json`. Nothing is loaded from it.
- You cannot add new kinds of things (a castle, a forest, rain). When the person asks for something the members
  below cannot say, tell them plainly that this world cannot do it yet, and offer the nearest thing it can do.
- Keep `world.json` valid JSON: double quotes, no comments, no trailing commas.

## The members of `world.json`

Colours are `"#RRGGBB"`: a `#` and six hexadecimal digits, nothing else (no names, no `rgb()`).
Amounts run from 0 (none) to 1 (the most). Bearings are degrees on the level from straight ahead of the person:
0 is ahead, 90 is to their right, -90 to their left, 180 or -180 behind them; from -180 to 180.

| Member | What it is | Rule |
| --- | --- | --- |
| `format` | The file's format | always `1` |
| `sky.zenith` | The sky straight up | colour |
| `sky.band` | A colour low in the sky between the two, as at sunset | colour, or `null` for none |
| `sky.horizon` | The sky at the horizon | colour |
| `disc` | A sun or a moon | the five members below, or `null` for neither |
| `disc.bearing` | Where it is, round the person | bearing |
| `disc.height` | Degrees above the horizon; below 0 it has set and only its glow shows | -10 to 90 |
| `disc.size` | Its radius in degrees; the real sun and moon are about 0.25, a big storybook one 5 | 0.25 to 15 |
| `disc.colour` | Its colour | colour |
| `disc.glow` | The light round it | amount |
| `stars` | How many stars show | amount |
| `clouds.amount` | How much cloud | amount |
| `clouds.colour` | The clouds' colour | colour |
| `mist.amount` | Haze at the horizon, where the water meets the sky | amount |
| `mist.colour` | The haze's colour | colour |
| `water.near` | The water close to the person | colour |
| `water.far` | The water far away | colour |
| `water.ripple` | From still (0) to choppy (1) | amount |
| `water.shine` | How strongly the water gives back the sky's light | amount |
| `ground.colour` | The ground the person is on | colour |
| `ground.radius` | How far it reaches round them, in meters | 1.5 to 12 |
| `wind.bearing` | Where the wind blows toward | bearing |
| `wind.strength` | How hard it blows: it moves the clouds and the water | amount |
| `places` | Where the person's projects stand in the world | a list of at most 8 |
| `places[].project` | The project's name, exactly as the person says it | 1 to 200 characters |
| `places[].bearing` | Where its place is, round the person | bearing |
| `places[].distance` | How far away, in meters | 3 to 80 |

## How to make a change well

- Change only what the person asked for, and keep the rest as it is.
- Make the parts agree. Night needs a dark `sky.zenith` and `sky.horizon`, a pale `disc.colour` for a moon, more
  `stars`, and darker `water`. A sunset needs a low `disc.height`, a warm `sky.band` and a warm `mist.colour`.
- The person reads words on panels that float in front of the water. Keep `water.near` and `ground.colour` dark
  enough that white words stay easy to read.
- Say in one or two plain sentences what you changed.
