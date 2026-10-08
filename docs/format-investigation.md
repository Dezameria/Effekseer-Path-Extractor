# Phase 0 — EVFX Shineforge investigation

Date: 2026-10-05. Status: read-only prototype implemented; Phase 0 writer/editor compatibility gate remains open.

Subsequent implementation on the same date: two texture mapping modes now create validated effect copies using a narrowly scoped structured writer. The earlier blanket writer decision below is historical; official editor/API verification and general rewriting remain open. See [texture-mapping.md](texture-mapping.md) for current capabilities and 38-test validation.

## Corpus and evidence

User-provided local directory: `effekseer/EVFX Shineforge`.

| Files | Count |
|---|---:|
| Effect `.efkefc` | 17 |
| Material `.efkmat` | 2 |
| Texture `.png` | 36 |
| Total | 55 |

15 effects record ToolVersion `1.62a`, INFO `1610`, and two BIN_ chunks with SKFE versions `1610` and `1500`. `annihilation_flare.efkefc` and `EVFXForge15_16_ImmaculatePurge.efkefc` record ToolVersion `1.80.7`, INFO `1810`, and one BIN_ version `1810`. All effect container versions are `0`. The recorded ToolVersion is evidence from EDIT, not independent proof of editor compatibility.

`Dissolve.efkmat` is EFKM version `3`; `EVFX_PolarMask.efkmat` is EFKM `1610`. Both use a 16-byte header (magic, version, GUID), followed by `DESC`, `PRM_`, `PRM2`, `E_CD`, `GENE`, `DATA` chunks. DATA is JSON with a terminal NUL in this corpus.

## Read layout

- EFKE: 8-byte header, then 4-byte chunk identifier + little-endian int32 payload size + payload. Repeated BIN_ identifiers are legal in these samples and must not be collapsed into one dictionary entry.
- INFO 1610: seven resource tables in the order color texture, normal texture, distortion texture, model, sound, material, curve.
- INFO 1810: typed entries with resource type, flags and path. File types: effect=0, texture=1, sound=2, model=3, material=4, curve=5; others remain Unknown.
- INFO/runtime path strings: little-endian UTF-16, int32 length in code units including terminal NUL.
- Runtime resource tables: texture, normal, distortion, sound, model, material; versions 1610/1810 include curve. Version 1500 has six tables. The remainder of the runtime node payload is not parsed by this prototype.
- EDIT: zlib-compressed structured tree with UTF-8 string tables, int16 indices/counts and int32 booleans. This is not a plain XML file. The inspector decodes the tree to XML for investigation but does not serialize it back.
- Material PRM_: five int32 fields before texture count in the two supported versions. Each texture contains UTF-8 display name, uniform name, default path, and five int32 metadata fields.
- Material DATA: texture catalogue plus texture-bearing node properties. SampleTexture/TextureObject use property 0; TextureObjectParameter uses property 2. These are labelled editor/default references, not unconditional runtime dependencies.

## Current audit results

19 documents parsed with no parser errors. INFO resource sets match the newest BIN_ resource sets for all 17 effects. All compatibility BIN_ resource tables were also inspected. This comparison does not validate runtime node payload semantics.

| Metric | Result |
|---|---:|
| Unique resource paths across all inspected roles | 42 |
| Existing inside boundary | 38 |
| Existing external | 0 |
| Missing | 4 |
| Resources appearing in INFO/runtime tables | 38 |
| Recorded occurrences across all storage representations | 706 |
| Portability certification | INCONCLUSIVE |

Missing references:

| Path | Owner / evidence | Interpretation |
|---|---|---|
| `Textures/Check01.png` | `Materials/Dissolve.efkmat`, PRM_ default and DATA | Missing material default/editor resource; may be overridden |
| `Textures/Distortion01.png` | `Materials/Dissolve.efkmat`, PRM_ default and DATA | Missing material default/editor resource; may be overridden |
| `Texture/DCSD_Aura_001.png` | ImmaculatePurge, EDIT only | Editor candidate; absent from INFO and runtime tables |
| `Texture/DCSD_GradientH.png` | EDIT in DazzlingGleam, ShimmeringSalvo, BanishRay, RadiantShower, ShineforgeSlash/Strike/Shoot/Pierce/Blast and SheerBrilliance | Editor candidate; absent from INFO and runtime tables |

The singular `Texture` folder in the pack must not be silently substituted for references to plural `Textures`. Neither fuzzy names nor visually similar images justify automatic replacement. There is no observed external dependency in this corpus's parsed tables; this is not proof that every possible Effekseer project behaves the same way.

All 55 input-file SHA-256 hashes were recorded before development inspection and are checked after the final scan. The automated corpus test also verifies bytes do not change during inspection. Local baseline/report/XML are under `artifacts/inspection`; user assets and decoded editor content are not included in distributable source.

## Resolution evidence and material override nuance

Official NodeRoot stores the effect's full filename as its base path. Renderer texture properties use that root base. `Path.SetRelativePath` delegates to `Misc.GetAbsolutePath`, which resolves a URI relative to the owning file. Material default paths are resolved relative to the material file in RendererCommonValues, while the effect stores resulting texture values relative to the effect's base. Existing parameter values can override material defaults. Therefore a missing material default is not sufficient to claim the compiled effect cannot render.

The inspector currently implements normalized filesystem resolution from the owning document folder. Ordinary relative paths in this corpus resolve successfully. URI escaping/percent/hash edge cases and actual editor behavior still require verification; no fallback to working directory, app directory or search root is used.

## Pinned primary sources

Current-source revision: `82b37081a302b9f9eff0bf14dc6c845fca8c3c54`.

- [EfkEfc.cs — container, INFO, EDIT and Save](https://github.com/effekseer/Effekseer/blob/82b37081a302b9f9eff0bf14dc6c845fca8c3c54/Dev/Editor/EffekseerCore/IO/EfkEfc.cs)
- [Exporter.cs — runtime tables and used material textures](https://github.com/effekseer/Effekseer/blob/82b37081a302b9f9eff0bf14dc6c845fca8c3c54/Dev/Editor/EffekseerCore/Binary/Exporter.cs)
- [Define.cs — file type values](https://github.com/effekseer/Effekseer/blob/82b37081a302b9f9eff0bf14dc6c845fca8c3c54/Dev/Editor/EffekseerCore/Define.cs)
- [MaterialInformation.cs — PRM_ and DATA](https://github.com/effekseer/Effekseer/blob/82b37081a302b9f9eff0bf14dc6c845fca8c3c54/Dev/Editor/EffekseerCore/Utils/MaterialInformation.cs)
- [RendererCommonValues.cs — material default/override resolution](https://github.com/effekseer/Effekseer/blob/82b37081a302b9f9eff0bf14dc6c845fca8c3c54/Dev/Editor/EffekseerCore/Data/RendererCommonValues.cs)
- [Path.cs](https://github.com/effekseer/Effekseer/blob/82b37081a302b9f9eff0bf14dc6c845fca8c3c54/Dev/Editor/EffekseerCore/Data/Value/Path.cs), [Misc.cs](https://github.com/effekseer/Effekseer/blob/82b37081a302b9f9eff0bf14dc6c845fca8c3c54/Dev/Editor/EffekseerCore/Utils/Misc.cs), [NodeRoot.cs](https://github.com/effekseer/Effekseer/blob/82b37081a302b9f9eff0bf14dc6c845fca8c3c54/Dev/Editor/EffekseerCore/Data/NodeRoot.cs)
- [efkMat.Models.cpp — material JSON save/load](https://github.com/effekseer/Effekseer/blob/82b37081a302b9f9eff0bf14dc6c845fca8c3c54/Dev/Cpp/EffekseerMaterial/efkMat.Models.cpp)
- [EffekseerCore.csproj — upstream net9.0/dependencies](https://github.com/effekseer/Effekseer/blob/82b37081a302b9f9eff0bf14dc6c845fca8c3c54/Dev/Editor/EffekseerCore/EffekseerCore.csproj)
- [Exporter from tag 1.51 — runtime 1500 six-table layout](https://github.com/effekseer/Effekseer/blob/bc26cac882002fea8e6135788695342937b08082/Dev/Editor/EffekseerCore/Binary/Exporter.cs)

Source files fetched for investigation are local-only under `.research/upstream`. The executable does not ship EffekseerCore or the commercial samples. Effekseer source is MIT licensed; upstream notice is preserved in `docs/licenses/Effekseer-MIT.txt`.

## Writer decision and remaining gate

Decision for this milestone: a structured read-only adapter, **CanRewrite=false for every version**. Do not replace bytes, modify only EDIT, or discard older BIN_ chunks.

Preferred writer investigation: official load → change typed path properties → official save/export into staging, if runtime and native dependencies permit. Upstream project targets net9.0; this does not by itself prove that its released API/native components work in the net10.0 app. No official library has yet been loaded or round-trip saved in this workspace.

Remaining work before Phase 0 is complete:

1. Verify real editor load/render on the fixture corpus and compare active resource usage with the table audit.
2. Verify resolution with controlled absolute/relative/URI and material override fixtures from the editor.
3. Prove no-op and edited official API round trips, preserving non-path semantics and all runtime versions needed by consumers.
4. Choose a version-aware writer approach and distribution dependencies/license based on those results.
5. Add schema-aware EDIT references and override/activation analysis before any complete-portability claim.

Phase 1 has initial scan/table/detail/filter/search/progress/cancel UI, but candidate selection, complete graph semantics and virtual relocation testing remain. Phase 2 transactions and repair are not started.

## Validation performed for this milestone

- Release solution build: passed, zero warnings/errors.
- Automated tests: 27 passed, zero failed/skipped in this workspace, including the real corpus and junction checks.
- Published self-contained CLI: scanned all 19 documents and ran exact-name missing search; no candidates exist for the four missing references inside the provided pack.
- Published self-contained WPF startup/scan/layout smoke check: exit code 0; rendered preview visually inspected.
- Source baseline comparison after the final scan: all 55 input files unchanged, zero new input files.
- Not yet performed: interactive UI acceptance, clean-machine verification and official Effekseer editor/API round trips.
