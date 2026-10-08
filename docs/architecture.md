# Implemented architecture — read-only milestone

Desktop update 2026-10-08: `MainWindow` now hosts `BatchMappingView` directly with `BatchMappingViewModel`. The duplicated texture-only and advanced tabs and their old `MainViewModel` were removed. Read-only file/folder audits, filters, boundary controls and missing-resource lookup are in `BatchMappingViewModel.Inspection.cs` and the collapsed inspection section of the same screen. Mapping supplies its final audit to that section. `TextureMappingService` remains for CLI compatibility; the desktop uses `ResourceMappingService` for all mapping operations.

Update: the texture-mapping milestone adds `TextureMappingService`, preview models, a restricted `TextureReferenceWriter` and EDIT encoder. It creates mapped copies with staged resource copies and rollback; general parser capabilities and complete portability remain restricted. See `texture-mapping.md`. The sections below describe the original inspector foundation.

```text
WPF App ─┐
         ├─ ResourceScanner (Core) ── IResourceParser / IPathResolver
CLI ─────┘                              │
                               Infrastructure implementations
                               ├─ EffekseerResourceParser
                               ├─ WindowsPathResolver
                               ├─ DocumentDiscovery
                               └─ MissingResourceFinder
```

## Projects

- `ResourceManager.Core`: domain records, parser/resolver contracts, breadth-first graph scanning and conservative portability validation. It does not reference WPF or EffekseerCore. Filesystem reads are performed in Infrastructure.
- `ResourceManager.Infrastructure`: bounded little-endian container/string readers, zlib EDIT decoding, INFO/runtime dependency tables, material JSON/default extraction, filesystem boundary and link checks, cancellation-aware discovery and missing-file search.
- `ResourceManager.App`: WPF view, MVVM observable properties and commands. MainWindow code-behind only handles drag/drop and view construction; scanning/searching is delegated to services.
- `ResourceManager.Inspector`: CLI audit/report/XML-dump entry point. Outputs use CreateNew and are restricted outside the source and boundary. No source writer exists.
- `ResourceManager.Tests`: unit tests plus conditional local corpus integration tests. Assets remain in the user-supplied directory and are not copied into distributable source.

## Graph semantics

ResourceReference identifies owner + storage locator + original string + type + evidence role. The scanner preserves all occurrences, groups resolved file identities for the flat table, and queues nested readable documents once per normalized path. Cycles retain edges without repeating document reads. Reference counts count storage occurrences, not render-node usage.

Documents report CanRewrite=false and explicit coverage. Parser errors, unsupported versions, linked targets or skipped filesystem entries prevent complete-portability certification. Current format adapters intentionally use MetadataOnly because activation/override semantics and editor round-trip behavior are not yet verified.

Runtime/INFO references are stronger evidence of compiled resource requirements than heuristic EDIT candidates or material defaults. Missing/external/absolute runtime references cause FAIL; default-only problems with incomplete semantics produce INCONCLUSIVE. No PASS is issued by the current Effekseer adapters.

## UI behavior and verification

Input or boundary edits invalidate the previous report. Scanning, searching and parsing run away from the UI thread with CancellationToken; progress returns through IProgress. Recovery from expected parser/filesystem failures is represented as diagnostics. Candidate search indexes only the selected scope and never links or copies a candidate.

The app has an internal `--smoke-test <input> <new.png>` mode which scans a corpus, lays out the real XAML content and renders it without showing a desktop window. This verifies startup, binding and layout but does not substitute for interactive mouse/keyboard acceptance or editor load/render verification.

No transaction, rewrite, backup, consolidation or settings persistence has been introduced. They require the next format/compatibility gates described in the development plan.
