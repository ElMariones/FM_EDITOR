# FM27 memory addresses

This file covers Football Manager 2027 **27.0.1+2494687**, module `game_plugin.dll`. The machine-readable copy that the app uses is [definitions/fm27-27.0.1.json](definitions/fm27-27.0.1.json). This file is the human-readable summary of it, plus what's still missing.

Everything here was found by looking at the game's own memory: RTTI class names, object layouts, and comparisons against in-game screens. Nothing was copied from closed-source tools.

## How to read the offsets

- **Records are found by class name.** The app scans memory for objects whose vtable belongs to a given RTTI class (layout `rttiInstances`). There are no static pointers or RVAs, so the offsets should survive small patches.
- **Offsets are relative to the record base.**
  - Players and staff use their `db::PERSON` part as the base, so player attributes have negative offsets: they sit in `db::PLAYING_DATA`, which comes before the person part.
  - Clubs, competitions and nations use the object start as the base.
- **`path [a, b]` means following pointers before applying the offset.** Read the qword at base+a, then the qword at that pointer+b, then add the offset. If any pointer is 0, the field is empty and can't be edited.
- **Scale 5** means the value is stored 1–100 and shown in game as 1–20, rounded.

### Types

| Type | Meaning |
|---|---|
| `fmString` | Pointer to an i32 byte count, the UTF-8 bytes, and a terminating 0. Edits are made in place, so the new text can't be longer than the old one. |
| `fmDate` | A u16 day of year counted from 1, then a u16 year. Day 1 of 1900 means no date. |
| `entityPtr` | Pointer to another record (club, nation, …). |

## Collections

| Collection | RTTI class | Record base (person part) | Count (test save) |
|---|---|---|---|
| Player | `db::ACTUAL_PLAYER` | +0x288 | 32,631 |
| Staff | `db::ACTUAL_NON_PLAYER` | +0x100 | 19,681 |
| Staff (player-managers) | `db::ACTUAL_PLAYER_AND_NON_PLAYER` | +0x380 | 171 |
| Staff (human managers) | `db::HUMAN_NON_PLAYER` | +0x440 | 2 |
| Club | `db::CLUB` | +0x0 | 36,123 |
| Competition | `db::COMP` | +0x0 | 8,217 |
| Nation | `db::NATION` | +0x0 | 251 |

**Sub-blocks of a person**

- **Player:** `db::PLAYING_DATA` starts at person−0x140.
- **Player-manager:** `db::PLAYING_DATA` starts at person−0x238. Because of that, player-managers are listed as Staff only; player fields would read wrong for them.
- **Staff:** `db::NON_PLAYING_DATA` starts at person−0xF8.

---

## Mapped

✅ = matched against in-game screens. 📋 = matched against known real-world data (league line-ups) but not yet tested in game. 📊 = inferred from statistics over every player; the game never shows the value, so it can't be confirmed.

### Player: identity and contract

| Field | Path | Offset | Type | Status |
|---|---|---|---|---|
| Id (unique ID, as used by graphics packs) | | 0xC | i32 | ✅ (0x8 is the row index, not the ID) |
| First name | [0x50] | 0x0 | fmString | ✅ (shared name-table entry: editing renames everyone who has that name) |
| Last name | [0x58] | 0x0 | fmString | ✅ (shared) |
| Common name | [0x60] | 0x0 | fmString | ✅ (null for most players) |
| Date of birth | | 0x88 | fmDate | ✅ checked on Haaland, Mbappé, Bellingham, Yamal and Guardiola |
| Nation | | 0x68 | entityPtr | ✅ |
| Height (cm) | | −0x5A | u8 | ✅ |
| Left foot | | −0x111 | u8 ×5 | ✅ |
| Right foot | | −0x110 | u8 ×5 | ✅ |
| Current ability | | −0x24 | i16 | ✅ 1 ≤ CA ≤ PA ≤ 200 across all players |
| Potential ability | | −0x22 | i16 | ✅ |
| Contract club | [0xA8, 0x10] | 0x30 | entityPtr | ✅ |
| Wage (per week, £) | [0xA8] | 0x20 | i32 | ✅ the game shows it per month (×52/12) |
| Contract start | [0xA8] | 0x44 | fmDate | ✅ |
| Contract expiry | [0xA8] | 0x48 | fmDate | ✅ |

### Player: attributes (u8, scale 5)

Every attribute was matched one byte at a time against four Real Madrid players' screens (Courtois, Huijsen, Mbappé, Espí). Goalkeeping was matched against Courtois and Lunin.

| Technical | Offset | Mental | Offset | Physical | Offset | Goalkeeping | Offset |
|---|---|---|---|---|---|---|---|
| Corners | −0x10E | Aggression | −0xFC | Acceleration | −0x107 | Aerial Reach | −0x11D |
| Crossing | −0x129 | Anticipation | −0x118 | Agility | −0xFB | Command of Area | −0x11C |
| Dribbling | −0x128 | Bravery | −0xFE | Balance | −0xFF | Communication | −0x11B |
| Finishing | −0x127 | Composure | −0xF5 | Jumping Reach | −0x102 | Eccentricity | −0x10A |
| First Touch | −0x113 | Concentration | −0xF4 | Natural Fitness | −0xF7 | Handling | −0x11E |
| Free Kicks | −0x106 | Decisions | −0x117 | Pace | −0x103 | Kicking | −0x11A |
| Heading | −0x126 | Determination | −0xF6 | Stamina | −0x104 | One on Ones | −0x116 |
| Long Shots | −0x125 | Flair | −0x10F | Strength | −0x105 | Punching | −0x108 |
| Long Throws | −0x10B | Leadership | −0x101 | | | Reflexes | −0x114 |
| Marking | −0x124 | Off the Ball | −0x123 | | | Rushing Out | −0x109 |
| Passing | −0x122 | Positioning | −0x115 | | | Throwing | −0x119 |
| Penalty Taking | −0x121 | Teamwork | −0x10D | | | | |
| Tackling | −0x120 | Vision | −0x11F | | | | |
| Technique | −0x112 | Work Rate | −0x10C | | | | |

### Player: hidden attributes (u8, scale 5) 📊

| Attribute | Offset | Evidence |
|---|---|---|
| Dirtiness | −0x100 | moves with Aggression |
| Injury Proneness | −0xF9 | moves against Natural Fitness |
| Versatility | −0xF8 | moves with the number of positions a player can play |

### Player: personality (u8, stored 1–20, no scaling)

| Attribute | Offset | Status |
|---|---|---|
| Adaptability | 0x70 | ✅ confirmed through staff screens (staff use the same byte) |
| Ambition | 0x71 | 📊 |
| Loyalty | 0x72 | 📊 |
| Pressure | 0x73 | 📊 |
| Professionalism | 0x74 | 📊 |
| Sportsmanship | 0x75 | 📊 |
| Temperament | 0x76 | 📊 |
| Controversy | 0x77 | 📊 |

The order fits the personality labels shown in game ("Model professional", "Ambitious", and so on).

### Staff

| Field | Path | Offset | Type | Status |
|---|---|---|---|---|
| Id (unique ID, as used by graphics packs) | | 0xC | i32 | ✅ (0x8 is the row index, not the ID) |
| First name / last name | [0x50] / [0x58] | 0x0 | fmString | ✅ |
| Date of birth | | 0x88 | fmDate | ✅ |
| Nation | | 0x68 | entityPtr | ✅ |
| Current ability | | −0x26 | i16 | ✅ (reads wrong for human managers, which use a different layout) |
| Potential ability | | −0x24 | i16 | ✅ (same caveat) |
| Adaptability | | 0x70 | u8 | ✅ |
| Contract club | [0xA8, 0x10] | 0x30 | entityPtr | ✅ |
| Wage (per week, £) | [0xA8] | 0x20 | i32 | ✅ |
| Contract expiry | [0xA8] | 0x48 | fmDate | ✅ |

### Club

| Field | Path | Offset | Type | Status |
|---|---|---|---|---|
| Id (unique ID, as used by graphics packs) | | 0xC | i32 | ✅ (0x8 is the row index, not the ID) |
| Name | | 0xC0 | fmString | ✅ |
| Short name | | 0xC8 | fmString | ✅ |
| Nation | | 0xD8 | entityPtr | ✅ |
| Balance (£) | [0x150] | 0xC | i32 | ✅ |
| Transfer budget (£) | [0x150] | 0x7C4 | i32 | ✅ value matches the screen; see the note below |
| Wage budget (per week, £) | [0x150] | 0x808 | i32 | ✅ |
| League (first team's) | [0x108] | 0x48 | entityPtr | 📋 read-only. Matched all 20 Premier League and 20 LaLiga clubs; every one of the 238 hits is in its own nation's league. Only clubs in leagues loaded in the save have it: elsewhere [0x108] is null. The slots at 0x278, 0x3E8, 0x558, 0x6C8 (0x170 apart) look like the B/youth teams. |

**Transfer budget copies:** the same value is also stored at +0x7C8 and +0x7D4. It's not yet known which copy the game actually uses, so editing +0x7C4 alone may not stick.

**Found but not yet in the definition** (all under the finance block at [0x150]):

| Offset | Meaning |
|---|---|
| +0x7CC | guaranteed minimum transfer budget |
| +0x7D0 | % of transfer income given back to the budget |
| +0x814 | current weekly wage bill |
| +0x834 | next season's wage budget |

### Competition

| Field | Offset | Type |
|---|---|---|
| Id (unique ID) | 0xC | i32 |
| Name | 0x40 | fmString |
| Short name | 0x50 | fmString |
| Nation | 0x60 | entityPtr |

### Nation

| Field | Offset | Type |
|---|---|---|
| Id (unique ID) | 0xC | i32 |
| Name | 0x18 | fmString |

---

## Missing

### Player

| Field | What's known / next step |
|---|---|
| Consistency, Important Matches | They are −0xFD and −0xFA, but which is which is unknown. Telling them apart needs a player whose two values clearly differ, as shown by a scout report or an in-game editor. |
| Positions (GK … ST) | A 15-byte block at person−0x138 (`PLAYING_DATA`+0x8). GK is index 0 and ST is index 12; the left/right columns aren't resolved. Needs screens of players with a strongly one-sided position (for example a pure left back). |
| Second nation | Not searched yet. |
| Weight | Not searched yet; probably next to Height (−0x5A). |
| Reputation (current, home, world) | Not searched yet. |
| Value | Not searched yet. |
| Trained at club / trained in nation | Not searched yet. |
| Looks (skin tone, hair colour, hair length) | Not searched yet. |
| Condition, match sharpness, jadedness, morale | Not searched yet. These change every day, so they're found by watching a value change over a match or a training week. |
| Injured / banned / retired from international football | Not searched yet. |
| Squad status, release clause, contract happiness | Probably inside the contract object at [0xA8]; not searched yet. |
| Future transfer (club, date, fee) | Not searched yet. |

### Staff

| Field | What's known / next step |
|---|---|
| Coaching attributes (Attacking, Defending, Fitness, GK Distribution, GK Handling, GK Shot Stopping, Mental, Tactical, Technical, Working with Youngsters) | All of these live in `NON_PLAYING_DATA`, person−0xF0 to −0xBD, but individual bytes aren't labelled. The game shows staff attributes only as bands (Élite, Muy bueno, Bueno…), and the bands don't map to fixed values across staff, so four screenshots can't pin them down. Ideas: get numeric values from a screen that shows them, or collect many more staff screens across different roles. |
| Determination, Level of Discipline, Motivating, People Management | Same block and same problem. |
| Judging Player Ability / Potential, Judging Staff Ability, Negotiating, Tactical Knowledge, Physiotherapy, Sports Science | Same block and same problem. The strongest pair, −0xD4/−0xD3 (r = 0.90), is probably Judging Player Ability and Potential. |
| Role | Not searched yet. |
| Reputation | Not searched yet. |
| Human manager CA/PA | `HUMAN_NON_PLAYER` uses a different layout, so −0x26/−0x24 read garbage for it. |

### Club

| Field | What's known / next step |
|---|---|
| Which transfer-budget copy the game uses | Edit each copy (+0x7C4, +0x7C8, +0x7D4) in turn and see which one the finance screen follows. |
| League (clubs in leagues not loaded in the save) | [0x108] is null for them, so their league isn't linked from the club. Probably only on the competition side, if at all. |
| Reputation, team cohesion | Not searched yet. |
| Max wage, ticket price, season ticket price | Probably in the finance block at [0x150]; not searched yet. |
| Facilities (training, youth, corporate, youth recruitment, junior coaching) | Not searched yet. |
| Kit colours | Not searched yet. |
| Stadium name, capacity | Not searched yet; the stadium is probably a separate record that the club points to. |
| League standing (position, played, W/D/L, goals, points) | Not searched yet; probably stored on the competition side, not the club. |

### Competition

| Field | What's known / next step |
|---|---|
| Reputation, prize money | Not searched yet. |

### Nation

| Field | What's known / next step |
|---|---|
| Continent, reputation | Not searched yet. |
| Rules (EU free movement, work permits, non-EU limit) | Not searched yet. |
