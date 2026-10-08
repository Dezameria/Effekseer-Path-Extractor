# การรองรับ Effekseer หลายเวอร์ชัน

อัปเดต 2026-10-07: ตัว Map ตรวจกลุ่มเวอร์ชันร่วมกับโครงสร้าง resource จริง แทนการอนุญาตเฉพาะชื่อ `1.62a` และ `1.80.7` การ Map คง ToolVersion และ runtime เดิม ไม่ได้แปลงไฟล์ไปเป็น Effekseer รุ่นใหม่

| กลุ่ม Editor stable | INFO | BIN_ หลัก | สถานะ |
|---|---:|---:|---|
| 1.60, 1.61, 1.62 รวม patch เช่น 1.62a | 1610 | 1610 | รองรับ Texture mapping เมื่อโครงสร้างผ่านการตรวจ |
| 1.70 รวม patch เช่น 1.70e | 1710 | 1710 | รองรับ Texture mapping เมื่อโครงสร้างผ่านการตรวจ |
| 1.80 รวม patch เช่น 1.80.3 และ 1.80.7 | 1810 | 1810 | รองรับ Texture mapping เมื่อโครงสร้างผ่านการตรวจ |
| alpha / beta / รุ่นอื่น / layout ใหม่ | — | — | ไม่อนุญาตให้ Map พร้อมแจ้งเวอร์ชันและสาเหตุ |

เลข layout ภายในไม่ใช่เลขรุ่นของโปรแกรม ตัวอย่าง `1.80.3` ใช้ INFO/BIN_ `1810` ส่วน BIN_ สำหรับ compatibility ที่เก่ากว่ารองรับ `1500`, `1610`, `1710` ตามโครงสร้างที่รู้จัก ต้องมี BIN_ หลักตรงกับกลุ่ม Editor ด้วย

ใช้ได้ทั้ง Map ไฟล์เดียว สร้างโฟลเดอร์ต่อ Effect แก้ไฟล์เดิมพร้อมสำรอง และโฟลเดอร์ที่มีหลายเวอร์ชัน ไฟล์ที่ยังไม่รองรับจะแสดงเป็นกรณีที่ต้องแก้ในรายการ ผู้ใช้เอาเครื่องหมายเลือกออกเพื่อทำไฟล์อื่นต่อได้ ช่องเวอร์ชันแสดงรุ่นที่อ่านได้สำหรับไฟล์ที่ Map ได้

## ตรวจอะไรบ้างก่อนบันทึก

- EFKE container 0 มี INFO และ EDIT อย่างละหนึ่งส่วน และมี BIN_ อย่างน้อยหนึ่งส่วน
- กลุ่ม ToolVersion, INFO และ BIN_ หลักสอดคล้องกัน ไม่มี runtime หรือ chunk ที่ยังไม่รู้จัก
- INFO ตรงกับ dependency ของ runtime หลัก และ resource type เป็นชนิดที่รู้จัก
- EDIT อ่านได้ และ path ที่จะเปลี่ยนอยู่ในช่อง Texture ที่ writer รู้จัก
- ก่อน commit ตรวจแฮชต้นทาง อ่านไฟล์ผลลัพธ์กลับ และตรวจ reference ที่แก้กับ reference ที่ต้องคงเดิม

ข้อความปฏิเสธบอก Editor, container, INFO, BIN_ และเงื่อนไขที่ไม่ผ่าน การเป็น patch ในกลุ่มที่รองรับไม่ได้ข้ามการตรวจโครงสร้าง หาก patch ใหม่เปลี่ยนรูปแบบจริง การ Map จะหยุด

## หลักฐานและขอบเขตการทดสอบ

เปรียบเทียบ resource tables และ EDIT encoding กับโค้ดทางการ:

Source revisions: tag `170e` ที่ commit `92fdeb349c0c7eab9dfaf152047f22dec2fa102d`, tag `1803` ที่ commit `3ec538cad7d7e089da25516f993e9d27388a4a49` และ revision เดิม `82b37081a302b9f9eff0bf14dc6c845fca8c3c54`

- [Effekseer 1.70e: EFKE INFO / EDIT](https://github.com/effekseer/Effekseer/blob/170e/Dev/Editor/EffekseerCore/IO/EfkEfc.cs) และ [runtime exporter](https://github.com/effekseer/Effekseer/blob/170e/Dev/Editor/EffekseerCore/Binary/Exporter.cs)
- [Effekseer 1.80.3: EFKE INFO / EDIT](https://github.com/effekseer/Effekseer/blob/1803/Dev/Editor/EffekseerCore/IO/EfkEfc.cs) และ [runtime exporter](https://github.com/effekseer/Effekseer/blob/1803/Dev/Editor/EffekseerCore/Binary/Exporter.cs)

ผ่าน 74 tests ในสภาพแวดล้อมนี้: 48 tests เดิม, 25 tests เพิ่มสำหรับกลุ่มเวอร์ชัน/โครงสร้าง/ข้อผิดพลาด/ตำแหน่งโฟลเดอร์ และ 1 test จาก Mega จริง 1.80.3

- ไฟล์จริง: EVFX 17 Effect รุ่น 1.62a/1.80.7 และ Mega 1.80.3 โดยใช้สำเนาทดสอบเท่านั้น
- fixture โครงสร้าง: รุ่น stable ในกลุ่ม 1.60–1.62, 1.70 และ 1.80 พร้อม ordered/typed INFO, ทุกตาราง Texture, flags, Unicode, compatibility runtime และ runtime node bytes
- ทดสอบปฏิเสธ alpha/beta/future version, Editor ไม่ตรง layout, runtime ใหม่, unknown chunk/type, INFO ไม่ตรง runtime และ path ที่อยู่ใน EDIT field ที่ไม่รู้จัก
- โฟลเดอร์ fixture ที่มีหลายเวอร์ชันสามารถสร้างผลลัพธ์แยกกัน และรายงานไฟล์ที่ยังไม่รองรับได้

**1.70 และ patch ที่ไม่มีไฟล์จริงใช้ fixture โครงสร้างตาม exporter ทางการ ยังไม่ได้ทดสอบโหลดหรือ render ด้วย Editor รุ่นนั้น** การรองรับตารางและการคงข้อมูลเดิมไม่ใช่การรับรองภาพเอฟเฟกต์ทุกไฟล์/ทุก patch

## Mega ที่ย้ายมา

`mega.efkefc` เป็น Editor 1.80.3, INFO/BIN_ 1810 ทดลอง Map บนสำเนาแล้ว Texture ที่จำเป็นทั้ง 24 path ใช้งานได้ แฮชไฟล์ต้นฉบับและ asset ไม่เปลี่ยน ส่วน Model, Material และ runtime node bytes คงเดิม

เมื่อใช้ขอบเขต `D:\Minecraft\effekseer\mega` จะพบ `color.png` สองตำแหน่ง UI ยังให้เลือกตาม path แม้ไฟล์ในชุดนี้มีแฮชเหมือนกัน เลือกตาม texture เดิมที่ต้องการได้

หน้าหลักหน้าเดียวจัดการ Asset ทุกชนิดแล้ว สามารถซ่อม path Material/Model/GLB ของ Mega และสร้างสำเนา `.efkmat` ที่ซ่อม Texture ภายในได้ในโหมดสร้างชุดใหม่ ส่วนโหมดใช้ไฟล์ที่มีอยู่แก้ path ใน Effect โดยใช้ Asset เดิม ตัว Map Texture เดิมยังเก็บไว้สำหรับ CLI เท่านั้น ดู [full-resource-repair.md](full-resource-repair.md) ไม่อัปเกรดฟอร์แมต ไม่แก้เนื้อหาเอฟเฟกต์ทั่วไป และยังไม่รับรองการแสดงภาพทั้งหมด

โหมดแยกโฟลเดอร์คำนวณโครงสร้างที่ต้องรักษาจากทรัพยากรอื่นที่มีอยู่และคัดลอกได้เท่านั้น path เก่าที่หาไม่เจอจะยังแจ้งเตือน แต่ไม่ดึงตำแหน่ง Effect ลงไปซ้อนโครงสร้างโฟลเดอร์ของโปรเจกต์เก่า

## พัฒนาเพิ่ม

กฎอยู่ใน `EffectFormatCompatibility.cs` ซึ่ง parser, writer และบริการ Map ใช้ร่วมกัน ก่อนเพิ่ม layout ใหม่ให้ตรวจ exporter ทางการ เพิ่มตัวอ่าน/ตัวเขียน resource tables ถ้าจำเป็น และเพิ่ม fixture ที่ยืนยันการคง node bytes และข้อมูลที่ไม่ได้แก้ ห้ามเพิ่มเพียงชื่อเวอร์ชันเพื่อข้ามการตรวจ

ชุดทดสอบ Mega ใช้ตัวแปร `EFFEKSEER_VERSION_SAMPLE_ROOT` ชี้ไปยังโฟลเดอร์ที่มี `mega.efkefc` และ asset หากไม่ตั้ง จะข้ามเฉพาะ test นี้ ไม่แจกไฟล์ asset ของผู้ใช้ใน release
