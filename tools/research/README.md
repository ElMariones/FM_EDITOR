# Research tools

Python scripts that read Football Manager's own field tables to find where each value lives. They produced most of [addresses.md](../../addresses.md), and they regenerate `definitions/fm27-*.json` and `src/FMOpenEditor.Core/Model/catalog.generated.json`.

They need Python 3.10+, `numpy`, `psutil`, `zstandard` and `capstone`, and a running game with a save loaded. Set the paths at the top of `memlib.py` and `emit.py` for your machine first. The scripts only read the game's memory. They write their outputs to the folder above the scripts, so copy the folder somewhere scratch before running them.

| Step | Script | What it does |
|---|---|---|
| 1 | `fmf.py <game>/shared/data/editor.fmf out_editor` | Unpacks the zstd frames of an `.fmf` archive. |
| 2 | `schema.py` | Parses the unpacked XML into `schema.json`: every field code with its name, type and range. |
| 3 | `allrun.py` | Finds each `db::` class's get/set field function (vtable slots 1 and 2, plus the person classes listed in the script) and analyses every handler. Writes `events.pkl`. |
| 4 | `consolidate.py` | Picks the bytes each handler reads or writes. Writes `fields.json`. |
| 5 | `gen.py` | Types each candidate and checks it against live records. Writes `gen.json`, with the reason for every rejection. |
| 6 | `emit.py` | Writes the definition, the generated catalog and markdown tables. |

The other files:

- `memlib.py`: the shared library (process access, PE and RTTI parsing, heap census by vtable).
- `ana.py`: the analyser, a small symbolic emulator over capstone.
- `sniff.py`: classifies each 8-byte slot of a class's objects (string, or pointer to which class).
- `d.py`: disassembles a function with field codes annotated.
- `show.py`: prints `fields.json` per record type.
- `deep.py`: analyses chosen field codes of one function with larger inlining limits, for handlers the full run gives up on.
- `slot.py`: disassembles chosen vtable slots of a class.

After a game patch, rerun steps 3 to 6. Class names and field codes survive patches. Offsets may move, and so may the few hard-coded function addresses in `allrun.py` and `extra_targets.txt` (the person get/set functions, which sit behind adjustor thunks). Running `d.py` on the class's vtable slots finds them again.
