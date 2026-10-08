# MASTER PROMPT — Effekseer Resource & Path Manager

ให้พัฒนา Desktop Application สำหรับ Windows ชื่อชั่วคราวว่า:

**Effekseer Resource & Path Manager**

จุดประสงค์ของโปรแกรมคือช่วยตรวจสอบ จัดการ ย้าย รวม และซ่อม Resource Dependency ของโปรเจกต์ Effekseer โดยเฉพาะปัญหาที่ Effect สามารถแสดงผลใน Effekseer ได้ตามปกติ แต่จริง ๆ แล้ว Resource บางส่วน เช่น Texture / Material / Model / Sound ถูกอ้างอิงจาก path เก่าหรือโฟลเดอร์อื่นในเครื่อง ทำให้เมื่อนำ Effect ไปใช้ในโปรเจกต์อื่น ย้ายเครื่อง ส่งให้ผู้อื่น หรือใช้กับ Minecraft Mod แล้ว Resource หายหรือ path พัง

โปรแกรมต้องสามารถทำให้ Effect กลายเป็น **Self-Contained / Portable Project** ได้

---

# 1. เป้าหมายหลักของโปรแกรม

โปรแกรมต้องสามารถ:

1. เปิดไฟล์หรือโปรเจกต์ Effekseer
2. ตรวจสอบ Resource Dependency ทั้งหมดที่ Effect ใช้
3. แสดง path จริงของ Resource แต่ละตัว
4. กำหนดขอบเขตหรือ `Resource Boundary`
5. ตรวจว่า Resource แต่ละตัวอยู่ภายในหรือภายนอก Boundary
6. ตรวจ Resource ที่หาย
7. ตรวจ Resource ที่มีชื่อซ้ำหรือไม่สามารถตัดสินใจได้ว่าควรใช้ตัวไหน
8. ค้นหา Resource ที่ถูกย้ายไปแล้ว
9. ดึง Resource ภายนอกเข้ามาไว้ใน Project
10. Rewrite path ให้ Effect ชี้มายัง Resource ใหม่
11. จัดระเบียบ Resource
12. Validate หลังแก้ไข
13. ทดสอบว่า Project สามารถย้ายไปที่อื่นได้หรือไม่
14. ห้ามแก้ Source ต้นฉบับโดยไม่แจ้งผู้ใช้
15. รองรับ Dry Run / Preview ก่อนแก้ไขจริง

เป้าหมายสุดท้ายคือ:

```text
Effekseer Effect
        ↓
Scan Dependencies
        ↓
Audit Paths
        ↓
Detect External Resources
        ↓
Pull Into Project
        ↓
Rewrite Paths
        ↓
Validate
        ↓
Portable / Self-Contained Effect
```

---

# 2. Technology Stack

แนะนำให้พัฒนาเป็น:

```text
Language: C#
Runtime: .NET 8
UI: WPF
Platform: Windows
Architecture: MVVM
```

เหตุผล:

- ทำ Windows Desktop Application ได้ง่าย
- File System API ดี
- Drag & Drop ทำได้สะดวก
- Publish เป็น `.exe` ได้
- ไม่ผูกกับ Minecraft
- ไม่ผูกกับ Gradle
- ใช้กับ Effekseer Project ใดก็ได้

อย่างไรก็ตามต้องแยก Core Logic ออกจาก UI อย่างชัดเจน เพื่อให้อนาคตสามารถเพิ่ม CLI หรือเปลี่ยน UI Framework ได้

ตัวอย่างโครงสร้าง:

```text
EffekseerResourceManager/
│
├─ App/
│
├─ UI/
│   ├─ Views/
│   ├─ ViewModels/
│   └─ Controls/
│
├─ Core/
│   ├─ Scanning/
│   ├─ Dependencies/
│   ├─ Paths/
│   ├─ Validation/
│   ├─ Relocation/
│   └─ Portability/
│
├─ Parsers/
│   ├─ IEffekseerEffectParser.cs
│   ├─ EfkefcParser.cs
│   ├─ EfkmatParser.cs
│   └─ PackageParser.cs
│
├─ Models/
│
├─ Services/
│
├─ Infrastructure/
│
└─ Tests/
```

---

# 3. หลักการสำคัญ

ระบบต้องแยกระหว่างคำว่า:

```text
Resolved
```

กับ

```text
Valid
```

อย่างชัดเจน

ตัวอย่าง:

Effect อ้าง:

```text
D:\OldProject\Common\smoke.png
```

และไฟล์นั้นยังอยู่จริง

Effekseer อาจแสดง:

```text
FOUND
```

แต่ถ้า Project ที่กำลังตรวจคือ:

```text
D:\CurrentProject\FireSpell\
```

ระบบต้องถือ Resource นี้ว่า:

```text
Resolved: YES
Valid: NO
Status: EXTERNAL
```

เพราะ Resource อยู่นอก Project Boundary

นี่คือหนึ่งใน Requirement ที่สำคัญที่สุดของโปรแกรม

---

# 4. Resource Boundary

ผู้ใช้ต้องสามารถกำหนดขอบเขต Resource ได้

ตัวอย่าง:

```text
Allowed Root:

D:\Effects\RapturousBloom\
```

Resource ภายใน:

```text
D:\Effects\RapturousBloom\Textures\petal.png
```

ถือว่า:

```text
VALID
```

Resource:

```text
D:\OldProject\Textures\smoke.png
```

ถือว่า:

```text
EXTERNAL
```

ถึงแม้ไฟล์จะยังอยู่และ Effect โหลดสำเร็จก็ตาม

---

# 5. Scan Boundary Modes

ให้มี Scan Mode อย่างน้อย:

```text
1. Effect Folder
2. Custom Root
3. Search Root
4. Entire Drive / Recovery Mode
```

## Effect Folder

ใช้ Folder ที่ไฟล์ Effect อยู่เป็น Allowed Root

ตัวอย่าง:

```text
D:\Effects\Bloom\bloom.efkefc
```

Allowed Root:

```text
D:\Effects\Bloom\
```

เหมาะกับการตรวจ portability

---

## Custom Root

ผู้ใช้เลือก root เอง

ตัวอย่าง:

```text
D:\MinecraftFX\
```

อนุญาต Resource ทุกตัวใต้:

```text
D:\MinecraftFX\
```

เช่น:

```text
D:\MinecraftFX\Common\
D:\MinecraftFX\Textures\
D:\MinecraftFX\Bloom\
```

---

## Search Root

เป็นพื้นที่สำหรับค้นหา Resource ที่หาย

เช่น:

```text
D:\EffekseerResources\
```

ไม่ได้หมายความว่าทุก Resource ในนั้นถือว่า Valid

แต่ใช้เพื่อค้นหา Missing Resource

---

## Entire Drive / Recovery Mode

ใช้เฉพาะกรณีต้องตามหา Resource เก่าที่กระจายอยู่ทั่วเครื่อง

ต้องไม่ Scan ทั้ง Drive โดยอัตโนมัติ

ต้องให้ผู้ใช้เลือกก่อน เพราะอาจใช้เวลามาก

---

# 6. Strict Project Boundary

เพิ่ม Option:

```text
☑ Strict Project Boundary
```

ถ้าเปิด:

Resource ทุกชนิดที่อยู่นอก Allowed Root ต้องถูก Flag ทันที

แม้ว่า:

```text
File Exists = true
```

ก็ยังต้องถือว่า:

```text
EXTERNAL
```

ตัวอย่าง:

```text
Allowed Root:

D:\Effects\FireDragon\
```

พบ:

```text
C:\Users\User\Downloads\particle.png
```

ผล:

```text
Status: EXTERNAL
Portable: NO
```

---

# 7. Resource Types

รองรับ Resource หลักของ Effekseer เช่น:

```text
Texture
Material
Model
Sound
Effect Dependency
Curve
Custom Material
Other referenced files
```

ห้าม hardcode เฉพาะ `.png`

Texture อาจเป็น format อื่น

Sound อาจเป็น format อื่น

ระบบควรออกแบบ Resource Type ให้ extend ได้ในอนาคต

เช่น:

```csharp
enum ResourceType
{
    Texture,
    Material,
    Model,
    Sound,
    Effect,
    Unknown
}
```

---

# 8. Dependency Scanner

ระบบต้องสามารถสร้าง Dependency Graph

ตัวอย่าง:

```text
Bloom.efkefc
│
├─ Texture
│   ├─ petal.png
│   └─ glow.png
│
├─ Material
│   └─ water.efkmat
│       ├─ noise.png
│       └─ distortion.png
│
└─ Model
    └─ flower.efkmodel
```

ต้องไม่ตรวจเฉพาะ Resource ที่ `.efkefc` อ้างตรง ๆ

ถ้า Material อ้าง Texture เพิ่ม ต้องตามต่อ

ถ้า Resource บางประเภทสามารถอ้าง Dependency ซ้อน ต้องสามารถรองรับ Nested Dependency

---

# 9. Resource Model

Resource แต่ละตัวควรเก็บข้อมูลอย่างน้อย:

```text
Id
Type
OriginalReference
ResolvedPath
CanonicalPath
FileName
Extension
Exists
InsideBoundary
Status
ReferencedBy
DependencyDepth
FileSize
Hash
LastModified
ProposedPath
ProposedAction
```

ตัวอย่าง:

```text
Type:
Texture

Reference:
../../OldFX/Common/smoke.png

Resolved:
D:\OldFX\Common\smoke.png

Exists:
true

InsideBoundary:
false

Status:
EXTERNAL
```

---

# 10. Resource Status

ให้มี Status อย่างน้อย:

```text
VALID
EXTERNAL
MISSING
AMBIGUOUS
REDIRECTED
DUPLICATE
UNRESOLVED
ERROR
```

ความหมาย:

### VALID

```text
ไฟล์มีอยู่
+
อยู่ภายใน Boundary
```

### EXTERNAL

```text
ไฟล์มีอยู่
แต่
อยู่นอก Boundary
```

### MISSING

```text
path เดิมหาไฟล์ไม่พบ
```

### AMBIGUOUS

เจอไฟล์ชื่อเดียวกันมากกว่า 1 ตัวและไม่สามารถตัดสินใจได้

### REDIRECTED

ผู้ใช้เลือก Resource ใหม่แทน path เก่า

### DUPLICATE

พบไฟล์เหมือนกันหลายตำแหน่ง

### UNRESOLVED

Parser พบ reference แต่ยังไม่สามารถ resolve path ได้

### ERROR

เกิดข้อผิดพลาดในการ parse หรือ access

---

# 11. Resource Audit UI

หน้าหลักควรประมาณ:

```text
┌──────────────────────────────────────────────────┐
│ Effekseer Resource & Path Manager                │
├──────────────────────────────────────────────────┤
│ Effect                                           │
│ [ D:\Effects\Bloom\Bloom.efkefc ] [Browse]       │
│                                                  │
│ Allowed Root                                     │
│ [ D:\Effects\Bloom\ ]             [Browse]       │
│                                                  │
│ Search Root                                      │
│ [ D:\EffekseerResources\ ]        [Browse]       │
│                                                  │
│ ☑ Strict Project Boundary                        │
│                                                  │
│ [ Scan Project ]                                 │
├──────────────────────────────────────────────────┤
│ Resource            Type       Status             │
│ petal.png           Texture    ✓ VALID            │
│ glow.png            Texture    ✓ VALID            │
│ smoke.png           Texture    ⚠ EXTERNAL         │
│ noise.png           Texture    ✕ MISSING          │
│ water.efkmat        Material   ✓ VALID            │
├──────────────────────────────────────────────────┤
│ Valid: 21                                        │
│ External: 3                                     │
│ Missing: 1                                      │
│ Ambiguous: 0                                    │
├──────────────────────────────────────────────────┤
│ [Find Missing] [Pull External] [Test Portability]│
└──────────────────────────────────────────────────┘
```

---

# 12. Resource Detail Panel

เมื่อกด Resource ให้แสดง:

```text
Resource
--------------------------------

Type:
Texture

Reference:
../../OldProject/Particles/smoke.png

Resolved Path:
D:\OldProject\Particles\smoke.png

Allowed Root:
D:\Effects\Bloom\

Status:
EXTERNAL

Referenced By:
Bloom.efkefc
Node 14

File Size:
34 KB

Hash:
xxxx

Actions:

[Locate]
[Open Folder]
[Replace]
[Copy Into Project]
[Ignore]
```

---

# 13. Missing Resource Finder

เมื่อ Resource เป็น:

```text
MISSING
```

ให้สามารถกด:

```text
Find Missing Resources
```

ระบบค้นหาภายใน Search Root

ตัวอย่าง:

ต้องหา:

```text
smoke.png
```

พบ:

```text
D:\Resources\Common\smoke.png
D:\Resources\Fire\smoke.png
```

ห้ามเลือกอัตโนมัติถ้ามีหลายผลลัพธ์

แสดง:

```text
smoke.png

Candidates:

1.
D:\Resources\Common\smoke.png

2.
D:\Resources\Fire\smoke.png
```

ให้ผู้ใช้เลือกเอง

---

# 14. Smart Matching

สามารถใช้เกณฑ์เพื่อช่วยจัดอันดับ Candidate ได้

เช่น:

```text
1. Exact filename
2. Extension
3. File hash
4. File size
5. Original folder hint
6. Similar directory structure
7. Modification time
```

แต่:

**ห้าม rewrite path จากการเดาโดยอัตโนมัติถ้ามีความกำกวม**

---

# 15. Duplicate Detection

ถ้าพบ:

```text
smoke.png
```

หลายตำแหน่ง

และ Hash เหมือนกัน:

```text
SHA256 A == SHA256 B
```

สามารถระบุ:

```text
IDENTICAL DUPLICATE
```

ถ้าชื่อเหมือน แต่ Hash ต่าง:

```text
NAME COLLISION
```

ต้องแยกให้ชัด

---

# 16. Pull External Resources

นี่คือ Feature หลัก

ตัวอย่าง:

พบ:

```text
D:\OldFX\Smoke\smoke.png
```

อยู่นอก Boundary

ผู้ใช้กด:

```text
Pull Into Project
```

ระบบเสนอ:

```text
Source:

D:\OldFX\Smoke\smoke.png

Destination:

D:\Effects\Bloom\Textures\smoke.png
```

หลังยืนยัน:

```text
Copy Resource
↓
Rewrite Reference
↓
Validate
```

---

# 17. Consolidate Project

เพิ่ม Feature:

```text
Consolidate Project
```

หน้าที่คือรวบ Resource ทั้งหมดที่ Effect ใช้ให้อยู่ใน Project เดียว

ก่อน:

```text
Bloom.efkefc

D:\OldProject\Smoke\smoke.png
C:\Downloads\Pack\noise.png
D:\Shared\Materials\water.efkmat
```

หลัง Consolidate:

```text
Bloom/
│
├─ Bloom.efkefc
│
├─ Textures/
│   ├─ smoke.png
│   └─ noise.png
│
├─ Materials/
│   └─ water.efkmat
│
├─ Models/
│
└─ Sounds/
```

และ reference ทั้งหมดต้องถูก rewrite

---

# 18. Destination Rules

Default organization:

```text
Project/
├─ Textures/
├─ Materials/
├─ Models/
├─ Sounds/
└─ Effects/
```

แต่ผู้ใช้ต้องสามารถแก้ Folder Mapping ได้

ตัวอย่าง:

```text
Texture  → Resources/Textures/
Material → Resources/Materials/
Model    → Resources/Models/
Sound    → Resources/Sounds/
```

---

# 19. Filename Collision

สมมติ Source มี:

```text
Fire/smoke.png
Ice/smoke.png
```

แต่ Consolidate จะเอามาไว้:

```text
Textures/
```

จะเกิดชนกัน

ห้าม overwrite

ต้องเตือน:

```text
Filename Collision

smoke.png

Source A:
D:\Fire\smoke.png

Source B:
D:\Ice\smoke.png
```

เสนอ:

```text
Keep Folder Structure

หรือ

Rename:

fire_smoke.png
ice_smoke.png
```

ผู้ใช้เป็นคนเลือก

---

# 20. Preserve Structure Option

เพิ่ม:

```text
☑ Preserve Resource Subfolders
```

ตัวอย่าง Source:

```text
Textures/
├─ Fire/
│  └─ smoke.png
└─ Ice/
   └─ smoke.png
```

ปลายทาง:

```text
Textures/
├─ Fire/
│  └─ smoke.png
└─ Ice/
   └─ smoke.png
```

ลดปัญหา filename collision

---

# 21. Rewrite Path

ระบบต้องสามารถ rewrite reference ได้

ก่อน:

```text
../../OldFX/Common/smoke.png
```

หลัง:

```text
Textures/Common/smoke.png
```

แต่ต้องระวัง format ของ Effekseer

**ห้าม assume ว่า `.efkefc` เป็น plain text**

ต้อง Investigate Format ก่อน

---

# 22. Parser Architecture

สร้าง abstraction:

```csharp
public interface IEffekseerResourceParser
{
    ParseResult Parse(string filePath);

    RewriteResult Rewrite(
        string filePath,
        IReadOnlyDictionary<string, string> pathMappings);
}
```

Parser แต่ละชนิดแยกกัน

เช่น:

```text
EfkefcParser
EfkmatParser
EfkpkgParser
```

ถ้า format บางแบบไม่สามารถ rewrite ได้อย่างปลอดภัย:

```text
Read = allowed
Rewrite = disabled
```

และแจ้ง UI

ห้ามใช้ binary string replacement แบบสุ่มโดยไม่มี verification

---

# 23. สำคัญมาก: อย่าทำลายไฟล์ต้นฉบับ

ทุก Operation ที่แก้ไข Effect ต้องมี Safety

Default:

```text
Backup Original = ON
```

ตัวอย่าง:

```text
Bloom.efkefc

→

Bloom.efkefc.backup
```

หรือ:

```text
.backup/
2026-xx-xx_xxxxxx/
```

ก่อนเขียนไฟล์จริง

---

# 24. Atomic Write

ห้ามเปิดไฟล์เดิมแล้วเขียนทับทันที

ใช้:

```text
Read Original
↓
Generate Modified Copy
↓
Validate
↓
Write Temporary File
↓
Replace Original Atomically
```

หากเกิด error:

```text
Original must remain untouched
```

---

# 25. Dry Run

ทุก Operation สำคัญต้อง Preview ได้

ตัวอย่าง:

```text
DRY RUN

Project:
Bloom

Changes:

External Resources:
3

Files To Copy:
3

Path Rewrites:
5

Conflicts:
0

Missing:
0
```

แสดง mapping:

```text
OLD

../../OldFX/smoke.png

NEW

Textures/smoke.png
```

จากนั้น:

```text
[Apply Changes]
```

---

# 26. Undo / Transaction

Operation เช่น Consolidate ต้องบันทึก Transaction

ตัวอย่าง:

```text
Transaction

Copied:
Textures/smoke.png

Changed:
Bloom.efkefc

Changed:
Materials/water.efkmat
```

ให้สามารถ:

```text
Undo Last Operation
```

อย่างน้อยภายใน session

ถ้าทำได้ควรมี operation manifest

---

# 27. Portability Test

เพิ่ม Feature สำคัญ:

```text
Test Portability
```

แนวคิด:

จำลองว่าทุกไฟล์นอก Boundary ไม่มีอยู่

แล้วตรวจว่า Effect ยังครบไหม

ผล:

```text
PORTABILITY TEST

Effect:
Bloom.efkefc

Boundary:
D:\Effects\Bloom\

Total Resources:
37

Internal:
31

External:
6

Missing:
0

RESULT:

NOT PORTABLE
```

หลัง Repair:

```text
Total:
37

Internal:
37

External:
0

Missing:
0

RESULT:

PORTABLE
```

---

# 28. Self-Contained Status

Project จะถือว่า Self-Contained เมื่อ:

```text
Missing = 0
External = 0
Ambiguous = 0
Unresolved = 0
```

แสดง:

```text
✓ SELF-CONTAINED
✓ PORTABLE
✓ SAFE TO MOVE
```

---

# 29. Move Simulation

เพิ่ม Option ขั้นสูง:

```text
Simulate Project Move
```

แนวคิดคือ:

คำนวณว่า ถ้า Project ถูกย้ายจาก:

```text
D:\Effects\Bloom\
```

ไป:

```text
X:\RandomFolder\Bloom\
```

reference ยัง resolve ผ่าน relative path ภายใน Project หรือไม่

ไม่จำเป็นต้อง copy จริง

แค่ simulate path resolution

---

# 30. Drag & Drop

รองรับ Drag & Drop:

```text
.efkefc
```

หรือ Folder

ลากเข้าหน้าต่างได้เลย

เมื่อ Drop:

```text
Detect Project
↓
Set Effect
↓
Suggest Allowed Root
↓
Scan
```

---

# 31. Project Scan

ถ้าผู้ใช้ลาก Folder:

```text
Bloom/
```

ให้ค้นหาไฟล์:

```text
*.efkefc
```

แล้วแสดง Effect ที่พบ

ถ้ามีหลายไฟล์:

```text
Bloom_Main.efkefc
Bloom_Impact.efkefc
Bloom_Aura.efkefc
```

ผู้ใช้เลือก Scan:

```text
Single Effect

หรือ

Entire Project
```

---

# 32. Entire Project Scan

ถ้า Scan ทั้ง Project:

ให้สร้าง dependency union

ตัวอย่าง:

```text
Bloom_Main
 ├ smoke.png
 └ glow.png

Bloom_Impact
 ├ smoke.png
 └ spark.png
```

Project Dependency:

```text
smoke.png
glow.png
spark.png
```

ไม่ duplicate resource

---

# 33. Reference Count

แสดงด้วยว่า Resource ถูกใช้กี่ครั้ง

ตัวอย่าง:

```text
smoke.png

References:
12
```

และใช้โดย:

```text
Bloom_Main
Bloom_Impact
Bloom_Aura
```

---

# 34. Dependency Tree View

เพิ่ม View:

```text
Bloom.efkefc
│
├─ Materials
│  └─ water.efkmat
│     ├─ noise.png
│     └─ distortion.png
│
├─ Textures
│  └─ petal.png
│
└─ Models
   └─ flower.efkmodel
```

---

# 35. Flat Resource View

อีก Tab เป็น Flat Table:

```text
Name
Type
Status
Path
Referenced By
Size
```

เพื่อ sort/filter ง่าย

---

# 36. Filters

ให้ Filter:

```text
All
Valid
External
Missing
Ambiguous
Duplicate
```

และ:

```text
Texture
Material
Model
Sound
```

---

# 37. Search

Search โดย:

```text
filename
path
extension
reference
```

---

# 38. Batch Operations

เลือกหลาย Resource แล้ว:

```text
Pull Into Project
Replace
Ignore
Open Folder
Copy Path
```

---

# 39. External Resource Repair

Workflow:

```text
Scan
↓
External = 16
↓
Select All External
↓
Pull Into Project
↓
Preview Mapping
↓
Copy
↓
Rewrite
↓
Rescan
```

ผลต้องกลายเป็น:

```text
External = 0
```

ถ้าไม่มี conflict

---

# 40. Missing Resource Repair

Workflow:

```text
Missing = 4

Find Missing
↓
Search Root
↓
Candidates
↓
User Resolve
↓
Copy Into Project
↓
Rewrite
↓
Validate
```

---

# 41. Manual Replace

ผู้ใช้สามารถกำหนด Resource ใหม่เอง

ตัวอย่าง:

```text
Old:
smoke.png

Replace With:
D:\NewResources\smoke_v2.png
```

หลัง Replace:

ระบบเสนอ copy เข้า Project ก่อน

ไม่ควรชี้ไป external path ใหม่โดยไม่เตือน

---

# 42. Warning เมื่อสร้าง External Dependency ใหม่

ถ้าผู้ใช้เลือกไฟล์:

```text
D:\Downloads\smoke.png
```

แต่ Allowed Root คือ:

```text
D:\Effects\Bloom\
```

แจ้ง:

```text
This resource is outside the project boundary.

Recommended:

Copy into project before linking.
```

ปุ่ม:

```text
[Copy Into Project]
[Use External Anyway]
[Cancel]
```

---

# 43. Ignore Rules

อนุญาต Ignore บาง Resource

แต่ต้องแสดง:

```text
IGNORED EXTERNAL
```

ไม่ถือว่า Self-Contained

เว้นแต่ผู้ใช้ตั้ง Policy เอง

---

# 44. Shared Resource Roots

อนาคตอาจต้องรองรับ Allowed Root มากกว่า 1

เช่น:

```text
Project Root:
D:\Effects\Bloom\

Shared Root:
D:\Effects\Shared\
```

Resource ที่อยู่ใน Shared Root:

```text
SHARED VALID
```

ไม่ใช่ External

ดังนั้น Architecture ควรรองรับ:

```text
AllowedRoots[]
```

แม้ MVP จะเริ่มจาก 1 Root ก็ได้

---

# 45. Path Normalization

ต้องจัดการ:

```text
/
\

..

.

relative path

absolute path

drive letter

case insensitive Windows paths
```

ใช้ `Path.GetFullPath`

และ canonicalize ก่อน comparison

ต้องป้องกัน:

```text
..\..\..\outside
```

---

# 46. Symbolic Links / Junctions

ถ้าเจอ Symlink / Junction:

ต้องสามารถ detect

เพราะ path ดูเหมือนอยู่ใน Project แต่จริง ๆ อาจชี้ออกไปภายนอก

แสดง Warning:

```text
SYMLINK EXTERNAL TARGET
```

ไม่ต้องรองรับขั้นสูงใน MVP แต่ Architecture ไม่ควรปิดทาง

---

# 47. Hashing

ใช้ SHA-256 สำหรับ:

```text
Duplicate Detection
Candidate Matching
Integrity Check
```

Hash เฉพาะเมื่อจำเป็น

อย่าคำนวณทุกไฟล์ขนาดใหญ่ทันทีโดยไม่มีเหตุผล

---

# 48. Performance

ต้องไม่ block UI

งาน:

```text
Scanning
Hashing
Searching
Copying
Validation
```

ต้องทำ async

UI ต้องมี:

```text
Progress
Cancel
Current Task
```

เช่น:

```text
Scanning Resources...

412 / 1038

[Cancel]
```

---

# 49. Search Scope Safety

ห้าม Scan ทั้งเครื่องทันที

ลำดับแนะนำ:

```text
1. Current Project
2. Search Root
3. User-selected Folder
4. User-selected Drive
```

---

# 50. Logging

มี Log Panel:

```text
[10:23:01] Loaded Bloom.efkefc
[10:23:02] Found 37 dependencies
[10:23:02] External resource detected: smoke.png
[10:23:02] Missing resource detected: noise.png
```

สามารถ Export Log ได้

---

# 51. Report

สามารถ Export Resource Audit เป็น:

```text
JSON
CSV
TXT
```

ตัวอย่าง JSON:

```json
{
  "effect": "Bloom.efkefc",
  "boundary": "D:\\Effects\\Bloom",
  "portable": false,
  "resources": []
}
```

---

# 52. Settings

จำค่า:

```text
Last Project
Last Search Root
Preserve Folders
Strict Mode
Backup Mode
Destination Mapping
```

เก็บใน AppData

---

# 53. Recent Projects

หน้าเริ่มต้นมี:

```text
Recent Projects

Bloom
FireDragon
LightningStrike
PoisonMist
```

---

# 54. Project Manifest

พิจารณาสร้างไฟล์:

```text
effekseer-resources.json
```

เพื่อบันทึก:

```text
Allowed Root
Search Roots
Mappings
Ignored Files
Resource Rules
```

แต่อย่าบังคับให้ Effekseer ต้องรู้จักไฟล์นี้

เป็น metadata ของ Tool เท่านั้น

---

# 55. Parser Investigation Phase

ก่อนเขียน Rewrite Engine ต้องศึกษาไฟล์ Effekseer จริง

ทำ Tool/Internal Debug Feature:

```text
Inspect Effect File
```

เป้าหมาย:

1. ตรวจ `.efkefc`
2. ตรวจ resource references
3. ตรวจว่า format เป็น binary / text / structured format
4. ตรวจ path representation
5. ตรวจ encoding
6. ตรวจ versioning
7. ตรวจว่า Material อ้าง resource ยังไง
8. ตรวจว่ามี official library/API ที่สามารถใช้ safely ได้หรือไม่

ห้ามรีบทำ Regex/Binary replacement จนกว่าจะเข้าใจ format

---

# 56. Parser Must Be Version Aware

Resource parser ควรสามารถรายงาน:

```text
Detected Format Version
```

ถ้าไม่รองรับ:

```text
READ ONLY MODE
```

แทนที่จะพยายาม rewrite แล้วทำไฟล์พัง

---

# 57. Unknown Resource

ถ้า Parser พบ reference ที่ไม่รู้ชนิด:

```text
Unknown Resource
```

ยังต้องแสดง path และตรวจ Boundary ได้

ห้าม drop ทิ้ง

---

# 58. Source File Integrity

ก่อน Rewrite:

```text
Original Hash
```

หลัง Rewrite:

```text
New Hash
```

Backup ต้องตรงกับ Original

ถ้า Operation fail:

restore

---

# 59. Validation หลัง Rewrite

หลังทุก rewrite:

1. Parse ไฟล์ใหม่อีกครั้ง
2. สร้าง dependency graph ใหม่
3. Resolve path ใหม่
4. ตรวจว่าทุก Resource ยังพบ
5. ตรวจว่า path ใหม่ตรง Mapping
6. ตรวจว่าไฟล์ไม่ corrupt

ถ้า Validation fail:

```text
Rollback
```

---

# 60. Final Validation Summary

หลัง Consolidate:

```text
VALIDATION COMPLETE

Resources:
37

Valid:
37

External:
0

Missing:
0

Ambiguous:
0

Result:

SELF-CONTAINED
PORTABLE
```

---

# 61. Do Not Assume Effekseer Resolution Rules

ห้าม assume ว่า path ถูก resolve เทียบกับ:

```text
working directory
effect directory
application directory
```

ต้องตรวจสอบ behavior จริง

และ encapsulate logic ใน:

```text
EffekseerPathResolver
```

---

# 62. Separation of Concerns

Architecture ควรแยก:

```text
Parser
Resolver
Scanner
BoundaryValidator
SearchEngine
Relocator
Rewriter
Validator
UI
```

ห้ามรวมทุกอย่างไว้ใน MainWindow

---

# 63. Suggested Core Interfaces

ตัวอย่าง:

```csharp
IEffectParser
IResourceResolver
IResourceScanner
IBoundaryValidator
IResourceSearchService
IResourceRelocator
IReferenceRewriter
IPortabilityValidator
IBackupService
ITransactionService
```

---

# 64. Unit Tests

ต้องมี Tests สำหรับอย่างน้อย:

```text
Path normalization
Boundary detection
Relative path resolution
External detection
Missing detection
Duplicate detection
Filename collision
Mapping generation
Portability check
```

---

# 65. Test Scenario A

Project:

```text
Bloom/
├─ Bloom.efkefc
└─ Textures/
   └─ petal.png
```

Effect ใช้:

```text
Textures/petal.png
```

Result:

```text
VALID
PORTABLE
```

---

# 66. Test Scenario B

Effect ใช้:

```text
D:\OldProject\smoke.png
```

ไฟล์มีอยู่จริง

Result:

```text
EXTERNAL
NOT PORTABLE
```

สำคัญมาก:

ห้ามรายงานเป็น VALID เพียงเพราะไฟล์มีอยู่

---

# 67. Test Scenario C

Effect อ้าง:

```text
../Texture/noise.png
```

แต่ไม่มีไฟล์

Result:

```text
MISSING
```

---

# 68. Test Scenario D

Search Root มี:

```text
Common/noise.png
Fire/noise.png
```

Result:

```text
AMBIGUOUS
```

ห้าม Auto-select

---

# 69. Test Scenario E

สองไฟล์ชื่อ:

```text
noise.png
```

แต่ SHA-256 เท่ากัน

Result:

```text
IDENTICAL DUPLICATE
```

สามารถเสนอใช้ตัวใดตัวหนึ่ง

---

# 70. Test Scenario F

Consolidate External Resource

ก่อน:

```text
D:\OldFX\smoke.png
```

หลัง:

```text
Bloom\Textures\smoke.png
```

Reference ใหม่ต้องชี้ถูก

Re-scan:

```text
VALID
```

---

# 71. Test Scenario G

Material Nested Dependency

```text
Bloom.efkefc
→ water.efkmat
→ noise.png
```

ถ้า noise.png external:

Project ต้องเป็น:

```text
NOT PORTABLE
```

ถึงแม้ `.efkefc` จะไม่ได้อ้าง `noise.png` โดยตรง

---

# 72. MVP Scope

Phase 1 ให้ทำ:

```text
Open .efkefc
Drag & Drop
Select Boundary
Scan Resource
Show Dependency List
Show VALID / EXTERNAL / MISSING
Search missing resource
Manual replacement
Resource detail
Portability Test
```

---

# 73. MVP Phase 2

เพิ่ม:

```text
Pull External Into Project
Copy Resource
Rewrite Path
Backup
Dry Run
Revalidate
```

---

# 74. Phase 3

เพิ่ม:

```text
Consolidate Project
Nested Dependency
Batch Operations
Collision handling
Project-wide scan
```

---

# 75. Phase 4

เพิ่ม:

```text
Undo
Operation history
Export report
Shared roots
Advanced duplicate detection
Move simulation
```

---

# 76. UX Principle

ผู้ใช้ไม่ควรต้องเข้าใจ internal format ของ Effekseer

Workflow หลักต้องเหลือประมาณ:

```text
1. ลาก Effect เข้าโปรแกรม
2. เลือก Project Root
3. Scan
4. ดู External / Missing
5. กด Repair / Pull Into Project
6. Preview
7. Apply
8. Test Portability
```

---

# 77. Main Action Buttons

UI หลักควรมี:

```text
Scan
Find Missing
Pull External
Consolidate
Test Portability
Validate
```

---

# 78. Color / Status Design

ใช้ Icon + Text

ไม่พึ่งสีอย่างเดียว

เช่น:

```text
✓ VALID
⚠ EXTERNAL
✕ MISSING
? AMBIGUOUS
↪ REDIRECTED
```

---

# 79. Default Safe Behavior

ค่า Default:

```text
Strict Boundary = ON
Backup = ON
Dry Run = ON
Overwrite Existing File = OFF
Preserve Folders = ON
Auto Resolve Ambiguous = OFF
```

---

# 80. Never Silently Overwrite

ถ้าปลายทางมี:

```text
smoke.png
```

อยู่แล้ว

ห้าม overwrite เงียบ ๆ

ตรวจ hash ก่อน

ถ้าเหมือนกัน:

```text
Reuse Existing
```

ถ้าต่าง:

```text
Conflict
```

---

# 81. Portable Project Definition

นิยามอย่างชัดเจนว่า Portable หมายถึง:

> Effect และ Dependency ทั้งหมดสามารถ resolve ได้จาก Resource ภายใน Allowed Roots ที่กำหนด โดยไม่ต้องพึ่ง path ภายนอกที่ไม่ได้รวมอยู่กับ Project

---

# 82. Minecraft ไม่ใช่ Requirement หลัก

โปรแกรมนี้ต้อง Generic

อย่าผูกกับ:

```text
Minecraft
Forge
Iron's Spells
Gradle
```

แต่อนาคตอาจมี Export Preset เช่น:

```text
Minecraft Mod Assets
Unity
Custom Folder
```

ดังนั้น architecture ต้องรองรับ Export Profile

---

# 83. Optional Export Profile

ออกแบบ interface เผื่อ:

```text
IExportProfile
```

ตัวอย่าง:

```text
Generic Portable Project
Minecraft Assets
Custom Mapping
```

แต่ยังไม่จำเป็นต้อง implement Minecraft ใน MVP

---

# 84. Important Development Rule

หากไม่แน่ใจว่า `.efkefc` หรือ `.efkmat` สามารถ rewrite safely อย่างไร:

**อย่าเดา**

ให้สร้าง:

```text
Read-only Inspector
```

ก่อน

รายงาน path/dependency ให้ถูกต้องก่อน แล้วค่อยพัฒนา Rewrite Engine

ความถูกต้องของไฟล์สำคัญกว่าความเร็วในการทำ feature

---

# 85. Deliverables

ต้องส่ง:

```text
1. Working Windows Desktop App
2. Source Code
3. README
4. Architecture Overview
5. Supported Effekseer Formats
6. Known Limitations
7. Test Project
8. Unit Tests
9. Build Instructions
10. Publish Instructions
```

---

# 86. README ต้องมี

อธิบาย:

```text
What problem this tool solves
How Resource Boundary works
Difference between VALID and EXTERNAL
How to scan
How to consolidate
How backup works
How portability test works
Supported Effekseer versions
Known limitations
```

---

# 87. Acceptance Criteria

โปรแกรมถือว่าใช้งานได้เมื่อ Scenario นี้ผ่าน:

มี:

```text
Bloom.efkefc
```

Effekseer เปิดได้ปกติ

แต่ใช้ Resource:

```text
D:\OldProject\smoke.png
C:\Downloads\noise.png
Bloom\Textures\petal.png
```

ตั้ง Boundary:

```text
Bloom\
```

Scan ต้องแสดง:

```text
petal.png
VALID

smoke.png
EXTERNAL

noise.png
EXTERNAL
```

จากนั้นกด:

```text
Pull External Into Project
```

ต้อง Preview:

```text
D:\OldProject\smoke.png
→
Bloom\Textures\smoke.png

C:\Downloads\noise.png
→
Bloom\Textures\noise.png
```

Apply แล้ว:

```text
Files copied
References rewritten
Effect reparsed
Dependencies rescanned
```

ผลสุดท้าย:

```text
VALID = 3
EXTERNAL = 0
MISSING = 0
```

และ:

```text
PORTABILITY TEST

PASS
```

จากนั้นผู้ใช้ต้องสามารถย้าย Folder:

```text
Bloom\
```

ไปตำแหน่งอื่น โดย Resource ยังคง resolve จากภายใน Project

---

# 88. Development Workflow

ทำงานตามลำดับ:

```text
Step 1
Inspect real Effekseer formats

Step 2
Implement parser

Step 3
Implement dependency graph

Step 4
Implement path resolver

Step 5
Implement boundary validator

Step 6
Build scan UI

Step 7
Implement missing-resource search

Step 8
Implement portability test

Step 9
Implement safe relocation

Step 10
Implement safe rewrite

Step 11
Add backup + rollback

Step 12
Add consolidate

Step 13
Test against real projects
```

อย่าข้าม Step 1 แล้วเริ่มทำ Rewrite ทันที

---

# 89. Coding Style

ต้อง:

```text
Clean Architecture
Readable naming
Small focused classes
No god classes
No huge MainWindow code-behind
Async filesystem operations
CancellationToken
Structured logging
Nullable enabled
Error handling
Unit testable services
```

---

# 90. Final Product Vision

เป้าหมายสุดท้ายคือผู้ใช้เปิด Effect ที่ Effekseer แสดงผลได้ตามปกติ แล้วโปรแกรมสามารถบอกได้ทันทีว่า:

```text
Effect นี้พึ่งไฟล์จากที่ไหนบ้าง

ไฟล์ไหนอยู่ใน Project

ไฟล์ไหนแอบอ้างจาก Project เก่า

ไฟล์ไหนอยู่ Downloads

ไฟล์ไหนหาย

ไฟล์ไหนชื่อซ้ำ

ไฟล์ไหนควรถูกดึงเข้ามา

และเมื่อกด Repair แล้ว

Project ทั้งชุดสามารถย้ายไปใช้ที่อื่นได้โดยไม่พัง
```

Core Concept ของโปรแกรมคือ:

```text
SCAN
↓
AUDIT
↓
BOUNDARY CHECK
↓
REPAIR
↓
CONSOLIDATE
↓
VALIDATE
↓
PORTABLE
```

ให้ยึด Concept นี้เป็นหลักตลอดการออกแบบและพัฒนา