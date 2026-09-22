# Memory Look — 千禧中式梦核

This folder is an additive presentation layer. Source Tripo meshes and their original materials are not modified.

> **Handoff warning:** this is not a disposable cache folder. `Scripts/MemoryPresentationDreamcore.cs`
> is a required partial implementation of `MemoryPresentationController` and must remain under
> version control. Deleting the whole `Generated/MemoryLook` folder will break compilation and/or
> the memory presentation.

## Runtime ownership

- `MemoryPresentationController` remains the single owner of chapter-driven restoration.
- The runtime Volume owns global grading.
- `Memory Reveal` is one URP Full Screen Pass on the PC renderer.
- `CRT_Screen_Overlay` is a replaceable child of the living-room `TV` anchor.

## Art direction values

- Early memory: faded but readable, cool shadows, restrained contrast.
- Restored memory: slightly warm highlights, near-neutral saturation.
- Film grain and vignette remain subtle so interaction targets stay legible.

## Model replacement

Replace the mesh below the existing furniture anchor. Keep `CRT_Screen_Overlay` separate and realign only its local position/scale when the television dimensions change.

## TV CRT tuning

Material: `Assets/Generated/MemoryLook/Materials/MAT_TV_CRT.mat`

- `Screen Tint` RGB: screen color. A: effect opacity (recommended 0.45–0.85).
- `Brightness`: emitted screen brightness (recommended 0.4–0.9).
- `Scanline Strength`: scanline visibility (recommended 0.08–0.22).
- `Noise Strength`: moving grain (recommended 0.01–0.04).
- `Curvature`: barrel curvature (recommended 0.02–0.08).
- `Chroma Offset`: RGB separation (recommended 0.0005–0.002).

Scene object: `Livingroomi/TV/CRT_Screen_Overlay`

- Move its local Position X/Z to align it with a replacement television screen.
- Local Position Y controls distance from the glass. Change this only in very small steps to avoid z-fighting.
- Local Scale X/Y controls screen width/height. Keep Z at 1.

## Quality policy

- PC: Memory Reveal enabled.
- Mobile: no Full Screen Pass added by this demo.
- CRT uses one local transparent draw and no second camera.
