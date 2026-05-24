# sector-editor-avalonia

Avalonia port of `tools/sector-editor/` — by far the largest editor in the
suite at **19,386 LOC across 93 files / 16 forms**. Edits the `sectors`,
`systems`, and `sector_objects` tables with an interactive map canvas.

## Tier breakdown (multi-session port)

The original is the only editor in the suite that depends on **Piccolo2D**
(`UMD.HCIL.Piccolo.dll`) — a defunct .NET 2.0-era 2D scene-graph library
that builds on `System.Drawing.Graphics`. It has no Avalonia port. Roughly
**149 references** to Piccolo types (`PCanvas`, `PLayer`, `PNode`, `PPath`,
`PImage`, `PText`, `PInputEventHandler`, etc.) thread through `SectorWindow`,
`SystemWindow`, `mainFrm`, and the 9 sprite classes under `Sprites/`.

Trying to do this in one commit produces either an unreviewable diff or a
broken half-port. Tier 12 is therefore split:

| Sub-tier | Scope | Status |
|---|---|---|
| **12a** | Project scaffold (csproj, App.axaml, Program.cs --smoke, MainWindow shell, slnx wiring) | this commit |
| **12b** | Sql/ layer ported through `commontools-avalonia`'s parameterised `DB.Instance` (kills the ~150 sprintf-style SQL sites in the original) | pending |
| **12c** | Simple dialogs (Login, AboutBox, NewSystem, NewSector, NewSectorObject, NewSectorObjectType, OptionsGui, SoundEffects, HarvestableResTypes, MobGroup, Destination, frmContrast, BaseAssets, Settings, NewFrm) | pending |
| **12d** | `PiccoloShim/` — minimal Avalonia-backed shim mapping the ~12 Piccolo types the editor uses onto Avalonia primitives (`Canvas`, `Control` with `Render(DrawingContext)`, `ScaleTransform` for zoom, custom pan event handler) | pending |
| **12e** | Sprites/ port (mostly mechanical translation once the shim is in place) and `SectorWindow`+`SystemWindow`+`UniverseWindow`+`TreeWindow` wiring into mainFrm's TreeView + canvas + property pane | pending |

The original WinForms editor still builds and still runs under WINE on
Linux — `tools/README.md` documents this. The Avalonia port is the
end-state; the WinForms binary is the interim story.

## Status

`dotnet build` clean. Headless smoke instantiates the shared `Login` window
and the `MainWindow` shell. The shell exposes the eventual layout
(TreeView left, tabbed canvas centre, properties right, menu top, status
bar bottom) but the tree is empty, the tabs hold empty `Canvas`es, and
the property pane is blank — wiring lands in Tier 12b+.

## Run

```
dotnet run --project tools/sector-editor-avalonia/                # interactive (will show empty shell)
dotnet run --project tools/sector-editor-avalonia/ -- --smoke     # CI smoke
```

## Why a Piccolo shim instead of a fresh-from-scratch canvas?

Considered three approaches for replacing Piccolo2D:

1. **Direct Avalonia rewrite of `SectorWindow`/`SystemWindow`/`mainFrm`** —
   throws away the structure of the original. Hard to verify behaviour
   parity with the WinForms editor; the diff against the original
   `Sprites/` files becomes total rewrites instead of identifiable line
   changes.

2. **A minimal Piccolo-API shim on Avalonia primitives** (chosen) —
   ports `Sprites/MobSprite.cs` etc. as near-1:1 translations because the
   shim exposes `PLayer`, `PNode.AddChild`, `PPath.CreateEllipse`,
   `PImage`, `PText`, `MouseDown`/`MouseDrag`/`MouseUp` event signatures
   that match the original. The shim is internal to this project — not a
   reusable library — and only implements the surface the editor actually
   uses (~12 classes, mostly thin wrappers over Avalonia `Visual`s).
   Pan/zoom done via `ScaleTransform` + a pan event handler on the root
   `Canvas`.

3. **Drop the map canvas entirely; expose only the tabular editing UI**
   — defeats the point of a sector editor. Rejected.

## License

CC BY-NC-SA 3.0, inherited from the upstream Net-7 sector editor sources.
See `LICENSES/Net7`. Piccolo2D itself is BSD-licensed (UMD HCIL); the
shim is a from-scratch reimplementation of a tiny subset of its API
surface against Avalonia, so no Piccolo source is carried.
