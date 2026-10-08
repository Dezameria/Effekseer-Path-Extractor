# ซ่อมทุก Asset ของ Effect — ชุด Mega

อัปเดต 2026-10-08: หน้าหลักหน้าเดียวค้นทุกชนิดจาก path ใน Effect เสมอ ได้แก่ Texture, Material, Model/GLB, Sound และ Curve รวม Texture ภายใน Material ไม่มี checkbox จำกัดการค้นหาแล้ว เลือก **ใช้ไฟล์ที่มีอยู่** หรือ **สร้างชุดใหม่** เพื่อกำหนดวิธีบันทึก การตรวจอย่างเดียวอยู่ในส่วนพับ **รายละเอียดผลตรวจ** แทนแท็บขั้นสูง

## รองรับอะไรใน Mega

| ชนิด | การจัดการ |
|---|---|
| `.efkefc` 1.80.3 | แก้ path ใน INFO, BIN_ และช่อง resource ที่รู้จักใน EDIT โดยคง runtime node payload เดิม |
| `.efkmat` EFKM 1800 ทั้ง 16 ไฟล์ | ใช้ไฟล์เดิมโดยตรงในโหมดใช้ไฟล์ที่มีอยู่; โหมดสร้างชุดใหม่จึงคัดลอกและแก้ Texture default ใน PRM_ และ Texture catalogue/node fields ใน DATA |
| `.efkmodel` layout 6 ทั้ง 10 ไฟล์ | ตรวจโครงสร้าง คัดลอกโดยคงไบต์ geometry เดิม และ Map runtime path ใน Effect |
| `.glb` 2 ทั้ง 10 ไฟล์ | ตรวจ header/chunks/URI คัดลอกโดยคงไบต์เดิม และ Map path สำหรับ Editor/GPU ใน Effect |
| `.png` 25 ไฟล์ | ค้นในโฟลเดอร์ย่อย Map ทั้งจาก Effect และ Material; `color.png` สองไฟล์ในชุดนี้มีข้อมูลเหมือนกัน จึงใช้ปลายทางเดียวกันได้เมื่อเลือกไว้แล้ว |

Material reader/writer รองรับ EFKM 3, 1610, 1710 และ 1800 ส่วน Effect ยังคงใช้กลุ่มเวอร์ชันตาม [version-support.md](version-support.md) ไม่มีการอัปเกรดเวอร์ชัน Effect หรือแปลง `.glb` เป็น `.efkmodel`

GLB ในชุด Mega เป็น embedded GLB ไม่มีไฟล์ buffer/image ภายนอก การซ่อมครบในรุ่นนี้อนุญาต GLB รูปแบบนี้เท่านั้น ถ้า GLB อ้างไฟล์ภายนอก โปรแกรมอ่านและแสดง reference ได้ แต่จะหยุดการซ่อมโมเดลนั้นพร้อมแจ้งสาเหตุ `.efkmodel` รุ่นอื่นยังไม่อนุญาตให้ผ่านขั้นซ่อมครบ

## วิธีใช้กับ Mega

1. ปิดโปรแกรมเดิม แล้วเปิด `เปิดโปรแกรม.cmd`
2. เลือก `D:\Minecraft\effekseer\mega\mega.efkefc` หรือโฟลเดอร์ `D:\Minecraft\effekseer\mega`
3. เลือกคลัง Asset เป็น `D:\Minecraft\effekseer\mega` ค้นเข้าโฟลเดอร์ย่อยอัตโนมัติ
4. เลือก **ใช้ไฟล์ที่มีอยู่** เพื่อ Map ทุกชนิดกลับไปยังคลัง แล้วแก้ Effect เดิมพร้อม backup
5. หรือเลือก **สร้างชุดใหม่** และตั้งโฟลเดอร์ผลลัพธ์ เพื่อคัดลอก Effect/Asset และแก้ path ในสำเนา Material
6. กดค้นหา รายการแสดงชนิด Asset และเจ้าของ เช่น Effect หรือ `ground1.efkmat`
7. กรณี Effect อ้าง `Textures/color.png` เดิมแต่ path เสีย จะพบชื่อซ้ำสองไฟล์ เลือก `mega\Textures\color.png` จาก path ที่แสดง ส่วน reference ที่มีอยู่จริงในคลังจะใช้ไฟล์ตาม path เดิมได้โดยตรง
8. ตรวจตัวอย่าง เห็น path ผลลัพธ์ ที่เก็บ Asset จำนวน Material ที่จะปรับ และรายการที่ยังไม่พบ จากนั้นบันทึก

ชื่อ Model และ Material คงเดิม เพื่อรักษาคู่ `.glb`/`.efkmodel` ตัวเลือกเปลี่ยนชื่อใช้กับ Texture เท่านั้น หากเลือกโมเดลสองรูปแบบจากคนละคู่ โปรแกรมจะหยุดและแจ้งให้แก้

## ที่เก็บผลลัพธ์

ตัวอย่างแบบแยกโฟลเดอร์:

```text
<output root>/mega.assets/
  mega.efkefc
  Materials/   # สำเนา Material ที่แก้ Texture path แล้ว
  Models/      # GLB + efkmodel ชื่อเดิม
  Textures/    # เปลี่ยนชื่อโฟลเดอร์นี้ได้
  mega.efkefc.mapping-<id>.json
```

แบบ **ใช้ไฟล์ที่มีอยู่**: Effect อยู่ที่เดิม สำรองเป็น `mega.efkefc.backup-<id>.bak` และบันทึกการ Map เป็น JSON ข้าง Effect แต่ไม่มีโฟลเดอร์ `mega.resources-<id>` หรือสำเนา Asset ทุกชนิดรวม Material เรียกไฟล์ในคลังโดยตรง ไม่แก้ Material ในคลังต้นทาง ไม่เปลี่ยนชื่อ Texture

ถ้า Texture ภายใน Material ยังเรียกไฟล์เดิมได้ถูกต้อง จะใช้ Material นั้นได้เลย หาก path ภายในเสีย หรือเลือก Texture คนละตำแหน่งที่ Material เดิมไม่ได้อ้าง โปรแกรมจะหยุดพร้อมแจ้ง `MaterialNeedsRepair` ให้แก้ Material ก่อน หรือเลือก **สร้างชุดใหม่** เพื่อซ่อมในสำเนา ต้องเก็บคลังไว้ที่เดิม โหมดนี้ไม่ได้ถือว่าเป็นชุดสำหรับย้ายไปที่อื่น

การค้นใช้ชื่อไฟล์พร้อมนามสกุล ไม่จับ `.glb` มาแทน `.efkmodel` เมื่อชื่อซ้ำและไม่มี path เดิมที่ถูกต้อง จะให้ผู้ใช้เลือกจากตำแหน่งจริงก่อนบันทึก

## ผลทดลองจริง

ชุดที่สร้างให้แล้ว: `D:\Minecraft\effekseer\mega-repaired\mega.assets\mega.efkefc`

ตรวจ 37 documents: Effect 1, Material 16, efkmodel 10, GLB 10 พบ 60 resource paths ที่ใช้งานได้ คัดลอก Asset 60 ไฟล์ และทดลองย้ายชุดผลลัพธ์ไปตำแหน่งใหม่แล้ว resource ที่จำเป็นทั้งหมดเป็น VALID

เหลือ **`trail_col.efkmat`** หนึ่งรายการที่ปรากฏเฉพาะใน EDIT และไม่มีไฟล์ในโฟลเดอร์ที่ให้มา ไม่อยู่ใน INFO/BIN_ ของ Mega โปรแกรมคง path เดิมและแจ้งเตือน ไม่สร้าง Material ทดแทนขึ้นเอง ถ้ามีไฟล์จริงในภายหลัง ให้เพิ่มในคลังแล้วค้นใหม่

การรองรับนี้ตรวจ path, โครงสร้าง, การคงข้อมูลและการย้ายไฟล์ ยังไม่ได้รับรองภาพผ่าน Effekseer Editor/runtime จริง ค่า Full Portability จึงยังเป็น Inconclusive ไม่แสดง PASS ทั้งที่มีข้อจำกัดนี้

## การตรวจและย้อนงาน

ผ่าน 98 tests ในสภาพแวดล้อมนี้ รวมชุดเดิม, Material ทุก layout ที่รองรับ, GLB/model ที่เสียหาย, Mega จริง, path ชื่อซ้ำ, Material เปลี่ยนหลังตัวอย่าง, ใช้ Asset เดิมทุกชนิดโดยไม่สร้างโฟลเดอร์, หยุดเมื่อ Texture ภายใน Material เสีย, ไม่รับแผนแก้ Material ในโหมดใช้ไฟล์เดิม, สร้างชุดใหม่แล้วเลื่อนโฟลเดอร์ Asset และ Map ใหม่โดยใช้ output เก่าเป็น Input/คลัง, คง shader/GUID/geometry/node data, ย้ายผลลัพธ์, ยกเลิกหลังคัดลอก Material และยกเลิกหลังแทนที่ Effect แล้วคืนค่าเดิม

ก่อน commit ตรวจแฮชต้นทาง อ่าน Effect/Material ที่เขียนกลับ ตรวจ reference ตรวจ resource ปลายทาง และตรวจ nested Material/Model อีกครั้ง ถ้าเกิดข้อผิดพลาดจะย้อนเฉพาะไฟล์ที่งานนี้สร้างและยังมีแฮชเดิม หากไฟล์ถูกเปลี่ยนโดยโปรแกรมอื่นจะเก็บไว้พร้อม `Recovery required` และ staging plan การสำเร็จของ batch เป็นราย Effect และยังไม่มีการกู้คืนอัตโนมัติหลังไฟดับ

Source assets ของ Mega ทั้ง 62 ไฟล์ไม่ถูกแก้ระหว่างทดสอบ ไม่แจก asset ของผู้ใช้ใน release ชุดทดสอบ Mega ใช้ตัวแปร `EFFEKSEER_VERSION_SAMPLE_ROOT` เช่นเดียวกับชุดทดสอบเวอร์ชัน

## CLI และ source

`repair <effect.efkefc> --resources <folder> --output-root <folder>` แสดงตัวอย่างชุดใหม่ก่อน ใช้ `--apply` เพื่อบันทึก หรือ `--in-place` เพื่อใช้ Asset เดิมและแก้ Effect เดิม โหมดนี้ใช้ `link` อัตโนมัติ; `copy` ใช้เฉพาะชุดใหม่ คู่โหมดอื่นจะถูกปฏิเสธ การเลือกกรณีชื่อซ้ำใช้ UI หรือ `--export-choices <new.json>` แล้วระบุ `CandidatePath` ในกรณีที่ยังว่างและนำกลับด้วย `--choices <json>` ไม่เดากรณีชื่อซ้ำให้

ฟังก์ชันอยู่ใน `ResourceMappingService` แยกจาก `TextureMappingService` เดิม `MaterialReferenceWriter` เปลี่ยนเฉพาะ path ที่รองรับ และ `ModelResourceReader` ตรวจชนิดโมเดลที่ใช้ในชุดนี้

อ้างอิงโครงสร้างจาก [MaterialInformation ของ Effekseer 1.80.3](https://github.com/effekseer/Effekseer/blob/1803/Dev/Editor/EffekseerCore/Utils/MaterialInformation.cs), [Model layout ของ Effekseer 1.80.3](https://github.com/effekseer/Effekseer/blob/1803/Dev/Cpp/Effekseer/Effekseer/Model/Effekseer.Model.h) และ [glTF 2.0 specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html)
