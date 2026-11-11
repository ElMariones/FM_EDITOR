# FM OpenEditor design language

The source of truth for how the app looks, moves and speaks. Tokens live in
[`src/FMOpenEditor.App/Theme/Tokens.axaml`](src/FMOpenEditor.App/Theme/Tokens.axaml), control styles in
[`Theme/Controls.axaml`](src/FMOpenEditor.App/Theme/Controls.axaml).

## Theme statement

**Floodlit, playful, precise.** A night match seen through glass: the pitch is dark, the light is
coloured and soft, and the numbers are sharp. The app edits a game people love, so it should feel
like part of matchday, not like a hex editor.

## Tiki, the mascot

Tiki is a small bot, named for tiki-taka: a glossy ball with two pill eyes. The ball is the
football; the eyes are the editor, watching your save. It is deliberately the simplest thing that
can still look at you, so it stays readable at 16 pixels and never competes with the data.

Tiki has one behavioural idea, and it is colour:

- **Chroma** (lime, turquoise, blue) is Tiki's resting skin and the brand gradient.
- **Sunset** (amber, coral, pink) is Tiki with unsaved changes. Warm always means "not saved yet":
  the pending pill, the dot on an edited row, an edited field's label, and Tiki in the top bar all
  turn warm together, and cool again when you save.

And three small behaviours, all in the eyes:

- They **follow the pointer**, easing rather than snapping, and the face tips slightly into the turn.
- They **blink** every few seconds.
- They squint into a **smile** when something goes well: a save, the end of the tour.

Tiki is drawn in code on a 256 grid in [`Controls/Mascot.cs`](src/FMOpenEditor.App/Controls/Mascot.cs),
not kept as a bitmap: the window icon, `Assets/icon.ico` and the PNGs in [`docs/brand`](docs/brand)
are rendered from it. After changing the drawing, regenerate them:

```bash
BRAND=1 dotnet test tests/FMOpenEditor.UiTests --filter BrandAssetTests
```

Where Tiki appears: app icon, top bar (mood indicator), home hero, empty states, the connecting
overlay, and as the guide in the tour. Never as decoration inside data, and never more than once
per view.

## The tour

The home screen stays quiet. The first time a save is connected, Tiki says hello and gives a
five-stop walk through the workspace ([`Controls/TourOverlay`](src/FMOpenEditor.App/Controls/TourOverlay.axaml.cs),
steps in `MainWindow.axaml.cs`). The screen dims except for the control being explained, a Chroma
ring glides from stop to stop, and Tiki looks at what it is describing. It runs once; the
**?** in the top bar replays it (on the home screen it gives a two-stop version). Rules for writing a stop: one idea, two sentences at most, say what
the user can do, and point at a control that exists on that screen (the UI tests check this).
## Colour

Two themes, one structure. Night is the default; Day follows the same roles on a pale sky.

| Role | Night ("Floodlit") | Day ("Matchday") |
|------|--------------------|------------------|
| Background | `#070A18` to `#0A1228` | `#EAF1FB` to `#E8F6F2` |
| Ink | `#EEF3FF` | `#0B1330` |
| Muted | `#9AA6C6` | `#4F5B7C` |
| Accent (text, icons) | `#3DEBB8` | `#067A5B` |
| Pending (text) | `#FFB938` | `#A85800` |
| Error | `#FF6B81` | `#C8203F` |
| Chroma gradient | `#C6FF4D`, `#2CE8B5`, `#3F8CFF` | `#B4F23A`, `#22D6AC`, `#4C8DFF` |
| Sunset gradient | `#FFC53D`, `#FF7A45`, `#FF3D8B` | same |

Rules:

- Chroma fills are for the one primary action in a view, the selected-page marker, icon tiles and
  the selected tab. Text on Chroma is always `#04130D`.
- Sunset is never used for anything except unsaved state.
- Body text uses Ink or Muted only; accent colour on text is reserved for state ("frozen", "better").

## Glass

Avalonia has no backdrop blur, so the blur is put into the light instead: four large, soft pools of
colour drift slowly behind everything ([`Controls/Aurora.axaml`](src/FMOpenEditor.App/Controls/Aurora.axaml)),
and panels only need to be translucent to read as frosted.

| Surface | Class | Use |
|---------|-------|-----|
| Glass | `Border.glass` / `.card` | Page panels and cards: gradient fill, lit top-left edge, soft shadow, radius 20 |
| Bar | `Border.bar` | Top bar, sidebar, bottom bar: denser so text over the light stays readable |
| Inset | `Border.inset` | A recessed area inside glass (filters, mass edit, list rows) |
| Pill | `Border.pill` | Status chips; `.pending` for the Sunset version |
| Tile | `Border.tile` | 38px gradient square holding an icon |

Glass is never stacked more than two deep (glass, then inset).

## Type

Inter for everything, Cascadia Mono for numbers that are compared down a column (attributes, money,
ids, addresses) so digits line up like a scoreboard.

| Class | Size / weight | Use |
|-------|---------------|-----|
| `display` | 44 bold, tight | Home headline only |
| `h1` | 26 bold | Page and entity titles |
| `h2` | 15 semibold | Card titles |
| `eyebrow` | 10.5 bold, tracked, caps | Section labels |
| body | 13.5 | Everything else |
| `num` | mono | Numeric cells and values |

## Icons

A single line set drawn for the app ([`Controls/Icon.cs`](src/FMOpenEditor.App/Controls/Icon.cs)):
24 grid, 1.75 stroke, round caps, in the inherited text colour. No emoji in the UI.

## Motion

Motion explains state; it is never the point.

| Moment | Motion |
|--------|--------|
| Hover on a control | 140ms colour ease; buttons lift 1-2px |
| Press | scale to 0.96 |
| Hover on a card | lifts 4px, shadow deepens (220ms) |
| Page or panel appears | fades in and rises 14px (320ms, ease-out) |
| Nav selection | the Chroma marker grows in; the icon tips on hover |
| Pending count changes | the pill pops |
| Unsaved changes exist | Tiki glides to Sunset over 700ms |
| Row hover | its picture grows slightly |
| Loading | a rotating Chroma arc; on connect, around Tiki |
| Background | the light drifts on 34-47s loops |

**Reduce motion** (Settings) turns off entrances, loops and the mascot's blinking. Hover and press
feedback stay, because they confirm input. Code that adds decorative motion goes through
[`Services/Motion.cs`](src/FMOpenEditor.App/Services/Motion.cs) so the switch keeps working.

## Pictures

Faces, badges, competition logos and flags come from the user's own Football Manager graphics
folder, never bundled. The rule for every view: **bind the picture's visibility to whether a picture
exists.** No silhouettes, no placeholder boxes; without a picture, the name stands alone.
See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#pictures).

## Voice

Plain, short, second person. Say what will happen ("Write every edit to the game as you make it"),
not what the control is. Errors say what went wrong and what to do next.
