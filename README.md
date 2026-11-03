# FM OpenEditor

**A free, open-source, real-time editor for Football Manager 2027.**
Open it next to the game, change what you like, save, and keep playing. Nonprofit and community-built, so everyone can play the game the way they want.

![Player editor](docs/screenshots/player-editor.png)

## What it does

The loop is simple:

1. **Open FM OpenEditor next to your game.** It reads your save while you play: every player, staff member, club and competition, with their attributes, contracts and budgets.
2. **Search and browse** as much as you like.
3. **Change what you like.** Edits are collected as pending changes (highlighted in amber) and written into the running game when you press **Save changes**. Tick *Save instantly* to skip that step, and use *Undo last save* to roll back.

### Features

| | |
|---|---|
| **One-click presets** | Heal, Inspire, Remove bans, Make WonderKid, Rich owner, Top facilities, Remove Brexit, Trained at current club… Presets are plain JSON in [`presets/`](presets), so you can write and share your own. |
| **Edit any player** | Names, nationality, looks, current/potential ability, every technical, mental, physical, goalkeeping and hidden attribute, positions, fitness, morale, injuries, bans, contract, wage, release clause, future transfers. |
| **Run the club** | Balance, transfer and wage budgets, maximum wage, ticket prices, training/youth/corporate facilities, youth recruitment, kits and colours, stadium, league standing and points. |
| **Find anyone** | Advanced Search filters players, staff, clubs, competitions and nations by any field (age, nationality, contract expiry, attributes…), combined with accent-insensitive name search. |
| **Mass edit** | Apply the same change (set, add, multiply, raise to at least, lower to at most) to everyone you pick: a squad or a whole search result. |
| **Freeze or only-up** | Lock someone's attributes, or let them improve but never decline, while the game runs. |
| **Nations** | EU free movement, work permits and non-EU limits, per nation. |
| **Compare** | Two players side by side, with the better value highlighted. |
| **Release on a free** | Right-click any player. |
| **Research tools** | A built-in value scanner and offset calculator for contributors mapping new game versions. |

## Project status: read this first

The editor, presets, search, mass edit, freeze/only-up and the live memory engine are built and tested. You can use all of it today with the **demo save**, a generated, fictional database.

**Live editing of FM27 needs the game's memory layout to be mapped.** FM27's UI is Unity (IL2CPP), but players, clubs and competitions live in the native simulation engine, `game_plugin.dll`. Where each value sits in that module's memory is not public. In this project the layout is a community-maintained JSON file, [`definitions/fm27-27.0.0.json`](definitions/fm27-27.0.0.json). It lists every field the editor supports, all currently **unmapped**. Each offset that gets filled in and verified makes that field editable in the live game, with no code changes. Mapping is the most valuable contribution right now. Start with [docs/DEFINITIONS.md](docs/DEFINITIONS.md).

Until the definition is mapped, **Connect to Football Manager** attaches to the game and opens the Research tools, but lists stay empty.

## Get started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/ElMariones/FM_EDITOR.git
cd FM_EDITOR
dotnet run --project src/FMOpenEditor.App
```

Pass `--demo` to open straight into the demo save:

```bash
dotnet run --project src/FMOpenEditor.App -- --demo
```

Run the tests:

```bash
dotnet test
```

## Safety

- **Back up your save** before editing. You're writing directly into the game's memory.
- Use it in **single-player** saves. Don't use it in online or shared careers.
- If the game runs as administrator, FM OpenEditor has to run as administrator too.
- FM OpenEditor never modifies game files on disk. It only reads and writes the running game's memory, and only when you save.

## How it's built

- **C# / .NET 10**, with an **Avalonia** UI (cross-platform toolkit; live attach is Windows-only for now).
- `src/FMOpenEditor.Core`: memory access, signature scanning, data-driven game definitions, the editing session (staging, save, undo), presets, search, freeze/only-up. No UI dependencies.
- `src/FMOpenEditor.App`: the desktop app.
- `tests/`: unit tests, including the live memory engine running against a fake game image, plus headless UI tests that drive the real window.

More in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Contributing

Mapping offsets, new presets, translations, bug reports and code are all welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Legal

FM OpenEditor is an independent fan project. It is not affiliated with, endorsed by or connected to Sports Interactive, SEGA, or the makers of any other editor. "Football Manager" is a trademark of its owners. The demo save is entirely fictional. All code here is original. Please don't contribute code, data or offset tables copied from closed-source tools.

Licensed under the [GNU GPL v3.0 or later](LICENSE): free to use, study, share and improve, and it stays that way.
