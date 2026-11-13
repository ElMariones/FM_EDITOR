# FM27 memory addresses

This file covers Football Manager 2027 **27.0.1+2494687**, module `game_plugin.dll`. The machine-readable copy that the app uses is [definitions/fm27-27.0.1.json](definitions/fm27-27.0.1.json). This file is the human-readable summary of it, plus what's still missing.

Everything here was found by looking at the game's own files and memory: RTTI class names, object layouts, comparisons against in-game screens, and the game's own field-access code. Nothing was copied from closed-source tools.

## How the second pass was done

The first pass matched bytes against screens one field at a time. The second pass reads the answers out of the game itself:

1. **`shared/data/editor.fmf`** is a zstd-packed archive of XML that lists every database field the game knows, per record type, with a 4-character code, a name, a type and a range (`PCAB` = Current Ability, `Scpy` = stadium Capacity, and so on). That is the checklist: about 2,250 codes.
2. **Every `db::` record class has a "get field" and a "set field" virtual function** (vtable slots 1 and 2) that switch on those same codes. The real code sits in the DLL's first section, which is named `.data`; the section names are scrambled.
3. **[tools/research](tools/research)** disassembles each handler, follows the calls it makes (resolving virtual calls against live objects), and records which bytes of the record the handler reads or writes. That gives a location, a width and, for packed flags, a bit.
4. **Each location is then checked against a few hundred live records.** Numbers must fit the range the schema gives, strings must decode, and pointers must land on records of one kind. Anything that fails, or that shares its location with a differently named field, is dropped.

Fields found this way are marked 🔧 below. They have been checked against live values and, where the answer is public knowledge, against the real world (Real Madrid: founded 1902, Santiago Bernabéu, 83,186 seats, built 14 Dec 1947, at 40.45°N 3.69°W). A handful were also read off in-game screens (marked ✅ where so). None has been edited in game yet.

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
| Stadium | `db::STADIUM` | +0x0 | 32,434 |
| City | `db::CITY` | +0x0 | 58,475 |
| Team | `db::TEAM` | +0x0 | 79,045 |
| Continent | `db::CONTINENT` | +0x0 | 7 |
| Local region | `db::LOCAL_REGION` | +0x0 | 2,532 |
| Award | `db::AWARD` | +0x0 | 3,902 |
| Injury | `db::INJURY` | +0x0 | 137 |
| Language | `db::LANGUAGE` | +0x0 | 126 |
| Media source | `db::MEDIA_SOURCE` | +0x0 | 369 |
| Derby | `db::DERBY` | +0x0 | 1,681 |
| Stage name | `db::STAGE_NAME` | +0x0 | 288 |
| Weather | `db::WEATHER` | +0x0 | 398 |
| Currency | `db::CURRENCY_RECORD` | +0x0 | 173 |

Every record starts the same way: vtable, then the row index at +0x8 and the unique ID at +0xC.

**A club's teams.** `db::CLUB`+0x18 is a vector of `db::TEAM` pointers; the first entry is the first team. The league, stadium and reputation live on the team, so a club's are read through `[0x18, 0x0]`. This works for every club, not only those in loaded leagues.

**The attribute array.** Player positions and attributes are one byte array starting at person−0x138, indexed by the game's attribute number: 0–14 are the positions (stored 1–20), the rest are attributes (stored 1–100). Staff have the same arrangement in `db::NON_PLAYING_DATA`.

**Sub-blocks of a person**

- **Player:** `db::PLAYING_DATA` starts at person−0x140.
- **Player-manager:** `db::PLAYING_DATA` starts at person−0x238. Because of that, player-managers are listed as Staff only; player fields would read wrong for them.
- **Staff:** `db::NON_PLAYING_DATA` starts at person−0xF8.

---

## Mapped

✅ = matched against in-game screens. 🔧 = read out of the game's field-access code and checked against live records (see above). 📋 = matched against known real-world data (league line-ups) but not yet tested in game. 📊 = inferred from statistics over every player; the game never shows the value, so it can't be confirmed.

### Human managers ✅

`db::HUMAN_NON_PLAYER` (person part at +0x440) is the class of the managers the players control. It is one of the Staff classes. The definition marks it `"human": true`, and the editor reads `Contract.Club` from each one for **My club**. Checked on 27.0.1: a one-player save had 2 objects. One was the real manager, linked to his club. The other was a profile copy, named in brackets after the online name, with no club.

### Player: injuries ✅

Current injuries hang off person−0x190: a pointer to an injury holder whose +0x0/+0x8 is a std::vector of pointers, one per current injury (+0x68 is a second vector, past injuries). Most players have no holder; 3,648 of 143,138 had one current injury in the save it was mapped on. Each injury entry: +0x8 → `db::INJURY`, +0x18 → the player's team, +0x20 start date, +0x28 and +0x2A two u16 day counters (12 and 7 for a hamstring strain shown as 2–9 days out). The `Pijl` field code reads `db::STARTING_INJURY`, the database's start-of-game table, which is empty in a running game.

Heal: zeroing both counters (`listFlag`, elementOffset 0x28, 4 bytes). Checked on 27.0.1 (Suk Hyun-Jun, hamstring strain): the game showed him fit after one day. The entry itself was still in the vector then, with both counters at 0; whether the game removes it later is not checked.

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

### Player: hidden attributes (u8, scale 5) 🔧

| Attribute | Offset | Evidence |
|---|---|---|
| Consistency | −0xFD | attribute 0x3B in the game's getter |
| Dirtiness | −0x100 | attribute 0x38; also moves with Aggression |
| Important Matches | −0xFA | attribute 0x3E |
| Injury Proneness | −0xF9 | attribute 0x3F; also moves against Natural Fitness |
| Versatility | −0xF8 | attribute 0x40; also moves with the number of positions a player can play |

### Player: positions (u8, stored 1–20, no scaling) 🔧

| GK | DL | DC | DR | DM | ML | MC | MR | AML | AMC | AMR | ST | WBL | WBR |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| −0x138 | −0x136 | −0x135 | −0x134 | −0x133 | −0x132 | −0x131 | −0x130 | −0x12F | −0x12E | −0x12D | −0x12C | −0x12B | −0x12A |

ML is the one position the getter analysis did not reach. It is the gap in the run, and its values are 1–20 like the others. −0x137 is not used by any handler.

### Player: personality (u8, stored 1–20, no scaling) 🔧

| Adaptability | Ambition | Loyalty | Pressure | Professionalism | Sportsmanship | Temperament | Controversy |
|---|---|---|---|---|---|---|---|
| 0x70 | 0x71 | 0x72 | 0x73 | 0x74 | 0x75 | 0x76 | 0x77 |

The order comes from the person getters (`Pada`, `Pamb`, `Ploy`, `Ppre`, `Ppro`, `Pspo`, `Ptem`, `Pcvy`), so it is no longer a guess. Staff have the same eight bytes.

### Person: looks, birth and caps 🔧

These sit in the shared person part, so players and staff both have them.

| Field | Offset | Type | Notes |
|---|---|---|---|
| Ethnicity | 0x8D | u8 | the game's own code |
| Hair colour | 0x8E | u8 | shade number 1–18; the game maps it to seven named colours |
| Hair length | 0x8F | u8 | 0 bald, 1 short, 2 medium, 3 long |
| Skin tone | 0x90 | u8 | 1–20 |
| City of birth | 0x80 | entityPtr | ✅ Mbappé reads Bondy, Camavinga Cabinda |
| Date joined club | 0x11C | fmDate | ✅ Camavinga reads 31 Aug 2021 |
| International apps / goals | 0x134 / 0x136 | i16 | ✅ Camavinga shows 29 caps, 2 goals in game |
| U21 apps / goals | 0x138 / 0x139 | u8 | ✅ Camavinga shows 13 and 2 in game |
| Morale (players) | −0x1C | u8 | 1–20. The setter clamps through a function, and the wording the game shows for each number is not checked. |

The getters reach these through the class's virtual-base table, which is why the first pass of the analyser missed them.

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

### Staff: job 🔧

One byte at person−0x22. The numbers were labelled from the in-game staff list (Real Madrid) and from each number's attribute profile across all 19,792 staff (the physio numbers have high Physiotherapy, the scout numbers high Judging Ability, and so on).

| Stored | Job | Stored | Job | Stored | Job |
|---|---|---|---|---|---|
| 0 | none | 20 | Assistant Manager ✅ | 48 | Sports Scientist |
| 2 | Coach ✅ | 22 | Set Piece Coach ✅ | 50 | Head Physio |
| 4 | Chairperson ✅ | 26 | Fitness Coach ✅ | 58 | Recruitment Analyst |
| 6 | Director | 34 | Goalkeeping Coach ✅ | 60 | Performance Analyst ✅ |
| 8 | Managing Director | 38 | Head of Sports Medicine | 62 | Chief Analyst |
| 10 | Director of Football | 40 | Head of Sports Science | 64 | Head of Youth Development |
| 12 | Physio | 44 | Chief Scout | 66 | Owner |
| 14 | Scout | 46 | Doctor | 88 | Technical Director |
| 16 | Manager ✅ | | | 80, 86 | not identified |

An odd number is the same job held by someone who still plays (3, 17, 21, 35). The definition lists the stored numbers in the field's `values`, so the editor shows the names.

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
| League (first team's) | [0x18, 0x0] | 0x50 | entityPtr | 🔧 read-only. The first team's division. It also works for clubs in leagues that are not loaded, which the earlier [0x108]+0x48 link did not. |

**Transfer budget copies:** the same value is also stored at +0x7C8 and +0x7D4. The finance object's own set-field handler (`Ctfc`) writes +0x7C4, so that is the one the editor writes. Whether the game then refreshes the other two by itself has not been tested.

**More finance fields** 🔧 (same block): average match ticket price +0x10 (f32), average season ticket price +0x14 (f32), transfer embargo start / end / appeal dates +0x24 / +0x28 / +0x2C, rich-benefactor code +0x34.

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
| Short name | 0x48 | fmString |
| Three letter name | 0x50 | fmString |
| Nation | 0x60 | entityPtr |

### Nation

| Field | Offset | Type |
|---|---|---|
| Id (unique ID) | 0xC | i32 |
| Name | 0x18 | fmString |

---

## Mapped from the game's field code 🔧

The *Code* column is the game's own field code from `editor.fmf`; rows without one were added by hand from the same analysis. *Live check* is what the values looked like across a sample of records in the test save. Player and staff offsets are relative to the person part, as above. Rows named like `Pos.ST` or `Reputation.Current` fill in properties the editor already had.

### Player

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Attr.Consistency |  |  | −0xFD | u8 ×5 | u8 20..100 (17 distinct) |
| Attr.ImportantMatches |  |  | −0xFA | u8 ×5 | u8 5..100 (20 distinct) |
| Pos.ML |  |  | −0x132 | u8 | u8 1..20 (17 distinct) |
| Morale |  |  | −0x1C | u8 | u8 1..20 (20 distinct) |
| Looks.SkinTone |  |  | 0x90 | u8 | u8 0..19 (20 distinct) |
| Looks.HairColour |  |  | 0x8E | u8 | u8 2..17 (15 distinct) |
| Looks.HairLength |  |  | 0x8F | u8 | u8 0..4 (5 distinct) |
| Ethnicity (code) |  |  | 0x8D | u8 | u8 0..10 (8 distinct) |
| Current reputation | `PCRI` |  | −0x28 | i16 | i16 50..8700 (114 distinct) |
| Home reputation | `PHRI` |  | −0x2A | i16 | i16 50..9000 (112 distinct) |
| Shirt name | `PShN` |  | −0x198 | fmString | str e.g. 'N. Aguerd' (49/400 set) |
| World reputation | `PWRI` |  | −0x26 | i16 | i16 50..7750 (106 distinct) |
| Striker | `Pace` |  | −0x12C | u8 | u8 1..20 (17 distinct) scale 1 |
| Attacking midfielder central | `Pamc` |  | −0x12E | u8 | u8 1..20 (16 distinct) scale 1 |
| Attacking midfielder left | `Paml` |  | −0x12F | u8 | u8 1..20 (17 distinct) scale 1 |
| Attacking midfielder right | `Pamr` |  | −0x12D | u8 | u8 1..20 (15 distinct) scale 1 |
| Body type | `Pbdt` |  | −0x58 | u8 | u8 1..4 (4 distinct) |
| Transfer value | `PbvE` |  | −0x54 | i32 | i32 -1..300000000 (328 distinct) |
| Overall physical condition | `Pcon` |  | −0x2C | i16 | i16 7000..10000 (83 distinct) |
| Defender central | `Pdce` |  | −0x135 | u8 | u8 1..20 (12 distinct) scale 1 |
| Defender left | `Pdls` |  | −0x136 | u8 | u8 1..20 (16 distinct) scale 1 |
| Defensive midfielder | `Pdmc` |  | −0x133 | u8 | u8 1..20 (18 distinct) scale 1 |
| Defender right | `Pdrs` |  | −0x134 | u8 | u8 1..20 (14 distinct) scale 1 |
| Match sharpness | `Pfit` |  | −0x30 | i16 | i16 3500..10000 (133 distinct) |
| Goalkeeper | `Pgoa` |  | −0x138 | u8 | u8 1..20 (4 distinct) scale 1 |
| International retirement date | `Pird` | [-0x168] | 0x28 | fmDate | date e.g. day 1 of 1900 |
| Fatigue | `Pjad` |  | −0x2E | i16 | i16 -250..456 (7 distinct) |
| Preferred central position | `Plcp` |  | −0x12 | u8 | u8 0..7 (8 distinct) |
| Midfielder central | `Pmce` |  | −0x131 | u8 | u8 1..20 (16 distinct) scale 1 |
| Midfielder right | `Pmrs` |  | −0x130 | u8 | u8 1..20 (16 distinct) scale 1 |
| Wing-Back left | `Pwbl` |  | −0x12B | u8 | u8 1..20 (14 distinct) scale 1 |
| Wing-Back right | `Pwbr` |  | −0x12A | u8 | u8 1..20 (14 distinct) scale 1 |
| Date when moved to based nation | `PCNj` |  | 0x120 | fmDate | date e.g. day 244 of 2025 |
| City of birth | `Pcbi` |  | 0x80 | entityPtr | -> City (400/400 set) |
| Date joined | `Pcjd` |  | 0x11C | fmDate | date e.g. day 244 of 2025 |
| International apps | `Piap` |  | 0x134 | i16 | i16 0..137 (39 distinct) |
| Is female | `Pife` |  | 0x19 | bool bit 0x10 | bit 0x10: 0/400 set |
| International goals | `Pigl` |  | 0x136 | i16 | i16 0..94 (13 distinct) |
| U21 international apps | `Puia` |  | 0x138 | u8 | u8 0..28 (22 distinct) |
| U21 international goals | `Puig` |  | 0x139 | u8 | u8 0..14 (9 distinct) |
| Default language | `dfln` | [0x68, 0x48] | 0x0 | entityPtr | -> Language (400/400 set) |

### Staff

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Role |  |  | −0x22 | u8 | u8 0..64 (23 distinct) |
| Skin tone |  |  | 0x90 | u8 | u8 0..19 (20 distinct) |
| Hair colour |  |  | 0x8E | u8 | u8 1..18 (18 distinct) |
| Hair length |  |  | 0x8F | u8 | u8 0..4 (5 distinct) |
| Ethnicity (code) |  |  | 0x8D | u8 | u8 0..10 (9 distinct) |
| Buying players | `Nbpl` |  | −0xE2 | u8 | u8 1..20 (20 distinct) scale 1 |
| Business | `Nbus` |  | −0xEF | u8 | u8 1..19 (19 distinct) scale 1 |
| Attacking | `Ncha` |  | −0xCE | u8 ×5 | u8 5..95 (60 distinct) scale 5 |
| Defending | `Nchd` |  | −0xCD | u8 ×5 | u8 5..100 (58 distinct) scale 5 |
| Fitness | `Nchf` |  | −0xCC | u8 ×5 | u8 1..92 (58 distinct) scale 5 |
| Possession | `Nchm` |  | −0xCB | u8 ×5 | u8 5..100 (66 distinct) scale 5 |
| Current reputation | `NcrI` |  | −0x2A | i16 | i16 15..8250 (156 distinct) |
| Set pieces | `Ncsp` |  | −0xBD | u8 ×5 | u8 5..70 (16 distinct) scale 5 |
| Tactical | `Ncta` |  | −0xC9 | u8 ×5 | u8 5..100 (70 distinct) scale 5 |
| Technical | `Ncte` |  | −0xCA | u8 ×5 | u8 3..95 (57 distinct) scale 5 |
| Dirtiness allowance | `Ndal` |  | −0xC8 | u8 ×5 | u8 5..95 (19 distinct) scale 5 |
| Depth | `Ndep` |  | −0xDD | u8 | u8 1..19 (19 distinct) scale 1 |
| Directness | `Ndir` |  | −0xED | u8 | u8 1..20 (20 distinct) scale 1 |
| Flexibility | `Nflx` |  | −0xDB | u8 | u8 1..19 (19 distinct) scale 1 |
| Hardness of training | `Nhat` |  | −0xDA | u8 | u8 1..20 (19 distinct) scale 1 |
| Home reputation | `NhmI` |  | −0x2C | i16 | i16 15..8750 (157 distinct) |
| Interference | `Nint` |  | −0xEA | u8 | u8 1..20 (20 distinct) scale 1 |
| Mind games | `Nmig` |  | −0xE1 | u8 | u8 1..20 (20 distinct) scale 1 |
| Negotiating | `Nneg` |  | −0xBF | u8 ×5 | u8 1..100 (78 distinct) scale 5 |
| Side of game willing to work | `NpGw` |  | −0x16 | u8 | u8 1..3 (3 distinct) |
| Sports science | `NpIM` |  | −0xC1 | u8 ×5 | u8 5..90 (43 distinct) scale 5 |
| Attacking formation | `Npaf` |  | −0x1E | u8 | u8 3..64 (11 distinct) |
| Patience | `Npat` |  | −0xE7 | u8 | u8 1..20 (20 distinct) scale 1 |
| Coaching style | `Npcs` |  | −0x17 | u8 | u8 0..9 (10 distinct) |
| Determination | `Npde` |  | −0xE3 | u8 ×5 | u8 5..100 (81 distinct) scale 5 |
| Defensive formation | `Npdf` |  | −0x1F | u8 | u8 3..69 (12 distinct) |
| Goalkeeping | `Npgs` |  | −0xD5 | u8 ×5 | u8 5..80 (22 distinct) scale 5 |
| Judging player ability | `Npja` |  | −0xD4 | u8 ×5 | u8 5..100 (76 distinct) scale 5 |
| Analysing data | `Npjd` |  | −0xC4 | u8 ×5 | u8 5..100 (40 distinct) scale 5 |
| Judging player potential | `Npjp` |  | −0xD3 | u8 ×5 | u8 5..95 (83 distinct) scale 5 |
| Judging staff ability | `Npjs` |  | −0xBE | u8 ×5 | u8 4..92 (76 distinct) scale 5 |
| Authority | `Npld` |  | −0xEC | u8 | u8 1..20 (20 distinct) scale 1 |
| People management | `Npmm` |  | −0xD2 | u8 ×5 | u8 4..100 (80 distinct) scale 5 |
| Motivating | `Npmo` |  | −0xD1 | u8 ×5 | u8 5..100 (72 distinct) scale 5 |
| Preferred formation | `Nppf` |  | −0x21 | u8 | u8 3..64 (11 distinct) |
| Physiotherapy | `Npph` |  | −0xD0 | u8 ×5 | u8 3..100 (51 distinct) scale 5 |
| Trigger press | `Npre` |  | −0xE6 | u8 | u8 1..20 (20 distinct) scale 1 |
| Second preferred formation | `Npsf` |  | −0x20 | u8 | u8 3..64 (12 distinct) |
| Tactical knowledge | `Nptk` |  | −0xCF | u8 ×5 | u8 1..95 (76 distinct) scale 5 |
| Versatility | `Npvr` |  | −0xC5 | u8 ×5 | u8 5..100 (20 distinct) scale 5 |
| Working with youngsters | `Npwy` |  | −0xE4 | u8 | u8 1..20 (20 distinct) scale 1 |
| Resources | `Nres` |  | −0xE5 | u8 | u8 1..17 (16 distinct) scale 1 |
| Squad rotation | `Nrot` |  | −0xD9 | u8 | u8 1..19 (19 distinct) scale 1 |
| Tempo | `Ntem` |  | −0xD8 | u8 | u8 1..20 (20 distinct) scale 1 |
| Width | `Nwid` |  | −0xD7 | u8 | u8 1..20 (20 distinct) scale 1 |
| World reputation | `NwrI` |  | −0x28 | i16 | i16 4..6750 (172 distinct) |
| Attacking formation (out of possession) | `Opaf` |  | −0x1A | u8 | u8 4..64 (10 distinct) |
| Defensive formation (out of possession) | `Opdf` |  | −0x1B | u8 | u8 4..69 (11 distinct) |
| Height | `Phes` |  | −0x5A | i16 | i16 0..0 (1 distinct) |
| Preferred set piece defensive marking | `SPDm` |  | −0xF | u8 | u8 0..2 (3 distinct) |
| Preferred set piece delivery target | `SPaf` |  | −0x10 | u8 | u8 0..2 (3 distinct) |
| Preferred set piece attacking numbers | `SPan` |  | −0xE | u8 | u8 0..2 (3 distinct) |
| Preferred set piece defensive numbers | `SPdn` |  | −0xD | u8 | u8 0..2 (3 distinct) |
| Preferred set piece delivery type | `SPdt` |  | −0x12 | u8 | u8 0..1 (2 distinct) |
| Preferred set piece post defence | `SPpd` |  | −0x11 | u8 | u8 0..2 (3 distinct) |
| Preferred set piece defending free kicks | `Spdl` |  | −0xC | u8 | u8 0..2 (3 distinct) |
| Preferred formation (out of possession) | `oopF` |  | −0x1D | u8 | u8 4..64 (10 distinct) |
| Second preferred formation (out of possession) | `oopS` |  | −0x1C | u8 | u8 4..64 (11 distinct) |
| Date when moved to based nation | `PCNj` |  | 0x120 | fmDate | date e.g. day 182 of 2023 |
| Ambition | `Pamb` |  | 0x71 | u8 | u8 1..18 (18 distinct) scale 1 |
| City of birth | `Pcbi` |  | 0x80 | entityPtr | -> City (400/400 set) |
| Date joined | `Pcjd` |  | 0x11C | fmDate | date e.g. day 182 of 2023 |
| Common name | `Pcna` | [0x60] | 0x0 | fmString | str e.g. 'Bolo' (20/20 set) |
| Controversy | `Pcvy` |  | 0x77 | u8 | u8 1..18 (18 distinct) scale 1 |
| International apps | `Piap` |  | 0x134 | i16 | i16 0..141 (32 distinct) |
| International goals | `Pigl` |  | 0x136 | i16 | i16 0..29 (13 distinct) |
| Loyalty | `Ploy` |  | 0x72 | u8 | u8 2..20 (17 distinct) scale 1 |
| Pressure | `Ppre` |  | 0x73 | u8 | u8 3..19 (17 distinct) scale 1 |
| Professionalism | `Ppro` |  | 0x74 | u8 | u8 1..20 (20 distinct) scale 1 |
| Sportsmanship | `Pspo` |  | 0x75 | u8 | u8 2..20 (19 distinct) scale 1 |
| Temperament | `Ptem` |  | 0x76 | u8 | u8 3..20 (18 distinct) scale 1 |
| U21 international apps | `Puia` |  | 0x138 | u8 | u8 0..27 (12 distinct) |
| U21 international goals | `Puig` |  | 0x139 | u8 | u8 0..8 (6 distinct) |
| Default language | `dfln` | [0x68, 0x48] | 0x0 | entityPtr | -> Language (400/400 set) |

### Club

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Finance.TicketPrice |  | [0x150] | 0x10 | f32 | f32 -1..33.4 |
| Finance.SeasonTicketPrice |  | [0x150] | 0x14 | f32 | f32 0..490 |
| Transfer embargo start |  | [0x150] | 0x24 | fmDate | date e.g. day 1 of 1900 |
| Transfer embargo end |  | [0x150] | 0x28 | fmDate | date e.g. day 1 of 2027 |
| Transfer embargo appeal date |  | [0x150] | 0x2C | fmDate | date e.g. day 1 of 1900 |
| Facilities.Youth |  | [0x100] | 0x12B | u8 | u8 1..255 (18 distinct) |
| Facilities.YouthRecruitment |  | [0x100] | 0x12D | u8 | u8 1..255 (20 distinct) |
| Facilities.JuniorCoaching |  | [0x100] | 0x12C | u8 | u8 1..255 (18 distinct) |
| Stadium.Name |  | [0x18, 0x0, 0x78] | 0x40 | fmString | str e.g. 'Estadio Besa' (313/313 set) |
| Stadium.Capacity |  | [0x18, 0x0, 0x78] | 0x7C | i32 | i32 100..68700 (105 distinct) |
| Stadium |  | [0x18, 0x0] | 0x78 | entityPtr | -> Stadium (313/394 set) |
| Second preferred formation | `C2pf` | [0xB0] | 0x75 | u8 | u8 0..64 (6 distinct) |
| Attacking formation | `Cafo` | [0xB0] | 0x77 | u8 | u8 0..64 (6 distinct) |
| Based nation | `Cbni` |  | 0xE8 | entityPtr | -> Nation (400/400 set) |
| Continental cup nation | `Cccn` |  | 0xF0 | entityPtr | -> Nation (400/400 set) |
| Corporate facilities | `Ccfa` | [0x150] | 0x8A9 | u8 | u8 0..16 (3 distinct) scale 1 |
| City | `Ccty` |  | 0xF8 | entityPtr | -> City (374/400 set) |
| Defensive formation | `Cdfo` | [0xB0] | 0x76 | u8 | u8 0..64 (6 distinct) |
| Division | `Cdvi` | [0x18, 0x0] | 0x50 | entityPtr | -> Competition (326/394 set) |
| Local region | `Clri` | [0xF8] | 0x38 | entityPtr | -> LocalRegion (348/374 set) |
| Morale | `Cmle` | [0x100] | 0x121 | u8 | u8 0..20 (19 distinct) scale 1 |
| Preferred formation | `Cpfr` | [0xB0] | 0x74 | u8 | u8 0..64 (9 distinct) |
| Status | `Cprs` |  | 0x16A | u8 | u8 1..3 (3 distinct) |
| Reputation | `Crep` | [0x18, 0x0] | 0xA8 | i16 | i16 1..8050 (91 distinct) |
| Number of season ticket holders | `Csth` | [0x100] | 0xC0 | i32 | i32 0..38000 (87 distinct) |
| Six letter name | `Csxn` | [0x100] | 0x0 | fmString | str e.g. 'Besa' (286/400 set) |
| Training | `Ctlv` | [0x100] | 0x120 | u8 | u8 1..18 (17 distinct) scale 1 |
| Training ground | `Ctsi` |  | 0x140 | entityPtr | -> Stadium (69/400 set) |
| Year founded | `Cyfn` | [0x100] | 0xD8 | i16 | i16 0..2025 (118 distinct) |
| Youth importance | `Cyim` | [0x100] | 0x126 | u8 | u8 1..20 (20 distinct) |

### Competition

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Trophy type | `CTty` |  | 0x100 | u8 | u8 0..9 (9 distinct) |
| Lowest official category allowed | `LAUC` |  | 0x102 | u8 | u8 0..5 (5 distinct) |
| Player gender | `PLgn` |  | 0xF9 | u8 | u8 0..1 (2 distinct) |
| Background colour (ARGB) | `cbcl` |  | 0x134 | i32 | i32 -16777216..-460552 (37 distinct) |
| Continent | `ccti` |  | 0x58 | entityPtr | -> Continent (397/400 set) |
| East boundary (regional division) | `ceas` |  | 0x98 | entityPtr | -> City (30/400 set) |
| Foreground colour (ARGB) | `cfcl` |  | 0x130 | i32 | i32 -16777216..-460552 (30 distinct) |
| Minimum pitch length | `cmnl` |  | 0x164 | i16 | i16 900..1050 (6 distinct) |
| Minimum pitch width | `cmnw` |  | 0x166 | i16 | i16 550..680 (5 distinct) |
| Maximum pitch length | `cmxl` |  | 0x168 | i16 | i16 1000..1200 (6 distinct) |
| Maximum pitch width | `cmxw` |  | 0x16A | i16 | i16 600..900 (9 distinct) |
| North boundary (regional division) | `cnor` |  | 0x80 | entityPtr | -> City (31/400 set) |
| Overriden subs rules | `cosr` |  | 0xFD | u8 | u8 255..255 (1 distinct) |
| Parent competition | `cpcm` |  | 0x68 | entityPtr | -> Competition (191/400 set) |
| Reputation | `crep` |  | 0x188 | i16 | i16 0..170 (95 distinct) |
| Short name | `csnm` |  | 0x48 | fmString | str e.g. 'Jupiler Pro League' (400/400 set) |
| South boundary (regional division) | `csou` |  | 0x88 | entityPtr | -> City (32/400 set) |
| Uses seated stadiums only | `csso` |  | 0xFC | bool | bool 3/400 set |
| Trim colour (ARGB) | `ctcl` |  | 0x138 | i32 | i32 -16777216..-460552 (26 distinct) |
| Uses extra officials | `cuae` |  | 0xFB | bool | bool 1/400 set |
| West boundary (regional division) | `cwes` |  | 0x90 | entityPtr | -> City (29/400 set) |
| Type | `dbty` |  | 0xF8 | u8 | u8 0..255 (28 distinct) |
| Division level | `levl` |  | 0x1AA | u8 | u8 0..100 (11 distinct) |
| Winter ball colour (ARGB) | `wbcl` |  | 0x15C | i32 | i32 -2064384..-460800 (13 distinct) |
| Winter ball end date | `wben` |  | 0x154 | fmDate | date e.g. day 1 of 1900 |
| Winter ball start date | `wbst` |  | 0x150 | fmDate | date e.g. day 1 of 1900 |

### Nation

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Three letter name | `N3ln` |  | 0x28 | fmString | str e.g. 'ALG' (251/251 set) |
| Youth rating | `NYRt` | [0x108] | 0x86C | i16 | i16 0..163 (102 distinct) |
| Attacking formation | `Nafm` | [0x108, 0xB0] | 0x77 | u8 | u8 0..64 (11 distinct) |
| Year association formed | `Nafy` | [0x108] | 0x868 | i16 | i16 0..2011 (92 distinct) |
| Attacking | `Natt` | [0x108] | 0x889 | u8 | u8 0..16 (17 distinct) scale 1 |
| Continent | `Ncti` |  | 0xF0 | entityPtr | -> Continent (229/251 set) |
| Currency | `Ncur` |  | 0x100 | entityPtr | -> Currency (229/251 set) |
| Depth | `Ndep` | [0x108] | 0x88A | u8 | u8 0..19 (18 distinct) scale 1 |
| Defensive formation | `Ndfm` | [0x108, 0xB0] | 0x76 | u8 | u8 0..69 (11 distinct) |
| Directness | `Ndir` | [0x108] | 0x88B | u8 | u8 0..18 (18 distinct) scale 1 |
| Economic factor | `Necf` |  | 0x1F0 | u8 | u8 0..20 (21 distinct) scale 1 |
| FA financial power | `Nfap` | [0x108] | 0x883 | u8 | u8 0..20 (21 distinct) scale 1 |
| Year affiliated | `Nfay` | [0x108] | 0x864 | i16 | i16 0..2016 (66 distinct) |
| Year affiliated to federation | `Nfdy` | [0x108] | 0x866 | i16 | i16 0..2017 (55 distinct) |
| Worldwide governing body full member | `Nffm` | [0x108] | 0x87F | bool | bool 211/251 set |
| Flamboyancy | `Nflm` | [0x108] | 0x88C | u8 | u8 0..15 (4 distinct) scale 1 |
| Flexibility | `Nflx` | [0x108] | 0x88D | u8 | u8 0..17 (18 distinct) scale 1 |
| Foreign manager likelihood | `Nfml` | [0x108] | 0x886 | u8 | u8 1..20 (20 distinct) scale 1 |
| FA patience | `Nfpa` | [0x108] | 0x885 | u8 | u8 0..20 (19 distinct) scale 1 |
| Free roles | `Nfrl` | [0x108] | 0x88E | u8 | u8 0..18 (16 distinct) scale 1 |
| Ranking points | `Nfrp` | [0x108] | 0x274 | f32 | f32 0..2e+03 |
| Gain nationality type | `Ngct` |  | 0x1EB | u8 | u8 1..2 (2 distinct) |
| Game importance | `Ngim` | [0x108] | 0x87D | u8 | u8 0..4 (5 distinct) |
| Marking | `Nmar` | [0x108] | 0x88F | u8 | u8 0..18 (18 distinct) scale 1 |
| Max. youth age | `Nmya` | [0x108] | 0x884 | u8 | u8 17..21 (5 distinct) |
| National stadium | `Nnsi` |  | 0x118 | entityPtr | -> Stadium (209/251 set) |
| Nationality name | `Nnty` |  | 0x30 | fmString | str e.g. 'Argelino' (251/251 set) |
| Offside | `Noff` | [0x108] | 0x890 | u8 | u8 0..17 (16 distinct) scale 1 |
| Preferred formation | `Nprf` | [0x108, 0xB0] | 0x74 | u8 | u8 0..68 (11 distinct) |
| Pressing | `Nprs` | [0x108] | 0x891 | u8 | u8 0..18 (19 distinct) scale 1 |
| Reputation | `Nrep` | [0x108, 0x18, 0x0] | 0xA8 | i16 | i16 1..9750 (124 distinct) |
| Short name | `Nsnm` |  | 0x20 | fmString | str e.g. 'Argelia' (251/251 set) |
| State of development | `Nsod` |  | 0x1EC | u8 | u8 1..3 (3 distinct) |
| Second preferred formation | `Nspf` | [0x108, 0xB0] | 0x75 | u8 | u8 0..63 (10 distinct) |
| Sitting back | `Nstb` | [0x108] | 0x892 | u8 | u8 0..20 (17 distinct) scale 1 |
| Tempo | `Ntpo` | [0x108] | 0x893 | u8 | u8 0..20 (19 distinct) scale 1 |
| Allow multiple assistant managers | `Numa` | [0x108] | 0x87E | bool | bool 60/251 set |
| Use of playmaker | `Nupm` | [0x108] | 0x894 | u8 | u8 0..20 (18 distinct) scale 1 |
| Width | `Nwid` | [0x108] | 0x895 | u8 | u8 0..17 (17 distinct) scale 1 |
| Nickname | `TCnn` | [0x108, 0xB0] | 0x90 | fmString | str e.g. 'Les Fennecs' (202/251 set) |

### Stadium

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x40 | fmString | str e.g. 'Goldsands Stadium' (400/400 set) |
| Date of current ownership | `Doco` |  | 0x38 | fmDate | date e.g. day 115 of 2025 |
| Build date | `Sbld` |  | 0x30 | fmDate | date e.g. day 365 of 1910 |
| Capacity for All-Seater competitions | `Scas` |  | 0x88 | i32 | i32 0..83186 (35 distinct) |
| Continental name | `Scnm` |  | 0x48 | fmString | str e.g. 'Bournemouth Stadium' (6/400 set) |
| Capacity | `Scpy` |  | 0x7C | i32 | i32 100..100000 (109 distinct) |
| City | `Scti` |  | 0x60 | entityPtr | -> City (400/400 set) |
| Stadium decay | `Sdcy` |  | 0xAA | u8 | u8 1..20 (5 distinct) scale 1 |
| Expansion capacity | `Secy` |  | 0x8C | i32 | i32 250..125000 (77 distinct) |
| Environment | `Senv` |  | 0xB0 | u8 | u8 0..4 (5 distinct) |
| Pitch length for international matches | `SlIM` |  | 0x9C | i16 | i16 -10..1097 (9 distinct) |
| Latitude | `Slat` |  | 0x70 | f32 | f32 -39.5..65.7 |
| Longitude | `Slon` |  | 0x74 | f32 | f32 -121..177 |
| Minimum pitch length | `Smnl` |  | 0xA0 | i16 | i16 900..900 (1 distinct) |
| Minimum pitch width | `Smnw` |  | 0xA2 | i16 | i16 550..550 (1 distinct) |
| Maximum pitch length | `Smxl` |  | 0xA4 | i16 | i16 990..1200 (12 distinct) |
| Maximum pitch width | `Smxw` |  | 0xA6 | i16 | i16 580..900 (12 distinct) |
| Nearby stadium | `Snbs` |  | 0x68 | entityPtr | -> Stadium (45/400 set) |
| Nation | `Snti` | [0x60] | 0x20 | entityPtr | -> Nation (400/400 set) |
| Owner | `Sowi` |  | 0x90 | entityPtr | -> Club (104/400 set) |
| Pitch relay required date | `Sprl` |  | 0x2C | fmDate | date e.g. day 1 of 1900 |
| Pitch recovery rate | `Sprr` |  | 0xAE | u8 | u8 0..20 (17 distinct) scale 1 |
| Pitch condition | `Sptc` |  | 0xA8 | i16 | i16 29..200 (7 distinct) |
| Pitch type | `Spty` |  | 0xAD | u8 | u8 0..8 (8 distinct) |
| Pitch width | `Spwd` |  | 0x9A | i16 | i16 580..750 (19 distinct) |
| Re-Build date | `Srbd` |  | 0x34 | fmDate | date e.g. day 182 of 2013 |
| Seat colour (ARGB) | `Sscl` |  | 0x78 | i32 | i32 -16777216..0 (93 distinct) |
| Seating capacity | `Sscy` |  | 0x80 | i32 | i32 0..85000 (92 distinct) |
| State | `Sste` |  | 0xAF | u8 | u8 1..5 (5 distinct) |
| Used by national U19 team | `Su19` |  | 0xB3 | u8 | u8 0..4 (5 distinct) |
| Used by national U21 team | `Su21` |  | 0xB2 | u8 | u8 0..5 (4 distinct) |
| Used capacity | `Sucy` |  | 0x84 | i32 | i32 0..100000 (42 distinct) |
| Used by national team | `Sunt` |  | 0xB1 | u8 | u8 0..4 (5 distinct) |
| Pitch width for international matches | `SwIM` |  | 0x9E | i16 | i16 -10..690 (7 distinct) |

### City

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x18 | fmString | str e.g. 'Gante' (400/400 set) |
| Altitude | `Calt` |  | 0x50 | i16 | i16 0..7871 (193 distinct) |
| Attraction | `Catt` |  | 0x54 | u8 | u8 0..20 (20 distinct) scale 1 |
| Inhabitants range | `Cinh` |  | 0x52 | u8 | u8 0..12 (13 distinct) |
| Latitude | `Clat` |  | 0x48 | f32 | f32 -39..71 |
| Language | `Clgi` |  | 0x30 | entityPtr | -> Language (143/400 set) |
| Longitude | `Clon` |  | 0x4C | f32 | f32 -171..178 |
| Local region | `Clri` |  | 0x38 | entityPtr | -> LocalRegion (370/400 set) |
| Nation | `Cnti` |  | 0x20 | entityPtr | -> Nation (400/400 set) |
| Weather | `Cwea` |  | 0x28 | entityPtr | -> Weather (400/400 set) |

### Team

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Club |  |  | 0x30 | entityPtr | -> Club (400/400 set) |
| Captain | `Tcap` |  | 0x88 | entityPtr | -> Player (5/400 set) |
| Division | `Tdvi` |  | 0x50 | entityPtr | -> Competition (201/400 set) |
| Midweek home match day | `Tmhm` | [0xA0] | 0x116 | u8 | u8 0..5 (4 distinct) |
| Other division | `Todo` |  | 0x68 | entityPtr | -> Competition (4/400 set) |
| Reputation | `Trep` |  | 0xA8 | i16 | i16 1..6650 (163 distinct) |
| Stadium | `Tsti` |  | 0x78 | entityPtr | -> Stadium (174/400 set) |
| Vice-Captain | `Tvcp` |  | 0x90 | entityPtr | -> Player (5/400 set) |
| Weekend home match day | `Twhm` | [0xA0] | 0x115 | u8 | u8 0..7 (4 distinct) |

### Continent

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x18 | fmString | str e.g. 'Asia' (7/7 set) |
| Three letter name | `C3ln` |  | 0x20 | fmString | str e.g. 'ASI' (6/7 set) |
| Background colour (ARGB) | `CTbc` |  | 0x4C | i32 | i32 -14671736..-7831520 (7 distinct) |
| Foreground colour (ARGB) | `CTfc` |  | 0x48 | i32 | i32 -2039744..-460552 (4 distinct) |
| Trim colour (ARGB) | `CTtc` |  | 0x50 | i32 | i32 -14671736..-7831520 (7 distinct) |
| Federation name | `Cfdn` |  | 0x30 | fmString | str e.g. 'Confederación Asiática de Fútbol' (7/7 set) |
| Regional strength | `Crgs` |  | 0xE4 | f32 | f32 0..1 |
| Short federation name | `Csfn` |  | 0x40 | fmString | str e.g. 'AFC' (7/7 set) |

### LocalRegion

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x18 | fmString | str e.g. 'Sureste' (400/400 set) |
| Nation | `Lnat` |  | 0x48 | entityPtr | -> Nation (400/400 set) |
| Population | `Lpop` |  | 0x58 | i32 | i32 0..21411923 (299 distinct) |
| Weather | `Lwea` |  | 0x50 | entityPtr | -> Weather (75/400 set) |

### Award

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x30 | fmString | str e.g. 'FIFA Balon de Oro' (400/400 set) |
| Announcement date | `AWad` |  | 0x4C | fmDate | date e.g. day 1 of 1900 |
| Allow previous winner | `AWap` |  | 0x8C | u8 | u8 0..3 (4 distinct) |
| Background colour (ARGB) | `AWbc` |  | 0x1C | i32 | i32 -16777216..-460552 (36 distinct) |
| Based | `AWbr` |  | 0x87 | u8 | u8 1..100 (9 distinct) |
| Continent | `AWci` |  | 0x50 | entityPtr | -> Continent (394/400 set) |
| Award date | `AWda` |  | 0x48 | fmDate | date e.g. day 299 of 2026 |
| Award period | `AWdr` |  | 0x86 | u8 | u8 1..9 (8 distinct) |
| Foreground colour (ARGB) | `AWfc` |  | 0x18 | i32 | i32 -16777216..-460552 (24 distinct) |
| Formation | `AWfo` |  | 0x92 | u8 | u8 0..64 (7 distinct) |
| Minimum age | `AWma` |  | 0x94 | u8 | u8 0..18 (4 distinct) |
| Minimum percentage of games played | `AWmg` |  | 0x97 | u8 | u8 0..100 (15 distinct) |
| Competition | `AWmi` |  | 0x58 | entityPtr | -> Competition (370/400 set) |
| Nation | `AWni` |  | 0x60 | entityPtr | -> Nation (363/400 set) |
| Number of placings | `AWnp` |  | 0x8F | u8 | u8 0..3 (4 distinct) |
| Position | `AWpo` |  | 0x7C | i32 | i32 0..16384 (6 distinct) |
| Run by | `AWrb` |  | 0x89 | u8 | u8 0..3 (4 distinct) |
| Reputation | `AWre` |  | 0x80 | i16 | i16 0..20 (20 distinct) scale 1 |
| Local region | `AWrg` |  | 0x68 | entityPtr | -> LocalRegion (1/400 set) |
| Award recipient type | `AWrt` |  | 0x8D | u8 | u8 1..8 (6 distinct) |
| Voting | `AWsc` |  | 0x84 | u8 | u8 0..7 (6 distinct) |
| Short name | `AWsn` |  | 0x38 | fmString | str e.g. 'FIFA Balon de Oro' (400/400 set) |
| Side | `AWsr` |  | 0x88 | u8 | u8 0..0 (1 distinct) |
| Use stats from | `AWss` |  | 0x8B | u8 | u8 0..7 (8 distinct) |
| Trim colour (ARGB) | `AWtc` |  | 0x20 | i32 | i32 -16777216..-460552 (25 distinct) |
| Voting format | `AWvf` |  | 0x8E | u8 | u8 0..3 (4 distinct) |
| Winner home reputation | `AWwh` |  | 0x95 | u8 | u8 0..200 (42 distinct) |
| Winner world reputation | `AWwr` |  | 0x96 | u8 | u8 0..200 (38 distinct) |
| Maximum age | `AWxa` |  | 0x93 | u8 | u8 0..24 (8 distinct) |

### Injury

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x18 | fmString | str e.g. 'resfriado' (136/137 set) |
| Extra days (men) | `Iexd` |  | 0x3A | i16 | i16 1..290 (35 distinct) |
| Minimum days (men) | `Imnd` |  | 0x38 | i16 | i16 1..240 (33 distinct) |
| Occurrence ratio | `Iocr` |  | 0x30 | f32 | f32 0.0001..9 |
| Sentence name | `Isnm` |  | 0x20 | fmString | str e.g. 'un resfriado' (136/137 set) |
| Extra days (women) | `Wexd` |  | 0x4E | i16 | i16 1..290 (35 distinct) |
| Minimum days (women) | `Wmnd` |  | 0x4C | i16 | i16 1..240 (31 distinct) |
| Seriousness | `inSr` |  | 0x6C | u8 | u8 1..82 (6 distinct) |
| Sub-Classification | `iscl` |  | 0x65 | u8 | u8 0..48 (46 distinct) |

### Language

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x18 | fmString | str e.g. 'Albanés' (126/126 set) |
| Language difficulty | `Ldff` |  | 0x3C | u8 | u8 0..20 (17 distinct) scale 1 |
| Language family | `Lfam` |  | 0x34 | i16 | i16 0..109 (20 distinct) |
| Language influence | `Linf` |  | 0x3B | u8 | u8 0..20 (16 distinct) scale 1 |
| Nation | `Lnat` |  | 0x28 | entityPtr | -> Nation (121/126 set) |

### MediaSource

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x18 | fmString | str e.g. 'Liverpool Echo' (369/369 set) |
| Attends press conferences | `Mapc` |  | 0xC3 | bool | bool 337/369 set |
| Continent | `Mcti` |  | 0xB8 | entityPtr | -> Continent (279/369 set) |
| City | `Mcyi` |  | 0xB0 | entityPtr | -> City (245/369 set) |
| Is fake | `Misf` |  | 0xC2 | bool | bool 189/369 set |
| Period | `Mper` |  | 0xE0 | u8 | u8 0..14 (7 distinct) |
| Scope | `Mscp` |  | 0xE1 | u8 | u8 0..7 (8 distinct) |
| Style | `Msty` |  | 0xC0 | u8 | u8 0..6 (7 distinct) |

### Derby

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x18 | fmString | str e.g. 'de Merseyside' (400/400 set) |
| Alternative name | `Danm` |  | 0x30 | fmString | str e.g. 'Old Farm' (9/400 set) |
| Alternative short name | `Dasn` |  | 0x28 | fmString | str e.g. 'Old Farm' (9/400 set) |
| National reputation | `Dntr` |  | 0x5D | u8 | u8 0..200 (47 distinct) |
| Short name | `Dsnm` |  | 0x20 | fmString | str e.g. 'de Merseyside' (400/400 set) |
| Team 1 | `Dtm1` |  | 0x40 | entityPtr | -> Team (340/400 set) |
| Team 2 | `Dtm2` |  | 0x48 | entityPtr | -> Team (340/400 set) |
| World reputation | `Dwdr` |  | 0x5C | u8 | u8 0..200 (39 distinct) |

### StageName

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x18 | fmString | str e.g. 'Repetición' (288/288 set) |
| Plural name | `SNpn` |  | 0x28 | fmString | str e.g. 'Repetición' (287/288 set) |
| Short name | `SNsn` |  | 0x20 | fmString | str e.g. 'Replay' (288/288 set) |

### Weather

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x18 | fmString | str e.g. 'English Coastal' (398/398 set) |
| Period 1: Dry | `w0p0` |  | 0x27 | u8 | u8 0..100 (71 distinct) |
| Period 1: Wet | `w0p1` |  | 0x28 | u8 | u8 0..57 (48 distinct) |
| Period 1: Drizzle | `w0p2` |  | 0x29 | u8 | u8 0..55 (31 distinct) |
| Period 1: Shower | `w0p3` |  | 0x2A | u8 | u8 0..40 (21 distinct) |
| Period 1: Down pour | `w0p4` |  | 0x2B | u8 | u8 0..16 (14 distinct) |
| Period 1: Start day | `w0sd` |  | 0x20 | i16 | i16 0..335 (50 distinct) |
| Period 1: Freezing (-7 to 0 celsius) | `w0t0` |  | 0x2E | u8 | u8 0..39 (20 distinct) |
| Period 1: Cold (1 to 6 celsius) | `w0t1` |  | 0x2F | u8 | u8 0..80 (48 distinct) |
| Period 1: Mild (7 to 13 celsius) | `w0t2` |  | 0x30 | u8 | u8 0..100 (65 distinct) |
| Period 1: Fine (14 to 21 celsius) | `w0t3` |  | 0x31 | u8 | u8 0..84 (72 distinct) |
| Period 1: Warm (22 to 28 celsius) | `w0t4` |  | 0x32 | u8 | u8 0..98 (66 distinct) |
| Period 1: Hot (29 to 34 celsius) | `w0t5` |  | 0x33 | u8 | u8 0..80 (42 distinct) |
| Period 1: Very hot (35 to 42 celsius) | `w0t6` |  | 0x34 | u8 | u8 0..9 (7 distinct) |
| Period 1: Extremely hot (43 Celsius and above) | `w0t7` |  | 0x35 | u8 | u8 0..0 (1 distinct) |
| Period 1: Extremely freezing (-16 Celsius and below) | `w0te` |  | 0x2C | u8 | u8 0..0 (1 distinct) |
| Period 1: Very freezing (-15 to -8 celsius) | `w0tv` |  | 0x2D | u8 | u8 0..20 (3 distinct) |
| Period 1: Calm | `w0w0` |  | 0x22 | u8 | u8 0..100 (80 distinct) |
| Period 1: Breezy | `w0w1` |  | 0x23 | u8 | u8 0..86 (73 distinct) |
| Period 1: Gusty | `w0w2` |  | 0x24 | u8 | u8 0..55 (51 distinct) |
| Period 1: Strong | `w0w3` |  | 0x25 | u8 | u8 0..49 (31 distinct) |
| Period 1: Gale | `w0w4` |  | 0x26 | u8 | u8 0..29 (15 distinct) |
| Period 2: Dry | `w1p0` |  | 0x3F | u8 | u8 1..100 (72 distinct) |
| Period 2: Wet | `w1p1` |  | 0x40 | u8 | u8 0..50 (44 distinct) |
| Period 2: Drizzle | `w1p2` |  | 0x41 | u8 | u8 0..50 (35 distinct) |
| Period 2: Shower | `w1p3` |  | 0x42 | u8 | u8 0..34 (25 distinct) |
| Period 2: Down pour | `w1p4` |  | 0x43 | u8 | u8 0..16 (13 distinct) |
| Period 2: Start day | `w1sd` |  | 0x38 | i16 | i16 11..356 (48 distinct) |
| Period 2: Freezing (-7 to 0 celsius) | `w1t0` |  | 0x46 | u8 | u8 0..1 (2 distinct) |
| Period 2: Cold (1 to 6 celsius) | `w1t1` |  | 0x47 | u8 | u8 0..15 (13 distinct) |
| Period 2: Mild (7 to 13 celsius) | `w1t2` |  | 0x48 | u8 | u8 0..78 (43 distinct) |
| Period 2: Fine (14 to 21 celsius) | `w1t3` |  | 0x49 | u8 | u8 0..85 (73 distinct) |
| Period 2: Warm (22 to 28 celsius) | `w1t4` |  | 0x4A | u8 | u8 0..95 (81 distinct) |
| Period 2: Hot (29 to 34 celsius) | `w1t5` |  | 0x4B | u8 | u8 0..89 (70 distinct) |
| Period 2: Very hot (35 to 42 celsius) | `w1t6` |  | 0x4C | u8 | u8 0..55 (13 distinct) |
| Period 2: Extremely hot (43 Celsius and above) | `w1t7` |  | 0x4D | u8 | u8 0..0 (1 distinct) |
| Period 2: Extremely freezing (-16 Celsius and below) | `w1te` |  | 0x44 | u8 | u8 0..0 (1 distinct) |
| Period 2: Very freezing (-15 to -8 celsius) | `w1tv` |  | 0x45 | u8 | u8 0..0 (1 distinct) |
| Period 2: Calm | `w1w0` |  | 0x3A | u8 | u8 1..100 (85 distinct) |
| Period 2: Breezy | `w1w1` |  | 0x3B | u8 | u8 0..84 (69 distinct) |
| Period 2: Gusty | `w1w2` |  | 0x3C | u8 | u8 0..71 (52 distinct) |
| Period 2: Strong | `w1w3` |  | 0x3D | u8 | u8 0..60 (28 distinct) |
| Period 2: Gale | `w1w4` |  | 0x3E | u8 | u8 0..20 (15 distinct) |
| Period 3: Dry | `w2p0` |  | 0x57 | u8 | u8 3..100 (77 distinct) |
| Period 3: Wet | `w2p1` |  | 0x58 | u8 | u8 0..52 (49 distinct) |
| Period 3: Drizzle | `w2p2` |  | 0x59 | u8 | u8 0..45 (35 distinct) |
| Period 3: Shower | `w2p3` |  | 0x5A | u8 | u8 0..50 (25 distinct) |
| Period 3: Down pour | `w2p4` |  | 0x5B | u8 | u8 0..20 (14 distinct) |
| Period 3: Start day | `w2sd` |  | 0x50 | i16 | i16 59..304 (44 distinct) |
| Period 3: Freezing (-7 to 0 celsius) | `w2t0` |  | 0x5E | u8 | u8 0..30 (20 distinct) |
| Period 3: Cold (1 to 6 celsius) | `w2t1` |  | 0x5F | u8 | u8 0..50 (45 distinct) |
| Period 3: Mild (7 to 13 celsius) | `w2t2` |  | 0x60 | u8 | u8 0..100 (65 distinct) |
| Period 3: Fine (14 to 21 celsius) | `w2t3` |  | 0x61 | u8 | u8 0..91 (72 distinct) |
| Period 3: Warm (22 to 28 celsius) | `w2t4` |  | 0x62 | u8 | u8 0..85 (52 distinct) |
| Period 3: Hot (29 to 34 celsius) | `w2t5` |  | 0x63 | u8 | u8 0..90 (47 distinct) |
| Period 3: Very hot (35 to 42 celsius) | `w2t6` |  | 0x64 | u8 | u8 0..15 (7 distinct) |
| Period 3: Extremely hot (43 Celsius and above) | `w2t7` |  | 0x65 | u8 | u8 0..0 (1 distinct) |
| Period 3: Extremely freezing (-16 Celsius and below) | `w2te` |  | 0x5C | u8 | u8 0..1 (2 distinct) |
| Period 3: Very freezing (-15 to -8 celsius) | `w2tv` |  | 0x5D | u8 | u8 0..4 (4 distinct) |
| Period 3: Calm | `w2w0` |  | 0x52 | u8 | u8 2..100 (77 distinct) |
| Period 3: Breezy | `w2w1` |  | 0x53 | u8 | u8 0..84 (74 distinct) |
| Period 3: Gusty | `w2w2` |  | 0x54 | u8 | u8 0..50 (45 distinct) |
| Period 3: Strong | `w2w3` |  | 0x55 | u8 | u8 0..35 (25 distinct) |
| Period 3: Gale | `w2w4` |  | 0x56 | u8 | u8 0..24 (17 distinct) |
| Period 4: Dry | `w3p0` |  | 0x6F | u8 | u8 1..100 (81 distinct) |
| Period 4: Wet | `w3p1` |  | 0x70 | u8 | u8 0..58 (52 distinct) |
| Period 4: Drizzle | `w3p2` |  | 0x71 | u8 | u8 0..42 (36 distinct) |
| Period 4: Shower | `w3p3` |  | 0x72 | u8 | u8 0..35 (23 distinct) |
| Period 4: Down pour | `w3p4` |  | 0x73 | u8 | u8 0..20 (15 distinct) |
| Period 4: Start day | `w3sd` |  | 0x68 | i16 | i16 0..359 (42 distinct) |
| Period 4: Freezing (-7 to 0 celsius) | `w3t0` |  | 0x76 | u8 | u8 0..100 (69 distinct) |
| Period 4: Cold (1 to 6 celsius) | `w3t1` |  | 0x77 | u8 | u8 0..95 (66 distinct) |
| Period 4: Mild (7 to 13 celsius) | `w3t2` |  | 0x78 | u8 | u8 0..95 (68 distinct) |
| Period 4: Fine (14 to 21 celsius) | `w3t3` |  | 0x79 | u8 | u8 0..87 (52 distinct) |
| Period 4: Warm (22 to 28 celsius) | `w3t4` |  | 0x7A | u8 | u8 0..95 (44 distinct) |
| Period 4: Hot (29 to 34 celsius) | `w3t5` |  | 0x7B | u8 | u8 0..85 (35 distinct) |
| Period 4: Very hot (35 to 42 celsius) | `w3t6` |  | 0x7C | u8 | u8 0..4 (5 distinct) |
| Period 4: Extremely hot (43 Celsius and above) | `w3t7` |  | 0x7D | u8 | u8 0..0 (1 distinct) |
| Period 4: Extremely freezing (-16 Celsius and below) | `w3te` |  | 0x74 | u8 | u8 0..90 (12 distinct) |
| Period 4: Very freezing (-15 to -8 celsius) | `w3tv` |  | 0x75 | u8 | u8 0..57 (23 distinct) |
| Period 4: Calm | `w3w0` |  | 0x6A | u8 | u8 1..100 (78 distinct) |
| Period 4: Breezy | `w3w1` |  | 0x6B | u8 | u8 0..87 (68 distinct) |
| Period 4: Gusty | `w3w2` |  | 0x6C | u8 | u8 0..55 (46 distinct) |
| Period 4: Strong | `w3w3` |  | 0x6D | u8 | u8 0..40 (33 distinct) |
| Period 4: Gale | `w3w4` |  | 0x6E | u8 | u8 0..18 (18 distinct) |

### Currency

| Field | Code | Path | Offset | Type | Live check |
|---|---|---|---|---|---|
| Name |  |  | 0x18 | fmString | str e.g. 'Albanian Lek' (173/173 set) |
| Short name |  |  | 0x20 | fmString | str e.g. 'Lek' (173/173 set) |
| Symbol |  |  | 0x30 | fmString | str e.g. '€' (117/173 set) |
| Exchange rate |  |  | 0x38 | f32 | units per pound: pound 1.0, euro 1.164, US dollar 1.348 |

Every amount in the game is kept in pounds. `+0x40` points at a list of dated past rates (8 bytes each: date, then f32 rate).

---

## Missing

### Player

| Field | What's known / next step |
|---|---|
| Second nationality | Not a single pointer. Person+0x78 is a vector of mixed relation entries (nations, clubs, people); each entry holds a pointer at +0x0 and type bytes at +0xA and +0xB. The getter picks entries of one type. The editor's definition format has no way to say "the entry of type N" yet. |
| Weight | FM27 has no weight field. Body type (person−0x58, five named shapes) replaced it. |
| Trained at club / nation | Lists (`Phgc`, `Phgn`), computed from the playing history. |
| Banned | A list (`Pbnl`), not a flag. |
| Retired from international football | The international data block at [−0x168]: date at +0x28, flag byte at +0x34. The flag's bit is not known. |
| Squad status | Contract +0xC4. Most contracts hold 255 and a few hold 23–26, so the shown status is mostly computed. Left out. |
| Release clauses, bonuses, contract happiness, future transfer | Lists of clause records on the contract; not traced. |

### Staff

| Field | What's known / next step |
|---|---|
| GK Distribution / Handling / Shot Stopping, Mental | FM27 has no such fields. It has one Goalkeeping attribute (−0xD5), plus Possession and Set Pieces. The catalog entries stay unmapped. |
| Jobs 80 and 86 | Seen on 94 and 62 staff; not identified. |
| Human manager CA/PA | `HUMAN_NON_PLAYER` uses a different layout, so −0x26/−0x24 read garbage for it. |

### Club

| Field | What's known / next step |
|---|---|
| Maximum wage | Computed on request (`CMWE`), not stored. |
| Kit colours | A list of kit records at [0x118]; each kit is its own record (`ktfl` in the schema). Not traced. |
| League standing | Lives in the 267 `sicomps::LEAGUE_STAGE` objects, one row per team. Reading it needs a lookup by team, which the definition format cannot express yet. |
| Team cohesion | Not among the field codes. |

### Competition and nation

| Field | What's known / next step |
|---|---|
| Prize money | Not a database field. It is paid by per-competition rule code (`sicomps::*_MONEY_HELPER`). |
| Nation rules (EU free movement, work permits, non-EU limit) | Not database fields either: they are competition rules in `sicomps`. The nation record only holds "Nations treated as non-foreign" (a list) and a dual-nationality flag word at +0x1E8. |

### Award

| Field | What's known / next step |
|---|---|
| Past winners | Not on the award record, and nothing in memory points back at an award except the award lists and its `YEAR_AWARD_DATA` schedule entry (award pointer at +0x8, dates from +0x18). `db::STARTING_AWARD_HISTORY` has no live objects: it is converted when the save starts (`CONVERT_STARTING_AWARD_HISTORY`). Where the converted history lives is not found. |
| Award date / announcement date | The *next* time the award is given, not a history. Announcement date is 1 Jan 1900 (empty) for most awards. |

### Record types not in the app yet

Officials (referees), agents and journalists are people with their own small tables. Their handlers are analysed in `tools/research`, but only a handful of fields resolve. Regions (`db::REGION`, 28 records) have no handler that resolves a name. Contracts, loans, kits and stadium changes are sub-records reached from their owner, not lists of their own.

### Fields the analyser found but dropped

About 300 named codes have a handler but no accepted location: lists, formatted text, values computed on the fly, flags that share a byte without a known bit, and locations that failed the live check. `tools/research/gen.py` writes the reason for each into `gen.json`.
