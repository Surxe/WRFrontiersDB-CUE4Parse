# Future ideas

Ideas that are not built. Each one is kept here so the design of the current export doesn't
rule it out by accident.

## Assets and maps options

- `--export-meshes`: a switch for mesh geometry. Meshes are exported today; this would make it
  optional.
- `--map-filters`: choose which `.umap` packages a map export covers.
- Nanite geometry: export the full-detail Nanite meshes instead of the fallback LOD0.
- Raw level dumps: keep each visited level package as plain JSON under `maps/<World>/levels/`.
- `--worker-count`: an override for the worker count that the memory budget computes.

## Subcommands

- Separate `assets` and `maps` subcommands, or toggles on a single command, instead of one run
  that does both.

## Concurrency

- A provider shared across threads. Each worker mounts its own provider today. Sharing one would
  cut memory, but CUE4Parse's thread safety is unverified; that needs a spike first.
- Check the computed worker counts on a larger machine. The per-phase estimates come from a
  laptop run.

## Mesh exports

- Don't export meshes twice. Scenes can point at the `.uemodel` files the asset phase already
  wrote, instead of writing a second copy.
