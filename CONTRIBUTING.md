# Contributing to FM OpenEditor

Thanks for helping. This is a nonprofit, community project. Every bit counts.

## Ways to help

1. **Map the game.** The single most useful thing right now. Each offset you find and verify makes another field editable for everyone. See [docs/DEFINITIONS.md](docs/DEFINITIONS.md).
2. **Write presets.** Presets are JSON files in [`presets/`](presets). Copy one, change it, test it on the demo save, and open a pull request.
3. **Report bugs.** Include your FM build (shown on the Home page after connecting), what you did, what you expected and what happened.
4. **Code.** Pick an issue, or open one to discuss bigger changes first.

## Development

```bash
dotnet build
dotnet test
dotnet run --project src/FMOpenEditor.App -- --demo
```

- Match the surrounding code style. Keep Core free of UI dependencies.
- New properties go in `PropertyCatalog`. The demo save supports them automatically.
- Add or update tests with your change. UI changes: run the UI tests with `SCREENSHOTS=1` and include updated screenshots if they changed.

## The one hard rule

**Everything here must be original work.** Don't contribute code, offsets, signatures, data files or assets copied or extracted from closed-source editors or other proprietary tools, even if you have a licence to use them. Learning *what* features people want from other tools is fine. Copying *how* they do it is not. Pull requests that look copied will be closed.

By contributing, you agree your contribution is licensed under GPL-3.0-or-later.
