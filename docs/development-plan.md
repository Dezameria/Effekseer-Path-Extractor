# วิเคราะห์ระบบและแผนพัฒนา Effekseer Resource & Path Manager

ร่างสำหรับระบบ AI สร้างและแก้ Effect เพิ่มเติมอยู่ใน [AI Effekseer architecture](ai-effekseer-architecture.md) เริ่ม implementation ของ isolated file host สำหรับ 1.80.7/1.70e แล้ว ดู [วิธีใช้และผลทดสอบ](editor-host.md) ส่วน live Editor Bridge, Preview และ AI ยังเป็นขั้นถัดไป

วันที่จัดทำ: 5 ตุลาคม 2026

อ้างอิงข้อกำหนด: `master prompt.md` ข้อ 1–90
สถานะล่าสุด 7 ตุลาคม 2026: มี Inspector, Map Texture แบบสำเนา/แก้ไฟล์เดิมพร้อมสำรอง/แยกโฟลเดอร์ต่อ Effect, ค้นแบบหลายไฟล์ และบันทึกคลัง Asset แล้ว ผ่านการทดสอบโครงสร้างและการย้อนงานในขอบเขตที่รองรับ 48 รายการ ส่วน editor/API load/render, generic rewrite และ full portability ยังไม่รับรอง ดู `batch-mapping.md` และ `format-investigation.md` แผนด้านล่างเป็น baseline ของขอบเขตระบบเต็ม

อัปเดตตามคำขอเพิ่ม: ทำ texture mapping สองโหมดแล้ว (link ไปคลังที่ระบุ / copy เข้าโฟลเดอร์ที่ตั้งชื่อได้) โดยสร้าง effect สำเนาและตรวจ writer เฉพาะ layout ที่รองรับ ดู `texture-mapping.md`; การตรวจใน editor จริง, general rewrite และ portability certification ยังอยู่ระหว่างพัฒนา

## 1. สภาพโปรเจกต์ที่ตรวจพบ

บันทึกส่วนนี้เป็น baseline ก่อนเริ่มพัฒนา ปัจจุบันเพิ่ม source, tests และตรวจ corpus EVFX Shineforge แล้ว รายละเอียดล่าสุดอยู่ใน `format-investigation.md` และ README

- Workspace มี `docs/master prompt.md` เป็นไฟล์งานที่ตรวจพบ ยังไม่มี solution, source code, tests หรือไฟล์ Effekseer ตัวอย่าง
- ไม่พบ `AGENTS.md` ใน workspace และโฟลเดอร์แม่ที่ตรวจ
- เครื่องมี .NET SDK 3.1.426 และ 9.0.304 จากการตรวจครั้งนี้ ยังไม่พบ SDK 8 หรือ 10
- จึงเป็นงานเริ่มต้นระบบใหม่ ไม่ใช่การปรับปรุงระบบเดิม และยังพิสูจน์ parser/resolver กับไฟล์จริงไม่ได้
- ไม่แก้เอกสารข้อกำหนดเดิม ข้อเสนอที่ต่างจากเอกสารระบุไว้ในแผนนี้

## 2. ข้อสรุปการวิเคราะห์

ระบบนี้เป็นเครื่องมือจัดการ dependency และแปลงโปรเจกต์ให้ย้ายได้ ไม่ใช่เพียงโปรแกรมค้นหาชื่อไฟล์ งานยากที่สุดคือความถูกต้องของ parser, กฎ resolve path และการแก้ไฟล์หลายตัวอย่างปลอดภัย ส่วนหน้าจอเป็นชั้นนำเสนอผลและให้ผู้ใช้ตัดสินใจ

กระบวนการหลัก:

```text
Load → Parse → Build Graph → Resolve → Audit
                                      ↓
                  Search / Select Replacement
                                      ↓
                  Plan → Preview → Apply Safely
                                      ↓
                 Reparse → Rescan → Relocation Check
```

หลักการที่ต้องรักษา:

1. ไฟล์ที่หาเจออาจอยู่ภายนอก project จึงแยก `Resolved` ออกจาก `InsideBoundary`
2. Search Root ใช้ค้นหา ไม่เพิ่มสิทธิ์ให้ไฟล์กลายเป็น internal
3. การเลือก replacement เป็นข้อเสนอจนกว่าจะ Apply สำเร็จ ไม่เปลี่ยนผล audit ของไฟล์เดิมทันที
4. ชื่อไฟล์เดียวกันไม่ยืนยันว่าเป็น resource เดียวกัน
5. รายงานความครบถ้วนของการตรวจด้วย หากอ่าน dependency บางส่วนไม่ได้ ห้ามประกาศ Portable
6. การย้ายต้องตรวจทุก reference ตามเจ้าของ reference รวมถึง material และ effect ที่อ้างต่อ

## 3. ข้อมูลจากแหล่งทางการและข้อจำกัดของการตรวจ

ซอร์ส `EfkEfc.cs` บน branch `master` ของ Effekseer ที่อ่านในครั้งนี้แสดง header `EFKE` และ chunk `INFO`, `EDIT`, `BIN_`; ส่วน `EDIT` ใช้โครงสร้างข้อมูลที่บีบอัดผ่าน zlib และกระบวนการ Save สร้างข้อมูล runtime ผ่าน exporter ด้วย จึงมีข้อมูลหลายส่วนที่อาจต้องปรับให้สอดคล้องกันเมื่อเปลี่ยน path ไม่ควรแก้เพียง string หรือ chunk เดียวแล้วถือว่าถูกต้อง

แหล่งอ้างอิง: [Effekseer EfkEfc.cs](https://github.com/effekseer/Effekseer/blob/master/Dev/Editor/EffekseerCore/IO/EfkEfc.cs)

นี่เป็นหลักฐานจากซอร์สปัจจุบัน ไม่ใช่การรับรองว่าไฟล์จากทุกเวอร์ชันมี layout เดียวกัน Phase 0 ต้องเลือก tag/commit ที่ตรงกับไฟล์ตัวอย่างและบันทึก revision ให้ทำซ้ำได้ ตรวจเวอร์ชัน container, dependency metadata และ runtime แยกกัน ไม่ใช้เพียง header version เป็นตัวตัดสินความเข้ากันได้

เอกสาร Effekseer 16x แยก `.efk` ซึ่งเป็น runtime binary ออกจาก `.efkpkg` ซึ่งรวม effect และ resource จึงต้องแยก adapter ของ effect, material และ package ไม่ถือว่า package ใช้ parser เดียวกับ effect

แหล่งอ้างอิง: [Effekseer File Export](https://effekseer.github.io/Helps/16x/Tool/en/ToolReference/fileExport.html)

ยังไม่ได้พิสูจน์ในครั้งนี้:

- กฎ resolution จริงของแต่ละ reference และแต่ละเวอร์ชัน
- dependency ของ `.efkmat` ถูกเก็บที่ใด และส่วนใดเป็น reference ที่ใช้งานจริง
- การใช้ EffekseerCore/API โดยไม่เปิด GUI รวมถึง native dependencies และข้อจำกัด runtime
- การรักษาข้อมูลที่ไม่เกี่ยวกับ path หลัง load/save
- license และเงื่อนไขการแจก library/sample พร้อมแอป

ทั้งหมดเป็นงานที่ต้องปิดใน Phase 0 ก่อนเปิดความสามารถ rewrite

## 4. จุดที่ควรปรับจาก master prompt

| ประเด็น | ความเสี่ยง | ข้อเสนอสำหรับการพัฒนา |
|---|---|---|
| Nested dependency อยู่ Phase 3 แต่ Portability อยู่ Phase 1 | effect ผ่าน แต่ material ยังอ้างไฟล์ภายนอก | วาง graph แบบ recursive ตั้งแต่แรก และรองรับ nested ของ format ที่ประกาศว่าตรวจครบ; ส่วนที่ยังไม่รองรับให้ผล Inconclusive |
| Manual replacement อยู่ Phase 1 แต่ rewrite อยู่ Phase 2 | ผู้ใช้เข้าใจว่าแก้ไฟล์แล้ว | Phase 1 เลือก candidate และ preview ได้; Apply replacement เปิดพร้อม safe rewrite |
| Backup/rollback ตามหลัง rewrite ใน workflow | prototype เขียนไฟล์โดยไม่มี recovery | transaction เป็น prerequisite ของทุกคำสั่งเขียน |
| VALID เท่ากับ exists + inside boundary | absolute path ภายใน root ยังชี้ตำแหน่งเดิมเมื่อย้าย | แยก Boundary Audit ออกจาก Relocation Test |
| Status รวม VALID, DUPLICATE, REDIRECTED | resource อาจเป็น external และ duplicate พร้อมกัน | เก็บสถานะการ resolve, boundary, diagnostics และ proposed action แยกกัน |
| Shared Root ถือว่า valid | โฟลเดอร์ที่ย้ายไม่ได้รวม shared root | แยก policy-valid ออกจาก self-contained และระบุ export set ที่รวม shared resource จริง |
| Unknown version เป็น read-only | read-only อาจยังอ่านได้ไม่ครบ | รายงาน capability และ completeness แยกกัน; ไม่รู้ layout ให้ Unsupported/Inconclusive |
| .NET 8 เป็น stack ที่แนะนำ | ใกล้หมดระยะ support | เสนอ .NET 10 LTS โดยรอผลความเข้ากันได้กับ Effekseer API |
| Undo อยู่ Phase 4 | error recovery ต้องใช้ก่อนหน้านั้น | rollback และ journal เริ่ม Phase 2; หน้าประวัติ/Undo สำหรับผู้ใช้เพิ่ม Phase 4 |

.NET 8 สิ้นสุด support วันที่ 10 พฤศจิกายน 2026 ขณะที่ .NET 10 LTS มี support ถึง 14 พฤศจิกายน 2028 ตาม [Microsoft .NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy) ดังนั้น baseline ที่เสนอคือ C# + .NET 10 + WPF + MVVM ถ้ามีข้อบังคับใช้ .NET 8 ต้องมีแผนอัปเกรดก่อนหมด support ไม่ติดตั้ง SDK หรือเปลี่ยน stack ในงานวางแผนครั้งนี้

## 5. สถาปัตยกรรมที่เสนอ

เริ่มด้วยจำนวน project เท่าที่จำเป็น เพื่อให้แยก logic และทดสอบได้โดยไม่เพิ่มโครงสร้างเกินงาน

```text
EffekseerResourceManager.sln
src/
  ResourceManager.Core/
    Models/                 Resource, Reference, Graph, Result
    Scanning/               Recursive scan and diagnostics
    Paths/                  Resolution and boundary policies
    Search/                 Candidate matching and indexing
    Planning/               Immutable operation plan
    Validation/             Integrity and portability rules
    Abstractions/           Filesystem, parser and operation contracts
  ResourceManager.Infrastructure/
    Effekseer/              Version-aware format/API adapters
    FileSystem/             Copy, hash, links and canonical paths
    Transactions/           Backup, journal, commit and recovery
    Persistence/            AppData settings and project manifest
    Reporting/              JSON, CSV and TXT
  ResourceManager.App/
    Views/                  WPF windows and panels
    ViewModels/             Commands, selection and progress
    Services/               Dialogs and composition root
tests/
  ResourceManager.Core.Tests/
  ResourceManager.IntegrationTests/
  Fixtures/
docs/
```

Dependency direction: `App → Core + Infrastructure`, `Infrastructure → Core`; Core ไม่อ้าง WPF หรือ Effekseer implementation และไม่มี filesystem access ที่ซ่อนอยู่ใน ViewModel

Interfaces หลัก: `IResourceParser`, `IResourceResolver`, `IBoundaryPolicy`, `IResourceScanner`, `IResourceSearchService`, `IOperationPlanner`, `IReferenceRewriter`, `ITransactionService`, `IPortabilityValidator` ใช้ async และ CancellationToken ในงานที่มี I/O หรือใช้เวลานาน

Parser ต้องคืน format identity/version, capabilities, diagnostics, completeness และรายการ reference ส่วน writer ต้องรับ reference identity และ expected original value ไม่ใช้ dictionary ของ string path เพียงอย่างเดียว เพราะ string เดียวกันในคนละไฟล์อาจหมายถึงคนละ resource

## 6. แบบจำลองข้อมูล

| Model | หน้าที่และข้อมูลสำคัญ |
|---|---|
| ProjectContext | Entry effects, AllowedRoots[], SearchRoots[], strict policy, folder mapping และ export set |
| ParsedDocument | Document identity, format/version, capability, completeness และ diagnostics |
| ResourceNode | File identity, lexical/physical path, type รวม Curve/Unknown, existence, metadata และ hash เมื่อจำเป็น |
| ResourceReference | Owner document, locator/node/property, original text, resolution base และ target node |
| DependencyGraph | Nodes + reference edges, root effects, cycles, reference count และ scan coverage |
| AuditResult | Resolve outcome, boundary result, diagnostics, duplicates และ ignore policy |
| Candidate | Path, matching evidence, optional hash และเหตุผลการจัดอันดับ |
| OperationPlan | Copy/reuse/rewrite actions, mapping, conflicts, precondition hashes และ parser capabilities |
| TransactionRecord | Operation id, backups, hashes, created files, journal state และ recovery outcome |
| PortabilityResult | PASS/FAIL/INCONCLUSIVE, coverage และรายการสาเหตุ |

หนึ่งไฟล์มี reference ได้หลายจุด และมีเจ้าของหลาย effect จึงต้องแยก ResourceNode ออกจาก ResourceReference นับ reference จาก edges; deduplicate การอ่านไฟล์ด้วย file identity แต่ไม่ลบ reference edges เมื่อพบ cycle

Depth เก็บเป็นข้อมูลการ traversal ต่อ root/edge หรือใช้ minimum depth สำหรับ flat view ไม่ถือว่า resource มี depth เดียวเสมอไป

แยกสถานะพื้นฐาน เช่น `Found/Missing/Unresolved/AccessError` และ `Internal/External/Shared/Unknown` ออกจาก tag `Duplicate`, `Ambiguous`, `Redirected`, `Ignored` UI ยังแสดงคำตาม master prompt ได้ แต่รายละเอียดต้องไม่ซ่อนปัญหาที่เกิดพร้อมกัน

## 7. กฎ path และ portability

- Normalize path โดยมี base ที่ parser/resolver ยืนยันแล้ว ไม่พึ่ง current working directory ของแอป
- เปรียบเทียบ root ด้วย path segments และ separator boundary ไม่ใช้ StartsWith แบบตรง ๆ ที่ทำให้ `Bloom2` อยู่ใต้ `Bloom`
- รองรับ slash, dot segments, Unicode, drive casing, UNC และแยก drive-relative เช่น `C:foo` ออกจาก absolute path
- ตรวจ lexical path และ physical target ของ junction/symlink; MVP อย่างน้อยตรวจและให้ Unknown/External เมื่อพิสูจน์ target ไม่ได้
- Resolve reference หลังย้ายจาก owner document ตำแหน่งใหม่ เช่น material ที่ย้ายไป `Materials/` ต้องคำนวณ relative path ของ texture จากฐานที่ format นั้นใช้จริง
- Path ภายใน boundary แต่เป็น absolute reference อาจ VALID สำหรับ audit ปัจจุบัน และ FAIL สำหรับ relocation
- Search ที่พบ candidate ไม่แก้ path โดยอัตโนมัติ หากมีหลายผลลัพธ์ต้องให้ผู้ใช้เลือก
- Ignore และการปิด strict policy ไม่ทำให้ project กลายเป็น self-contained
- หาก parser ไม่ทราบ transitive dependency หรือมี access error ให้ INCONCLUSIVE เว้นแต่มีหลักฐานที่เพียงพอให้ FAIL แล้ว

Portability PASS ต้องมี dependency coverage ครบในขอบเขต format ที่รองรับ, ไม่มี dependency ที่ขาดหรืออยู่นอก export set, ไม่มีการเลือกที่ยังค้าง และทุก reference resolve ได้หลัง remap roots ไปตำแหน่งใหม่ โดยปิด fallback ไป root เดิม/ภายนอก

Relocation check ระยะแรกใช้ virtual filesystem ที่มีเฉพาะ export set; ระยะท้ายเพิ่มทดสอบสำเนาใน temp directory และตรวจโหลดใน Effekseer ผลนี้รับรอง dependency portability ภายใต้เวอร์ชันที่ทดสอบ ไม่ใช่การรับรองภาพ render ในทุก engine

## 8. ระบบแก้ไขไฟล์อย่างปลอดภัย

ค่าเริ่มต้น: Strict ON, Backup ON, Preview ON, Overwrite OFF, Preserve Folders ON, Auto-resolve ambiguous OFF

ขั้นตอน Apply:

1. สร้าง immutable plan ระบุ owner/reference, source, destination, copy/reuse และ old/new reference ให้ review ได้
2. ตรวจ conflicts, missing, unsupported rewrite และปลายทางที่หลุด boundary ก่อนเปิด Apply
3. ตรวจ hashes/metadata ของ source, owner และ destination อีกครั้ง หากเปลี่ยนหลัง preview ต้องสร้างแผนใหม่
4. สร้าง transaction journal และ verified backups ก่อนเปลี่ยนไฟล์
5. Copy และ rewrite ใน staging ให้ตำแหน่งสัมพันธ์สะท้อน final layout รักษาไฟล์ที่มีอยู่เดิม
6. Reparse/rescan staging ตรวจ mapping ครบทุก reference, resource integrity และ semantics ที่ไม่ควรเปลี่ยน
7. Commit ต่อไฟล์ด้วย atomic replacement ใน volume ที่รองรับ พร้อม journal checkpoints
8. Rescan final project และบันทึกผลสำเร็จ หากล้มเหลว rollback จาก journal

Filesystem ไม่มี atomic transaction ทั้งโปรเจกต์หลายไฟล์โดยอัตโนมัติ จึงต้องแยก atomic replacement ต่อไฟล์ออกจากการรับประกัน recovery ของทั้ง operation ทดสอบ failure ระหว่าง commit และ startup recovery หลังแอปหยุดกลางทาง

Rollback ลบเฉพาะไฟล์ที่ operation สร้างและยังตรงกับ hash ที่บันทึก ห้ามลบไฟล์เดิมที่ reuse หรือเขียนทับการแก้ภายหลังของผู้ใช้ ถ้า restore ไม่ได้ ให้สถานะ Recovery Required พร้อมข้อมูลกู้คืน ไม่รายงานว่างานสำเร็จ

เสนอให้ Consolidate เริ่มด้วย output folder ใหม่เพื่อลดความซับซ้อนของการแก้ in-place; Pull External ที่แก้ project เดิมต้องผ่าน transaction tests เช่นเดียวกัน

## 9. แผนพัฒนาเป็นระยะ

เวลาเป็นประมาณการสำหรับผู้พัฒนา 1 คนทำเต็มเวลา ต้องประเมินใหม่หลัง Phase 0 และไม่รวมเวลารอไฟล์ตัวอย่างหรือการแก้ปัญหา API ที่ยังไม่ทราบ

| ระยะ | งานหลัก | ผลส่งมอบ | เกณฑ์ผ่าน | ประมาณ |
|---|---|---|---|---|
| 0 — Format investigation | หา fixtures จริง, pin upstream revision, ตรวจ INFO/EDIT/BIN_, material, resolution, API/runtime/license | Format findings, support matrix, inspector prototype และ rewrite decision | อ่าน reference ตรงกับ editor และมีแนวทาง writer ที่พิสูจน์ได้สำหรับเวอร์ชันแรก | 3–5 วัน |
| 1 — Read-only audit MVP | Solution, Core graph/resolver/boundary, read adapters, basic nested support, WPF scan/table/detail, search candidates, preview replacement, progress/cancel และ portability coverage | แอปตรวจ resource โดยไม่แก้ source | Scenario A–E/G และ absolute-internal/cycle/unsupported tests ให้ผลถูกต้อง | 8–12 วัน |
| 2 — Safe repair MVP | Planner, collisions, preview, backup/journal/staging, safe writer, Pull External, Apply replacement, rollback/recovery และ validation | แอป repair ได้สำหรับ format ที่ผ่าน writer gate | Scenario F/87 ผ่าน และ failure/cancel ไม่ทิ้งไฟล์เสียหรือผลสำเร็จเท็จ | 10–15 วัน |
| 3 — Project consolidation | Folder/project-wide scan, shared graph, batch, tree view, preserve subfolders, configurable mapping และ consolidate output | ย้ายทั้ง project หลาย effect เป็นชุดเดียว | ทุก owner ชี้ปลายทางถูก, reference count ถูก, collision ไม่มี overwrite | 5–8 วัน |
| 4 — Release features | History/Undo UI, reports, settings/recent, manifest, shared roots, advanced duplicate และ temp move check | Release candidate พร้อมคู่มือและ package | real-project verification, recovery/undo checks และ clean-machine smoke test ผ่าน | 5–8 วัน |

รวมประมาณ 31–48 วันทำงาน หรือราว 6–10 สัปดาห์ ส่วน writer อาจใช้เวลามากกว่านี้หาก official API ใช้ไม่ได้หรือ format เป้าหมายหลากหลาย ไม่เปิด rewrite ให้เวอร์ชันที่ยังไม่ผ่าน compatibility gate

ลำดับ critical path: Fixtures/format → Parser/resolver → Complete graph/audit → Planner + safe writer + transaction → Repair acceptance → Project consolidation → Release

## 10. Backlog เริ่มต้น

| ID | งาน | ขึ้นกับ | Definition of Done |
|---|---|---|---|
| P0-01 | จัดชุดตัวอย่างจากเวอร์ชันที่ใช้งานจริง | ไม่มี | มี internal/absolute/external/missing/material และ expected references ที่ยืนยันจาก editor |
| P0-02 | ตรวจ container และ metadata/runtime versions | P0-01 | format notes + fixture matrix และ invalid/truncated samples |
| P0-03 | พิสูจน์ path resolution ของ effect/material | P0-01 | fixture ที่แยก effect-dir, material-dir และ working-dir พร้อมผลจาก editor |
| P0-04 | ทดลอง official API load/save/export | P0-02 | รายงานความเข้ากันได้, non-path semantic comparison, runtime consistency และ license |
| P0-05 | เลือก API adapter หรือ structured writer | P0-03/04 | decision record ระบุ supported versions และ rewrite gate |
| P1-01 | สร้าง solution/Core models/test harness | P0-05 | build ได้และ Core ไม่ผูก UI |
| P1-02 | ทำ resolver + boundary policy | P0-03, P1-01 | tests สำหรับ path edge cases และ physical-target uncertainty |
| P1-03 | ทำ read parser + recursive scanner | P0-02, P1-01/02 | references มี owner/locator, cycle handling และ completeness |
| P1-04 | ทำ scan UI + detail/filter/search | P1-03 | เปิด/ลากไฟล์, progress/cancel และ diagnostics ใช้ได้ |
| P1-05 | ทำ missing search + candidate selection | P1-03 | index เฉพาะ root ที่เลือก, ambiguity ไม่ถูก auto-select |
| P1-06 | ทำ audit/relocation validator | P1-03 | absolute internal และ nested external ไม่ได้ PASS |
| P2-01 | ทำ operation plan และ conflict preview | P1-05/06 | mapping ต่อ reference และ deterministic destination |
| P2-02 | ทำ backup/journal/staging/recovery | P2-01 | failure injection และ startup recovery ผ่าน |
| P2-03 | ทำ safe writer และ semantic validator | P0-05, P2-01 | editor/runtime refs สอดคล้องและ non-path values ถูกเก็บ |
| P2-04 | เปิด Apply/Pull External/Replace | P2-02/03 | Scenario 87 ผ่านครบ scan → preview → apply → move |

ไม่มี prerequisite สำหรับการลงมืออ่านและวิจัยซอร์สเพิ่มเติม แต่การปิด Phase 0 และรับรอง format เป้าหมายต้องมีไฟล์จริงและ Effekseer รุ่นที่สร้างไฟล์เหล่านั้น

## 11. แผนทดสอบ

### Core tests

- Scenario A: relative internal → VALID และ PASS เมื่อ graph ครบ
- Scenario B: absolute external ที่มีจริง → EXTERNAL และ FAIL
- Scenario C: path หาย → MISSING และ FAIL
- Scenario D: candidate หลายตัว → AMBIGUOUS และไม่มี rewrite
- Scenario E: ชื่อซ้ำ/hash เท่ากัน → identical duplicate; hash ต่าง → name collision
- Scenario F: pull → relative rewrite → rescan VALID
- Scenario G: material อ้าง external texture → project FAIL แม้ direct references อยู่ภายใน
- Absolute reference ภายใน boundary → audit internal แต่ relocation FAIL
- Prefix trap `Bloom`/`Bloom2`, Unicode, dot segments, UNC, case, drive-relative และ link ที่ออกนอก root
- Multiple owners, relative text เดียวกันคนละ base, diamond graph และ cycles
- Unsupported/truncated format, inaccessible folder และ unresolved nested format → error/coverage ที่ชัดเจน

### Integration tests สำหรับ writer/transaction

- No-op round-trip เทียบ semantic data; ไม่บังคับ byte equality หาก exporter มีผลต่างที่อธิบายได้
- Path rewrite ตรวจ editor metadata, dependency metadata และ runtime ตาม format ที่รองรับ พร้อมเปิดใน Effekseer
- Validate ใน final layout หรือ staging ที่มีโครงสร้างเทียบเท่า ไม่ตรวจเฉพาะไฟล์เดี่ยวที่ temp path ผิดฐาน
- Source เปลี่ยนหลัง preview, destination ชน, file locked, permission denied, disk full, cancel และ app interruption
- Backup hash ตรง original; rollback เก็บ reused file; restore failure แจ้ง Recovery Required
- Undo เจอไฟล์ที่ถูกแก้ภายหลังต้องไม่เขียนทับเงียบ ๆ
- ย้ายสำเนาไป temp root โดยไม่อนุญาต fallback ถึง source เดิม แล้วตรวจ closure อีกครั้ง

### UI และประสิทธิภาพ

- ตรวจ drag/drop, scan/filter/detail, candidate choice, preview/apply และ progress/cancel ด้วย workflow จริง
- UI ไม่ค้างเมื่อ scan/search/hash/copy; debounce search และจำกัด concurrency
- Search index สร้างครั้งเดียวต่อ scope แทนค้นทั้ง root ใหม่ต่อ missing resource
- Hash แบบ lazy และ cache โดยมี invalidation; ก่อน Apply ตรวจข้อมูลสำคัญใหม่
- บันทึก benchmark จาก fixture เล็ก/กลาง/ใหญ่ใน Phase 1 ก่อนตั้ง SLA ห้ามระบุตัวเลขความเร็วที่ยังไม่วัด

## 12. UX ที่ต้องได้ในแต่ละ release

Read-only MVP: เลือก effect/root → Scan → ดู VALID/EXTERNAL/MISSING → ดู reference owner → Find Missing → เลือก candidate → Preview → Test Portability พร้อมเหตุผลและ coverage

Repair MVP: เพิ่ม Pull External/Replace → Preview จำนวน copy/rewrite/conflict → Apply → แสดง validation summary และ backup/operation id ปุ่ม Apply ใช้ได้เฉพาะแผนที่ไม่มี unresolved conflict และมี writer รองรับ

หน้าหลักใช้ Flat Table ก่อน ส่วน Dependency Tree เพิ่มเมื่อทำ project workflow รายละเอียดที่ควรเห็นทันทีคือ original reference, resolved path, boundary, owner และสาเหตุของสถานะ ใช้ icon + text ไม่พึ่งสีอย่างเดียว

## 13. ความเสี่ยงและวิธีควบคุม

| ความเสี่ยง | วิธีควบคุม |
|---|---|
| Format/API ต่างรุ่น | Pin revisions, support matrix, version adapters, ปิด writer เมื่อยังไม่รับรอง |
| Editor/runtime refs ไม่ตรงกัน | ใช้ official save/export ถ้าพิสูจน์ได้ หรือ structured writer ที่ validate ทุกส่วน |
| Material มี dependency ที่ยังไม่รู้ | Recursive parser + completeness; ไม่ให้ Portable PASS |
| Backup แล้วแต่ operation หลายไฟล์หยุดกลางทาง | Journal checkpoints + recovery tests และ output folder option |
| Filename collision/preserve structure ต่าง drive | ให้ preview ระบุ anchor/layout ชัดเจน; hash/reuse หรือให้ผู้ใช้แก้ชื่อ ห้าม overwrite |
| Shared roots/ignore ทำให้เข้าใจผิด | แยก policy-valid, self-contained และ relocation result |
| Whole-drive scan ใช้เวลามาก/เข้าไม่ได้ | Explicit user-selected scope, cancellation และรายงาน skipped entries |
| Distribution ของ Effekseer libraries | ตรวจ license/runtime/native dependencies ใน Phase 0 และบันทึก provenance |

## 14. เกณฑ์พร้อมส่งมอบ

1. Supported format/version matrix มีหลักฐานจาก fixtures และ editor ที่ระบุรุ่น
2. Scenario 87 สำเร็จครบ รวมการย้าย project และตรวจโดยไม่ใช้ resource จาก root เดิม
3. ไม่มี Portable PASS เมื่อ audit ไม่ครบ, nested unresolved หรือ absolute reference ผูกตำแหน่งเดิม
4. ทุก mutation มี preview, verified backup, validation และ recovery ที่ผ่าน tests
5. Build/test/publish ทำซ้ำได้; ทดสอบ package บน Windows สะอาดที่ไม่มี SDK พร้อมระบุ runtime/native requirements
6. ส่ง source, README, architecture, known limitations, licensed test project, tests และ build/publish instructions
7. Release รุ่นแรก generic ไม่ผูก Minecraft; export profiles เป็น extension ภายหลัง

ขั้นต่อไปที่เหมาะสมคือ Phase 0: ทำ read-only format inspector และชุด fixture ที่ยืนยัน reference/resolution ได้ แล้วจึงเริ่ม audit MVP ตาม baseline ที่ตัดสินจากผลวิจัย
