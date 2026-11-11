# Game definitions: mapping a game version

A **definition** tells FM OpenEditor where Football Manager keeps its data in memory for one game build. Definitions are JSON files in [`definitions/`](../definitions). The app picks the one whose `moduleVersions` matches the running game's `game_plugin.dll` product version (shown on the Home page when you connect). If none matches exactly, it falls back to the newest definition with the same major version.

No code is needed to support a new patch, or to make a new field editable. Only numbers.

## Where the data lives

FM27 is a Unity game: the UI runs as IL2CPP in `GameAssembly.dll`. The football database (people, clubs, competitions, nations) lives in the native C++ simulation engine, **`fm_Data/Plugins/x86_64/game_plugin.dll`**, and in the heap memory it allocates.

A definition has three layers:

1. **Roots**: global pointers inside `game_plugin.dll` that lead to the big tables (e.g. "every person").
2. **Collections**: how to walk a table to get each record's address.
3. **Structs**: for each entity kind, the offset of each field from the record's base address.

## File format

```jsonc
{
  "game": "Football Manager 2027",
  "version": "27.0.0",
  "module": "game_plugin.dll",
  "moduleVersions": ["27.0.0+2493426"],   // product version(s) of game_plugin.dll
  "verified": false,                     // true once someone has tested it in-game

  "roots": {
    "People": {
      // Preferred: a byte signature of an instruction that loads the global,
      // e.g. "mov rax, [rip+disp32]". ?? = wildcard. Survives most patches.
      "pattern": "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ??",
      "dispOffset": 3,            // where the 32-bit displacement starts in the match
      "instructionLength": 7,     // RIP-relative = relative to the end of the instruction
      // Alternative to a pattern: a fixed offset from the module base (breaks every patch).
      "moduleOffset": null,
      // Then follow pointers: read pointer at (address + offset), for each entry.
      "deref": ["0x0"]
    }
  },

  "collections": {
    "Player": {
      "root": "People",
      "layout": "pointerRange",   // pointerRange | pointerArray | inlineArray
      "beginOffset": "0x0",       // pointerRange: begin/end pointers (std::vector style)
      "endOffset": "0x8",
      "countOffset": null,        // pointerArray / inlineArray: int32 count
      "stride": 0,                // inlineArray: record size
      // Players and staff often share one table; keep records where this field == filterValue.
      "filter": { "offset": "0x4", "type": "u8" },
      "filterValue": 1
    }
  },

  "structs": {
    "Player": {
      "Id":             { "offset": "0x0",  "type": "i32" },
      "FirstName":      { "offset": "0x8",  "type": "utf16Ptr" },
      "DateOfBirth":    { "offset": "0x18", "type": "fmDate" },
      "Attr.Finishing": { "offset": "0x40", "type": "u8", "scale": 5 },
      // "path" follows pointers from the record before applying "offset":
      "Contract.Club":  { "path": ["0x50"], "offset": "0x0", "type": "entityPtr" },
      "Contract.Wage":  { "path": ["0x50"], "offset": "0x8", "type": "i64" }
    }
  }
}
```

### Collections found by RTTI (no root needed)

Instead of a root, a collection can name C++ classes. At connect, the editor finds their vtables through RTTI and scans the heap once for objects that point at them:

```jsonc
"Player": {
  "layout": "rttiInstances",
  "classes": [
    // vtableOffset picks which of the class's vtables to look for. The record base is where it sits,
    // so pointing every class at the vtable of a shared base part (db::PERSON) gives one set of offsets.
    { "class": "db::ACTUAL_PLAYER", "vtableOffset": "0x288" },
    { "class": "db::ACTUAL_PLAYER_AND_NON_PLAYER", "vtableOffset": "0x380" }
  ],
  // Optional: freed memory can still hold old objects; a filter (e.g. a flag or id that must match) drops them.
  "filter": { "offset": "0x...", "type": "u8" }, "filterValue": 1
}
```

Class names survive patches; only `vtableOffset` and struct offsets may move. The Research page's **Classes (RTTI)** tab lists classes, their vtables and object offsets. To read the same RTTI from the DLL on disk without the game running, use `PeFileMemory` with `RttiScanner`.

### Field types

| type | meaning |
|---|---|
| `u8` `i8` `u16` `i16` `i32` `u32` `i64` `f32` `f64` | numbers |
| `bool` | one byte, 0 or 1 |
| `fmDate` | u16 day of year + u16 year. The day counts from 0 unless the definition sets `"dateDayBase": 1` (FM27 does; there, day 1 of 1900 is the empty date). |
| `utf16Ptr` / `utf8Ptr` | pointer to a null-terminated string. In a live game, names can be shortened, not lengthened. |
| `utf16Inline` | characters stored inside the record (`maxLength`) |
| `fmString` | pointer to FM's length-prefixed string: i32 byte count, UTF-8 bytes, 0. FM27 uses it for every name. Edited in place, never longer than the current text. |
| `entityPtr` | pointer to another record (club, nation…); the editor shows the name |

A `path` whose pointer is empty (no common name, no contract) makes the field read as empty; it can't be edited for that record.

`"mask": "0x10"` on a `bool` says the flag is one bit of a shared byte. Reading tests that bit, and writing changes only that bit.

`"values": [0, 2, 4, 16]` on a choice field lists the number the game stores for each choice, in the order of the property's choice list. Use it when the game's numbers are not 0, 1, 2… (staff jobs, for example). A stored number that is not in the list reads as empty.

`"readOnly": true` shows a field (in lists, filters and related tabs) but never writes it. Use it for links the game also keeps somewhere else, such as a club's league, which the competition's own team list repeats: changing only one copy would leave the save inconsistent.

`scale` converts the stored value to the one shown: shown = stored ÷ scale. FM has historically stored 1–20 attributes as 1–100 (scale 5). **Confirm this per field.** Mark a field `"verified": true` once you've tested it in-game.

Property keys (`Attr.Finishing`, `Finance.Balance`…) come from [`PropertyCatalog.cs`](../src/FMOpenEditor.Core/Model/PropertyCatalog.cs). Properties found by the scripts in [`tools/research`](../tools/research) are listed in [`catalog.generated.json`](../src/FMOpenEditor.Core/Model/catalog.generated.json), which is built into the app and extends that catalog.

Record kinds are `Player`, `Staff`, `Club`, `Competition`, `Nation`, and, since FM27, `Stadium`, `City`, `Team`, `Continent`, `LocalRegion`, `Award`, `Injury`, `Language`, `MediaSource`, `Derby`, `StageName`, `Weather` and `Currency`. A kind appears in the sidebar when the definition gives it a collection with records, except `Team`: a club's page carries its first team's fields, and a link to a team opens its club. [`definitions/_template.json`](../definitions/_template.json) lists them all. A test fails if a definition uses an unknown key.

## Mapping workflow

You need FM27 running with a save loaded, and FM OpenEditor connected (**Connect to Football Manager**). Open **Research**. Its tabs:

| Tab | What it's for |
|---|---|
| **Value scan** | Classic scan, change in game, scan again (Byte, Int16/32/64, Float, Double), plus the offset calculator. |
| **Strings & pointers** | Find a name as UTF-16/UTF-8; "what points here?" with an optional max offset. Pointers that live inside `game_plugin.dll` are starred: root candidates. |
| **Classes (RTTI)** | Lists the module's C++ classes from MSVC run-time type information with their vtables, and counts each class's objects in the heap. |
| **Record diff** | Snapshot a record, change one thing in game, snapshot again: changed bytes with their readings (u8/i16/i32/i64/f32/f64/fmDate). Also lists the record's pointers (vtable, name strings, sub-records) and follows them. |
| **Correlator** | Give it several records and the value the game shows for each; it finds the offset, type and scale that explain all of them, as a JSON snippet. |

Addresses flow between tabs: open an RTTI object or a search hit as a record, add objects to the correlator, or search pointers to anything. Addresses can be typed as hex (`0x1F2A3B40`) or as `game_plugin.dll+0x1234`.

### 1. Find one record

Try these in order:

1. **Classes (RTTI).** Press **Find classes**, filter by `person`, `player`, `staff`, `club`, `nation`, `competition` and **Count objects (listed)**. A class with about as many objects as the game has people (tens of thousands or more) is a strong lead. Count it on its own to list its objects: each address is the base of one record. If the module has no RTTI, or the data classes aren't polymorphic, use one of the next two.
2. **A name.** In **Strings & pointers**, search for an unusual player's name, then find pointers to the hit. One is usually a name field inside the record. FM has also stored names as indices into a name table, so if nothing points at the string, look near it for that table.
3. **A value.** In **Value scan**, scan for something you can see and change: your club's balance is a good start. Set the game's currency to pounds first, since displayed money is converted and rounded. Try `Int32`, `Int64` and `Double`. Confirm by writing a new value and checking the game shows it. Then use **Find pointers to selected** with a max offset such as `0x800` to reach the start of the record.

### 2. Map the fields

- **Record diff**, for one field at a time: snapshot, change exactly one thing in the game (sign a contract, injure a player, use the in-game editor), snapshot again. Snapshot once more without changing anything to rule out values that change on their own.
- **Correlator**, for attributes and anything you can read off many records: add 10 or more records whose values differ (from the RTTI object list), type the shown value next to each, and press **Find the field**. It tries every aligned offset as u8/i8/u16/i16/u32/i32/i64/f32/f64, exact (stored = shown × scale) and rounded or floored (shown = round(stored ÷ scale)). One match is very likely the field, and it tells you the `scale`.
- **Fields behind pointers** (contract, finances): in Record diff, select a heap pointer under *Pointers in this record* and **Follow** it. Fields found there go in the definition with `"path": ["<offset of the pointer>"]`.

A memory viewer such as [x64dbg](https://x64dbg.com) or [ReClass.NET](https://github.com/ReClassNET/ReClass.NET) is still handy for looking around a record.

### 3. Find a root (the hard part)

To list every record of a kind, you need a stable path from `game_plugin.dll` to the table:

1. From a record's base, use **Find pointers** (max offset `0x0`). Usually it's an entry in an array or vector. Find pointers to the start of that array (raise the max offset), and repeat until a hit is starred (inside the module). That module address is the global.
2. Disassemble around code that reads that global (Ghidra, IDA or x64dbg: find references to the address) and copy a byte pattern for the instruction, replacing the displacement and other volatile bytes with `??`. Make it long enough to be unique.
3. Put it in `roots`, add the `deref` hops, then describe the table in `collections`.

Reconnect. If the list fills, it worked. Check the count against the RTTI object count for the same class. Warnings on the Home page tell you which root or collection failed.

### 4. Share it

Open a pull request with your changes to the definition file. Say what you verified and how, e.g. "Club finances verified on 27.0.0+2493426: edited the balance, it showed in the game and persisted after saving."

## Ground rules

- **Only contribute what you found yourself.** Don't copy offsets, signatures or structures from closed-source tools or their data files. That's a licence problem for the whole project.
- Fill fields in gradually. A partly mapped definition is useful: unmapped fields are simply hidden.
- Never mark something `verified` that you didn't test in-game.
