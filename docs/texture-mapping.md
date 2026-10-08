# Legacy texture-only mapping

Implemented on 2026-10-05. This adds a narrowly scoped texture-path writer to the previous read-only inspector. It creates a new effect copy; it does not edit the original effect or the resource library.

## Current desktop

As of 2026-10-08, the desktop uses one mapping screen for individual effects and folder batches, saved asset libraries, in-place editing and new packages. The redundant texture-only and advanced audit tabs have been removed. See [batch mapping](batch-mapping.md) for current desktop instructions.

Current build: `release/app/ResourceManager.App.exe` (single-file EXE), also accessible through `เปิดโปรแกรม.cmd` in the project root. Copy the `release` folder when sharing so that the license accompanies the executables; no separate .NET installation is needed. The main screen uses Thai labels and opens directly to the mapping workflow.

Read-only scanning is available through **ตรวจสอบอย่างเดียว** and the **รายละเอียดผลตรวจ** foldout on the same screen. It supports effect folders and standalone `.efkmat`, `.efkmodel` and `.glb` files, with boundary settings, status/name filters, original references and missing-resource search. The audit boundary is separate from the mapping library.

The remaining sections document the legacy texture-only core and CLI `map` command, which are retained for compatibility. Their sibling-copy behavior and format limits do not describe the desktop's full-resource workflow.

### Mode 1: link to the existing folder

Use CLI `map --mode link`. The effect copy's references point directly to the selected library files, without copying textures.

```text
D:/Work/Fire/fire.efkefc
D:/Library/Textures/smoke.png

Output: D:/Work/Fire/fire.mapped.efkefc
New reference: ../../Library/Textures/smoke.png
```

The relative path is computed from the output effect location, not the application's working directory. Cross-drive targets require absolute paths. This mode deliberately depends on the existing resource library and is not self-contained when the effect folder is moved alone.

### Mode 2: copy into a named texture folder

Use CLI `map --mode copy --folder MyTextures --output-name fire.mapped.efkefc`. The texture folder is created beside the effect.

```text
Fire/
  fire.efkefc             original, unchanged
  fire.mapped.efkefc      new copy
  fire.mapped.efkefc.mapping.json
  MyTextures/
    smoke.png

New texture reference: MyTextures/smoke.png
```

Only selected matches are copied. Existing identical files are reused after SHA-256 verification. Different content at the same destination blocks Apply; choose a different destination and preview again. The source library remains unchanged.

The output stays beside the original effect so that references to materials, models, sounds and other resources keep their original base. Those resources are not repaired or consolidated by this feature; a moved effect with missing materials still needs that separate repair.

## Match and preview rules

- All texture references in the effect's supported INFO/runtime tables are required for this mapping operation; missing or ambiguous selections block Apply.
- Known EDIT texture candidates are also offered. Editor-only candidates without a selected match are left unchanged with a warning; they do not block the compiled-texture mapping.
- Material default references are not edited by this mapper. Effect-level custom-material texture overrides are mapped where the EDIT field is recognized.
- Changing input, resource folder, mode, output name, copy name or a candidate invalidates the previous preview.
- Output effects and manifests are never overwritten. Choose another output filename to run again.
- A `.mapped` copy is the result to use. Scanning the original again will still show its original paths.

## Supported writer and verification

Supported stable editor families are 1.60–1.62, 1.70 and 1.80 (including 1.80.3). Their INFO/newest BIN_ layouts must match 1610, 1710 and 1810 respectively; older compatibility BIN_ tables may use 1500/1610/1710. Only known INFO/EDIT/BIN_ chunks are writable. Release names alone do not grant write access: unsupported layouts, alpha/beta releases, mismatched metadata/runtime, unknown resource types or unexpected editor fields containing a selected path are rejected. See [version support and evidence](version-support.md) for the test matrix and limits.

This is a structured writer: INFO and runtime texture tables retain entry order/count/flags, string lengths and chunk lengths are recomputed, every BIN_ is retained, and runtime node payload bytes are unchanged. EDIT is decoded and only recognized texture fields are changed before re-encoding. Decoded EDIT is compared after encoding, and a staged output is reparsed to verify every mapped reference and all untouched references before commit.

Original effect and selected resource hashes are frozen in the preview and checked again when applying. Texture copies are staged and hash-checked. New files are moved without overwrite, then rescanned. Cancellation/errors roll back newly created files only; existing/reused files and originals are preserved. If a created file was changed by another process or cannot be removed, a Recovery Required error retains the staging manifest instead of deleting that file.

The `.mapping.json` sidecar records sources, hashes and mappings. This is not a full Undo/history or crash-recovery interface. A process/power interruption may leave a hidden `.resource-map-*` staging directory or partial new output resources, while the original remains untouched.

Official editor/API load/render verification has not yet been performed. The writer has structural/integrity verification and corpus tests; general rewriting and full portability certification remain disabled. Inspect the mapped copy in Effekseer before using it in production.

## CLI

```powershell
# Preview only
& '.\release\inspector\ResourceManager.Inspector.exe' map '.\Fire\fire.efkefc' `
  --resources 'D:\TextureLibrary' --mode copy --folder MyTextures

# Apply to a new effect copy
& '.\release\inspector\ResourceManager.Inspector.exe' map '.\Fire\fire.efkefc' `
  --resources 'D:\TextureLibrary' --mode copy --folder MyTextures --apply

# Link to the selected library instead of copying
& '.\release\inspector\ResourceManager.Inspector.exe' map '.\Fire\fire.efkefc' `
  --resources 'D:\TextureLibrary' --mode link --output-name fire.linked.efkefc --apply
```

CLI selects unique filename matches only. For ambiguous candidates, use the desktop's full-resource workflow, where each candidate can be selected before mapping. A successful mapping exit code does not certify portability.

## Historical texture-only validation (2026-10-05)

- The simplified desktop UI was rendered and inspected at default and minimum window sizes, including empty, preview, blocked, completed and advanced audit screens. UI smoke checks exercised combined discovery/preview, both mode selections, preview invalidation and creation of a demo copy.
- 38 automated tests passed, including the original 27 tests and 11 texture-mapping tests.
- All 17 real effects were copied to locations without textures and linked successfully to the provided library.
- Copy mode tested on effects with both current and legacy runtime layouts, including retaining two BIN_ chunks.
- Runtime node tails are byte-identical before/after mapping; editor trees change only in selected texture values.
- Tested duplicate candidates, required missing files, destination conflicts, identical-file reuse, name collisions, changed effect/texture since preview, invalid paths and cancellation after copying.
- A demo moved effect under `artifacts/mapping-demo` maps six textures into `MyTextures` and resolves them successfully after copying the output and texture folder again.

Format investigation and pinned primary sources: [format-investigation.md](format-investigation.md).
