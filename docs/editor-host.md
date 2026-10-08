# เริ่มใช้งานตัวแก้ Node: Effekseer 1.80.7 / 1.70e

สถานะ 8 ตุลาคม 2026: ลงมือทำขั้นแรกแล้ว เป็น **ตัวทำงานกับไฟล์แยก process** ใช้ Core และ exporter จากโปรแกรม Effekseer ที่ติดตั้งจริง มีคำสั่งผ่าน PowerShell และ JSON stdio ยังไม่เชื่อมหน้าต่าง Editor ที่เปิดอยู่, Preview หรือ AI และยังไม่มีปุ่มใหม่ในหน้า WPF

เริ่มจากเครื่องใหม่หรือเพิ่ง clone ให้ติดตั้ง SDK/PowerShell และดาวน์โหลด Editor ตาม [คู่มือติดตั้ง](installation.md) ก่อน ตัว host ไม่ได้รวมอยู่ใน Git ต้อง build ด้วย DLL ของ Editor รุ่นที่เลือก

## ความสามารถที่ทำแล้ว

- สร้างโปรเจกต์เริ่มต้นหรือเปิด `.efkefc` ที่ ToolVersion ตรงกับ host
- อ่านต้นไม้ Node พร้อมชื่อ renderer, โหมดสี/ขนาด, material และ ID ของ session
- เลือก Node ภายใน host, สร้าง Node ลูก, เปลี่ยนชื่อ
- แก้สี Fixed ของ Sprite/Model ที่ใช้ material แบบ built-in และช่องสี RGBA
- แก้ขนาด Fixed ที่ไม่มี dynamic equation; การรับค่าของ Node ลูกยังเป็นไปตามกฎเดิมของ Effekseer
- ตรวจทั้งแผนก่อนแก้ และรวมหนึ่งแผนเป็นหนึ่ง Undo พร้อม Redo
- บันทึกเป็นไฟล์ใหม่ผ่าน exporter จริง ซึ่งสร้าง EDIT/INFO/BIN_ ใหม่
- อ่านกลับด้วย Core รุ่นเดียวกัน และเทียบ XML ของ project ก่อนเผยแพร่ไฟล์
- ตรวจ session/revision และ hash ต้นฉบับ ป้องกันคำสั่งเก่าและไฟล์ที่ถูกแก้จากภายนอก

การแก้สี Random/Easing/F-Curve/Gradient แบบทั่วไป, material graph, วิถีการพุ่งและหาง ยังต้องเพิ่ม binding แยกกัน ปัจจุบันเพิ่ม recipe พายุสำหรับ 1.80.7 แล้ว ซึ่งตั้งสีสุ่ม/แรงหมุน/อัตราปล่อยผ่าน typed recipe โดยเฉพาะ และทดลองเรนเดอร์ PNG/GIF ด้วย native renderer สำเร็จ ยังไม่มี Preview ใน UI หรือใน protocol ของ host

## สร้างพายุหมุน (1.80.7)

```powershell
./scripts/create-tornado.ps1 -OutputDirectory 'D:\Minecraft\effekseer\My-New-Tornado'
```

ต้องเลือกโฟลเดอร์ใหม่ สคริปต์สร้าง Texture แบบ procedural 3 ภาพ และ `Raging-Tornado.efkefc` พร้อมตรวจ Undo/Redo และบันทึกอ่านกลับ Recipe มีเมฆกรวย 8 ชั้น เส้นลม 4 ชั้น ฝุ่น เศษวัสดุ และประกาย โดยใช้ Vortex รอบแกน Y ร่วมกับความเร็วขึ้นด้านบน ปล่อยอนุภาคประมาณ 6 วินาทีแล้วสลาย

คำสั่ง protocol `recipe.tornado` เป็น operation ใน `apply`, ใช้ `nodeId` ของ root กับ `textureDirectory` ที่มี cloud.png/wind.png/spark.png และเพิ่มกลุ่มใหม่ให้ project รองรับเฉพาะ 1.80.7 ส่วน `node.setRendered` ใช้ boolean `value` และ `new` รับ `durationFrames` 1–3600 ได้

การตรวจ roundtrip ยอมรับความคลาดเคลื่อนไม่เกิน 0.000001 เฉพาะค่าทศนิยม Center/Min/Max เนื่องจาก official loader คำนวณค่ากลางใหม่ ค่าอื่นและโครงสร้างต้องตรงกัน มี regression tests ที่ปฏิเสธการเปลี่ยนชื่อ path โครงสร้าง และค่าที่เกิน tolerance

## เตรียมตัวทำงาน

ใช้ Windows x64, PowerShell 7, .NET SDK 10 และโปรแกรม Effekseer รุ่นที่ต้องการ โดย 1.70e ใช้ .NET Framework 4.8 ส่วน host 1.80.7 ใช้ .NET 10 โหลด Core .NET 9 ของ release นี้ การทดสอบนี้ยังไม่ใช่การรับรอง runtime combination อื่น

สั่งจาก root ของ repository:

```powershell
./scripts/build-editor-host.ps1 -Version 1.80.7 -EditorDirectory 'D:\Minecraft\Studio\Effekseer1.80.7Win'
./scripts/build-editor-host.ps1 -Version 1.70e -EditorDirectory 'D:\Minecraft\effekseer\Effekseer170eWin'
```

ระบบค้นโฟลเดอร์ Tool/bin ให้อัตโนมัติ และสร้างผลลัพธ์ใน `release/editor-host/<version>` พร้อม profile/hash ของ Core ที่ใช้ ไม่แก้ไฟล์ในโฟลเดอร์ติดตั้ง Editor ไม่รวม host ใน solution หลัก เพราะต้องมี DLL ของแต่ละ release อยู่ในเครื่องก่อน build ส่วน source ของ host อยู่ที่ `src/EffekseerAI.Host`

## สร้าง Effect เริ่มต้นสีม่วงขนาดเล็ก

```powershell
./scripts/edit-effect.ps1 -Version 1.80.7 -Create `
  -Color '#A032FF' -Scale 0.5 -Name 'ม่วงเล็ก' `
  -OutputPath 'D:\Minecraft\effekseer\purple-test.efkefc'
```

เปลี่ยน Version เป็น `1.70e` เพื่อสร้างด้วย exporter รุ่นนั้น โฟลเดอร์ปลายทางต้องมีอยู่แล้ว และชื่อไฟล์ต้องยังไม่มี นี่คือ Node Sprite เริ่มต้นของ Editor ไม่ใช่สูตรเอฟเฟกต์พายุหรือหาง

## ตรวจและแก้ไฟล์เดิม

```powershell
./scripts/edit-effect.ps1 -Version 1.80.7 `
  -InputPath 'D:\Minecraft\effekseer\purple-test.efkefc' -Inspect

./scripts/edit-effect.ps1 -Version 1.80.7 `
  -InputPath 'D:\Minecraft\effekseer\purple-test.efkefc' `
  -NodePath '0' -Color '#FF8000' -Scale 0.25 `
  -OutputPath 'D:\Minecraft\effekseer\orange-test.efkefc'
```

ผล Inspect แสดง `path`: `root` คือราก, `0` คือ Node แรก, `0/1` คือลูกลำดับที่สองของ Node แรก และแสดง `sourceHash` ซึ่งส่งเป็น `-ExpectedSha256` ในคำสั่งแก้เพื่อยืนยันว่าไฟล์ยังเป็นฉบับที่ตรวจไว้ได้ Path เป็นตำแหน่งในต้นไม้ ณ เวลาที่อ่าน ส่วน ID ที่ใช้แก้จริงถูกสร้างใหม่ต่อ session และตรวจ revision เสมอ

`-Scale` ตั้งค่า XYZ เท่ากันแบบ absolute เช่น 0.5 ไม่ใช่คูณขนาดเดิมด้วย 0.5 `-Color '#RRGGBB'` รักษา alpha เดิม หรือใช้ `#RRGGBBAA` เพื่อระบุ alpha ด้วย ผลลัพธ์บอก absolute path และ SHA-256 ของไฟล์ที่สร้าง

ไฟล์ต้องมี ToolVersion ตรงกับ host แบบ exact ในระยะนี้ ตัวอย่างที่มากับชุด 1.70e บางไฟล์ยังบันทึกเป็น 1.62 หรือ 1.70α4 และจะถูกปฏิเสธด้วย `VersionMismatch` ยังไม่มีการย้ายเวอร์ชันอัตโนมัติ

การ Save As จะอ้างอิง Asset เดิมตามพฤติกรรม official exporter ไม่ได้คัดลอก Texture/Material/Model ให้ ใช้ระบบจัดชุด Asset เดิมเมื่อต้องการย้ายแพ็กไปเครื่องอื่น

## สัญญาสำหรับ UI / AI ที่จะต่อเพิ่ม

ส่ง JSON หนึ่ง object ต่อบรรทัดทาง stdin และรับหนึ่ง response ต่อบรรทัดทาง stdout; diagnostics ของ Core ไป stderr มีคำสั่ง `capabilities`, `new`, `open`, `snapshot`, `select`, `apply`, `undo`, `redo`, `saveAs` ปิด stdin เพื่อจบ process

```json
{"id":"read-1","method":"capabilities"}
```

หลัง `new` หรือ `open` อ่าน `sessionId`, `revision` และ `nodes[].id` จาก snapshot แล้วส่ง:

```json
{
  "id": "edit-1",
  "method": "apply",
  "sessionId": "<sessionId จาก snapshot>",
  "revision": 0,
  "operations": [
    {"kind":"color.setFixed","nodeId":"<id จาก snapshot>","rgba":[160,50,255,255]},
    {"kind":"scale.setFixed","nodeId":"<id จาก snapshot>","xyz":[0.5,0.5,0.5]}
  ]
}
```

ตัวอย่างจัดย่อหน้าเพื่ออ่านง่าย เมื่อส่งจริงต้องอยู่บรรทัดเดียว ใช้ `scripts/editor-host-client.psm1` เพื่อจัดการ process, UTF-8, timeout และ JSON ได้ `node.create` ใช้ nodeId ของ parent และ `name`; `node.rename` ใช้ nodeId เป้าหมายและ `name`

- เก็บ snapshot ล่าสุดทุกครั้ง; `select` ก็เพิ่ม revision
- คำสั่งที่เปลี่ยน project ต้องมี session/revision รวมถึง `open`/`new` เมื่อมี project อยู่แล้ว
- ID ไม่ใช่ชื่อหรือ index ที่ AI เดาเอง และหมดอายุเมื่อเปิด/สร้าง/บันทึกอ่านกลับ
- Save As ตรวจ roundtrip โดย reload จึงเริ่ม session ใหม่และล้าง Undo; response ระบุ `undoHistoryReset=true`
- หลัง native setter/export/load ล้มเหลว session อาจถูกปิดการแก้ ต้องเปิดใหม่; ไม่มีการรับรอง transaction rollback ของ native Core
- ไม่มี network listener และไม่มีการรันโค้ดตามข้อความ AI
- loader `.efkefc` เป็น internal ใน upstream ทั้งสองรุ่น จึงมี reflection แบบอ่านอย่างเดียวที่ชื่อ type/method คงที่: 1.70e คืน XmlDocument, 1.80.7 คืน bool และอ่าน field EditorData; setters เป็น typed public API
- หากต้องการแก้ state ในหน้าต่าง Effekseer จริง ต้องเพิ่ม dispatcher/bridge ที่ทำงานบน Editor thread พร้อมพิสูจน์ selection และ Preview อีกขั้น

## หลักฐานการทดสอบ

```powershell
./scripts/test-editor-host.ps1 -Version 1.70e
./scripts/test-editor-host.ps1 -Version 1.80.7
```

เพิ่ม `-SamplePath` เพื่อทดสอบไฟล์ ToolVersion ตรงกัน และ `-MismatchedSamplePath` เพื่อทดสอบปฏิเสธไฟล์ข้ามเวอร์ชัน ผลเก็บใน `artifacts/editor-host-tests/<version>-<id>/result.json`

รอบตรวจล่าสุด: **55 checks ผ่าน** (1.70e: 27, 1.80.7: 28) ครอบคลุมสร้าง/แก้/Undo/Redo, ภาษาไทย, plan validation, revision conflict, save/reload, runtime BIN_ เปลี่ยนตามค่าที่แก้, ต้นฉบับไม่เปลี่ยน, ปฏิเสธเขียนทับ/ข้ามเวอร์ชัน/custom material สี โดย 1.80.7 ทดสอบ roundtrip เพิ่มกับ Mega ของผู้ใช้ ส่วน 1.70e ทดสอบกับ fixture ที่สร้างด้วย Core 1.70e จริง ยังไม่ได้ทดสอบ render หรือเปิดผ่าน GUI

## งานถัดไป

1. เชื่อม Editor thread/selection/Preview ของสองรุ่น และพิสูจน์ Undo ที่ร่วมกับการแก้จากผู้ใช้
2. เพิ่ม position/velocity/rotation/spawn/lifetime แล้วทำ recipe พายุ กระจาย และ projectile + trail
3. เพิ่มสีและขนาดโหมดอื่น พร้อม parameter schema รายรุ่น
4. เชื่อม UI แสดงแผนและ AI planner หลัง executor/Preview พร้อม

ดูขอบเขตเต็มใน [architecture](ai-effekseer-architecture.md) และ source upstream ของ [1.80.7](https://github.com/effekseer/Effekseer/tree/1807/Dev/Editor/EffekseerCore) กับ [1.70e](https://github.com/effekseer/Effekseer/tree/170e/Dev/Editor/EffekseerCore)
