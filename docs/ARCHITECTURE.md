# Architecture

```
FMOpenEditor.App (Avalonia UI)
        │  view models only talk to EditorSession
        ▼
EditorSession ── ChangeSet (pending edits, save, undo)
   │          ── AttributeWatcher (freeze / only-up)
   │          ── derived properties (Age, display name)
   ▼
IGameDatabase
   ├── LiveDatabase ── GameDefinition (JSON) ── IMemoryAccess ── ProcessMemory (Win32)
   └── DemoDatabase (generated fictional save)

PropertyCatalog: every property, its type, range and category
PresetEngine / CustomActions, SearchEngine: work through EditorSession
```

## Key ideas

**The property catalog is the single source of truth.** [`PropertyCatalog`](../src/FMOpenEditor.Core/Model/PropertyCatalog.cs) lists every property of every entity kind: key, label, category, value kind, range. The detail editor, the grid, Advanced Search, Mass Edit, presets, Compare and freeze are all generated from it. To add a property, add one line to the catalog and its offset to a definition.

**Game knowledge is data, not code.** [`LiveDatabase`](../src/FMOpenEditor.Core/Data/LiveDatabase.cs) knows how to follow signatures, pointers, tables and field types, but not where anything is. That's in the [definition JSON](DEFINITIONS.md). A game patch means a new or updated JSON file, and anyone can contribute one.

**Unsupported means hidden, not broken.** `IGameDatabase.Supports(kind, key)` drives what the UI shows. A partly mapped definition gives a partly featured editor. Presets skip unmapped fields and report them.

**Edits are staged.** `EditorSession.Set` coerces and clamps the value, then stages it in the `ChangeSet`. Nothing touches the game until *Save changes*, which writes every pending edit and records the batch so it can be undone. *Save instantly* commits each edit straight away.

**Freeze / only-up** is a watch list polled every 2 seconds. Freeze rewrites any change the game makes; only-up keeps increases and rewrites decreases. A pending user edit always wins over a watch.

**The demo save** (`DemoDatabase`) supports every property, so the whole UI can be developed and tested without the game. It is deterministic (seeded), so tests can rely on it.

## Values

Each `ValueKind` has one .NET representation (see [`Values`](../src/FMOpenEditor.Core/Model/Values.cs)): Integer → `int`, Money → `long`, Decimal → `double`, Boolean → `bool`, Text → `string`, Date → `DateOnly?`, Choice → `int` index, Reference → `int?` entity id. `Values.Coerce` converts anything reasonable (strings, JSON, boxed numbers) and clamps it to the property's range.

## Presets

A preset is JSON ([examples](../presets)): a target kind, optional `where` filters, a scope (`Selection` or `All`), and a list of actions. Each action sets a property (wildcards like `Attr.*` and a `category` filter are allowed) with an op (`Set`, `Add`, `Multiply`, `AtLeast`, `AtMost`), copies another property (`fromProperty`), or calls a registered custom action (`ImproveTeamCohesion`, `ReleaseOnFree`). User presets live in `%APPDATA%/FMOpenEditor/presets`.

## Testing

- `tests/FMOpenEditor.Tests`: Core logic. `LiveDatabaseTests` builds a fake game image in memory (module with a signature, people and club tables) and checks the definition-driven reader finds and edits everything. `EditingTests` covers staging, undo, presets, search, mass edit, freeze, and validates every shipped preset and definition.
- `tests/FMOpenEditor.UiTests`: headless Avalonia tests that drive the real main window. Set `SCREENSHOTS=1` to refresh `docs/screenshots`.
