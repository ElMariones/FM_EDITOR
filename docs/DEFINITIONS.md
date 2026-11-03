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

### Field types

| type | meaning |
|---|---|
| `u8` `i8` `u16` `i16` `i32` `u32` `i64` `f32` `f64` | numbers |
| `bool` | one byte, 0 or 1 |
| `fmDate` | u16 day of year (0-based) + u16 year |
| `utf16Ptr` / `utf8Ptr` | pointer to a null-terminated string. In a live game, names can be shortened, not lengthened. |
| `utf16Inline` | characters stored inside the record (`maxLength`) |
| `entityPtr` | pointer to another record (club, nation…); the editor shows the name |

`scale` converts the stored value to the one shown: shown = stored ÷ scale. FM has historically stored 1–20 attributes as 1–100 (scale 5). **Confirm this per field.** Mark a field `"verified": true` once you've tested it in-game.

Property keys (`Attr.Finishing`, `Finance.Balance`…) come from [`PropertyCatalog.cs`](../src/FMOpenEditor.Core/Model/PropertyCatalog.cs). [`definitions/_template.json`](../definitions/_template.json) lists them all. A test fails if a definition uses an unknown key.

## Mapping workflow

You need FM27 running with a save loaded, and FM OpenEditor connected (**Connect to Football Manager**). Open **Research**.

### 1. Find a value

Pick something you can see and change in the game. Your club's balance is a good start.

1. In Research, type the balance (exact number, e.g. `12500000`), pick the type (`Int64` for money, `Int32` otherwise; try both), and press **First scan**.
2. Change the value in-game (sell a player, pay a fee…), type the new number, choose **Exact**, and press **Next scan**. Repeat until only a few addresses remain.
3. Confirm by writing a new value to the address and checking the game shows it.

### 2. Find the record base

The address you found is *inside* a record. To turn it into an offset you need the start of that record. A memory viewer or debugger such as [x64dbg](https://x64dbg.com) or [ReClass.NET](https://github.com/ReClassNET/ReClass.NET) helps here: look for the pointer that points a little before your address, and for the club's name and id nearby. Find two or three fields of the same record (balance, transfer budget, reputation) and the layout becomes clear.

The **Offset calculator** in Research subtracts the base from the field address.

### 3. Find a root (the hard part)

To list every club, you need a stable path from `game_plugin.dll` to the club table:

1. Find what points at your record: scan for the record's address as an `Int64`. Repeat up the chain until you reach an address inside `game_plugin.dll`'s range (shown in Research).
2. Disassemble around code that reads that global (x64dbg: find references to the address) and copy a byte pattern for the instruction, replacing the displacement and other volatile bytes with `??`. Make it long enough to be unique.
3. Put it in `roots`, add the `deref` hops, then describe the table in `collections`.

Reconnect. If the club list fills, it worked. Warnings on the Home page tell you which root or collection failed.

### 4. Share it

Open a pull request with your changes to the definition file. Say what you verified and how, e.g. "Club finances verified on 27.0.0+2493426: edited the balance, it showed in the game and persisted after saving."

## Ground rules

- **Only contribute what you found yourself.** Don't copy offsets, signatures or structures from closed-source tools or their data files. That's a licence problem for the whole project.
- Fill fields in gradually. A partly mapped definition is useful: unmapped fields are simply hidden.
- Never mark something `verified` that you didn't test in-game.
