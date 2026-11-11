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

## Pictures

Faces, club badges, competition logos and flags are read from the user's own Football Manager graphics folder, the same packs the game uses. Nothing is bundled and nothing is configured.

**Finding the folder.** [`GraphicsLocator`](../src/FMOpenEditor.Core/Graphics/GraphicsLocator.cs) asks the OS where Documents is (so OneDrive and relocated Documents work), looks for `Sports Interactive/Football Manager NN/graphics`, and picks the edition of the connected game, or the newest one. macOS and Linux user-data locations are checked too. A folder chosen in Settings overrides all of that.

**Reading packs.** A pack is a folder of pictures plus a `config.xml` whose records map a file to a place in the game's resource tree:

```xml
<record from="face_1000042" to="graphics/pictures/person/1000042/portrait"/>
<record from="13172353"     to="graphics/pictures/club/13172353/icon"/>
```

[`GraphicsLibrary`](../src/FMOpenEditor.Core/Graphics/GraphicsLibrary.cs) walks the whole tree and reads every `config.xml` it finds, so any pack works wherever it is dropped and whatever it is called. The `to` path gives the type (`person`, `club`, `comp`, `nation`), the game's unique id, and the slot (`portrait`, `icon`, `logo`). Big facepacks have configs of 40+ MB and half a million records, so they are scanned as text and stored compactly (id → folder + filename prefix); over a million pictures index in a few seconds, on a background thread.

**Showing them.** [`ImageService`](../src/FMOpenEditor.App/Services/ImageService.cs) decodes on demand, off the UI thread, at display size, with a bounded cache. Grid rows ask for a picture only when their cell is realised, so scrolling a 500,000-face pack costs nothing up front.

**When there is no picture**, the view shows no picture element: every picture's visibility is bound to whether one loaded, and columns only make room for pictures when the user's packs have that kind. No placeholders. The demo save shows none, because its ids are invented and wouldn't match a pack.

## Look and feel

The visual language (themes, glass, motion, the mascot) is described in [DESIGN.md](../DESIGN.md). Theme colours are `DynamicResource`s defined per theme in `Theme/Tokens.axaml`, so views must not use `StaticResource` for brushes or they won't follow the day/night switch.

## Testing

- `tests/FMOpenEditor.Tests`: Core logic. `LiveDatabaseTests` builds a fake game image in memory (module with a signature, people and club tables) and checks the definition-driven reader finds and edits everything. `EditingTests` covers staging, undo, presets, search, mass edit, freeze, and validates every shipped preset and definition.
- `tests/FMOpenEditor.UiTests`: headless Avalonia tests that drive the real main window. Set `SCREENSHOTS=1` to capture the demo save into `docs/screenshots/demo`. `LiveScreenshotTests` captures the README screenshots from a running game (`LIVE_SCREENSHOTS=1`), read-only, in both themes. `PictureTests` generates a graphics pack on disk and checks pictures appear where the pack has them and collapse where it doesn't. `BRAND=1` regenerates the app icon from the mascot drawing.
