# Effekseer Resource & Path Manager

Windows dependency inspector with effect resource mapping, saved asset libraries and folder batches. Batch discovery always includes Texture, Material, Model/GLB, Sound and Curve paths, plus nested material textures. Existing-file mode updates the effect with a backup and links every asset directly without asset copies or new asset folders. New-package mode copies assets and rewrites texture references in material copies. Full rendering/portability certification remains under development. See [resource mapping and Mega](docs/full-resource-repair.md), [batch and in-place usage](docs/batch-mapping.md) and [legacy single-copy texture mapping](docs/texture-mapping.md).

## Run the desktop inspector

Double-click `เปิดโปรแกรม.cmd` in the project root or open `release/app/ResourceManager.App.exe`. The Windows x64 app bundles its runtime and dependencies in one EXE; no separate .NET installation is needed. Copy the whole `release` folder when sharing so that the license accompanies the app. See [folder guide](docs/folder-guide.md). The Thai desktop has one screen for choosing files/folders, an asset library, existing-file or new-package mode, matching and saving. See [desktop mapping steps](docs/batch-mapping.md). Previous builds and generated validation artifacts are under `artifacts`.

The former Texture-only and advanced-inspection tabs have been removed. Single effects and folder batches use the same mapping workflow, saved libraries, duplicate cases and output paths. Texture renaming is optional and applies only to new packages. The legacy texture-only service remains available through the CLI `map` command.

Read-only inspection is available on the same screen through **ตรวจสอบอย่างเดียว** and the collapsed **รายละเอียดผลตรวจ** section:

1. Choose an effect (`.efkefc`), material (`.efkmat`), model (`.efkmodel`/`.glb`) or project folder. Drag and drop is supported.
2. Select **ตรวจสอบอย่างเดียว** without needing an asset library. Inspecting does not enable saving or write to input files.
3. Expand **รายละเอียดผลตรวจ** to adjust the audit boundary and inspect resource paths and reference details. Successful mapping also supplies its final audit here.
4. Filter by status or search filename/path/reference text.
5. Choose an asset library, then **ค้นไฟล์ที่ขาดในคลัง** to list exact filename candidates. This audit search does not map candidates.
6. The audit summary explains portability limits separately from mapping success.

`VALID` means a file exists inside the boundary. `EXTERNAL` means it exists outside the boundary. An absolute reference to an internal file can still fail after a move. Search roots never make an external resource valid.

The audit parser deliberately reports `MetadataOnly` and cannot certify `PORTABLE`: effect activation, material override semantics and round-trip loading in the official editor are still under investigation. A known missing/external/absolute runtime reference produces `FAIL`; otherwise incomplete inspection produces `INCONCLUSIVE`. The separate texture-copy writer validates supported path changes structurally; it does not provide general rewrite support.

Material defaults and heuristic editor path candidates are labelled separately from runtime/INFO dependencies. Missing defaults may be overridden by the effect and are not automatically proof of runtime failure. Reference counts count recorded occurrences across metadata/runtime/editor storage; they are not the number of particle nodes using a texture.

## Inspect and export from the command line

```powershell
& '.\release\inspector\ResourceManager.Inspector.exe' '.\effekseer\EVFX Shineforge' `
  --output '.\artifacts\inspection\new-audit.json'
```

Optional arguments: `--boundary <folder>`, `--search <folder>`, `--dump-editor <folder>` to decode EDIT as XML. Outputs must be outside the source folder and the boundary. Existing output files are never overwritten. The JSON export contains document versions, chunks, resource references, audit results and search candidates. This milestone supports JSON export; CSV/TXT export comes later.

Exit codes: `0` means inspection completed without parser/discovery errors, **not** portability certification; `1` means a parser/discovery/search diagnostic error; `2` means invalid arguments or output failure; `130` means cancellation.

## Supported read layouts

| Container | Verified layout | Capability |
|---|---|---|
| EFKE `.efkefc` | Container 0, INFO 1610, 1710 and 1810 | Read INFO and compressed EDIT |
| SKFE inside BIN_ | 1500, 1610, 1710, 1810 | Read resource tables; all BIN_ chunks inspected |
| EFKM `.efkmat` | 3, 1610, 1710, 1800 | Read/rewrite PRM_ defaults and DATA texture references using private copies |
| `.efkmodel` | Layout 6 | Inspect geometry structure; preserve bytes and map effect references |
| `.glb` | GLB 2 | Inspect buffer/image URIs; embedded models can be copied and mapped |
| Other versions / `.efkpkg` / standalone `.efk` | Not supported yet | No rewriting; explicit unsupported result |

These layout numbers are not the same as editor release names. Texture mapping recognizes stable editor families 1.60–1.62, 1.70 and 1.80 only when their actual INFO/runtime/EDIT structures pass validation. Real local effects tested are 1.62a, 1.80.3 and 1.80.7; other accepted patches and the 1.70 layout have structural fixture tests, not editor rendering certification. See [version support](docs/version-support.md) and [format investigation](docs/format-investigation.md).

## Build and test

Requires Windows and a .NET 10 SDK. A workspace-local SDK was installed in `.tools/dotnet`; no global SDK was upgraded.

This repository contains source code. The bundled SDK, generated executables and local asset samples are excluded from Git. After cloning, install a .NET 10 SDK and run `scripts/build.ps1 -Publish` to create the executables before using `เปิดโปรแกรม.cmd`.

```powershell
.\scripts\build.ps1
.\scripts\build.ps1 -Publish
```

The script uses the local SDK when present, otherwise `dotnet` from PATH. The solution separates Core, Infrastructure, WPF App and CLI Inspector. Unit tests use xUnit. Runtime app projects do not depend on EffekseerCore or third-party UI packages.

Tests cover boundaries, Windows paths, junctions, missing candidates, cycles, owner-specific resolution, incomplete coverage, malformed files, cancellation and corpus source integrity. Local commercial sample tests automatically skip when samples are absent. Set `EVFX_SAMPLE_ROOT` to an alternative local copy of the unchanged EVFX Shineforge corpus to run them. No user asset is redistributed with the build.

Publish produces single-file app and CLI executables under `release` by default. Build/test logs are under `artifacts/tests`. Generated artifacts and the SDK are ignored by Git.

## Known limitations and next work

- Texture mapping supports sibling copies, in-place replacement with unique backups, and per-effect folders. Cancellation/error rollback is per effect; a batch retains earlier successful effects. No general manual replacement, full Undo/history or crash recovery yet.
- No official editor/API load/save test completed; full portability certification is disabled.
- EDIT candidates use an explicit extension heuristic and can contain inactive settings. INFO unknown resource types remain visible as Unknown.
- Symlink/junction targets are not certified; linked resources become UNRESOLVED and linked directory traversal is skipped with diagnostics.
- No full-drive automatic scan or automatic content-hash duplicate selection. Named asset libraries and the output root are persisted.
- Safety limits: document size 64 MiB, EDIT expansion 32 MiB, 4096 chunks, bounded counts/depth. Large inputs above these limits report an error.

Next: official editor/API verification, broader resource formats, nested repair and complete portability validation. See [development plan](docs/development-plan.md) and [texture mapping](docs/texture-mapping.md).

## License

Project source code is licensed under the [MIT License](LICENSE), copyright (c) 2026 Dezameria. The upstream Effekseer notice is preserved separately in [docs/licenses/Effekseer-MIT.txt](docs/licenses/Effekseer-MIT.txt). Published builds include both license files.
