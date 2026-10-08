# ติดตั้งและใช้งานจาก GitHub

**Clone แล้วต้อง build ครั้งแรกก่อนเปิดโปรแกรม** เพราะ repository เก็บซอร์สโค้ด ไม่ได้รวม `release`, `.tools`, DLL ของ Effekseer หรือ Asset ของผู้ใช้ คู่มือนี้สำหรับ Windows x64; หน้าโปรแกรมใช้ WPF

## 1. ติดตั้งสิ่งที่ต้องใช้

| สิ่งที่ติดตั้ง | ใช้ทำอะไร | จำเป็นเมื่อใด |
|---|---|---|
| Git for Windows | clone / pull repository | เมื่อดาวน์โหลดผ่าน Git; ถ้าใช้ Download ZIP ไม่ต้องติดตั้ง Git |
| PowerShell 7 x64 | เรียกสคริปต์ build และเครื่องมือแก้ Node | แนะนำสำหรับทุกขั้นตอนในคู่มือนี้; สคริปต์ Editor Host ต้องใช้รุ่น 7 ขึ้นไป |
| .NET 10 **SDK** x64 | สร้างและทดสอบโปรแกรม | ต้องใช้เมื่อเริ่มจากซอร์ส; ติดตั้งเฉพาะ Runtime ไม่พอ |
| Effekseer Windows x64 รุ่น 1.80.7 / 1.70e | Core และ exporter สำหรับสร้าง/แก้ Node | เฉพาะ Editor Host และดูภาพ Effect; ไม่จำเป็นสำหรับสแกน/Map Asset |
| .NET Framework 4.8 หรือ 4.8.1 | รัน Editor Host รุ่น 1.70e | เฉพาะ 1.70e; คนละตัวกับ .NET 10 |

ติดตั้งสามตัวแรกด้วย WinGet ได้ เปิด Terminal แล้วรันทีละบรรทัด:

```powershell
winget install --id Git.Git --exact --source winget
winget install --id Microsoft.PowerShell --exact --source winget
winget install --id Microsoft.DotNet.SDK.10 --exact --source winget
```

หลังติดตั้ง ปิด Terminal เดิมแล้วเปิด **PowerShell 7** จาก Start Menu หรือพิมพ์ `pwsh` เพื่อเข้า shell รุ่นใหม่ ตรวจว่าพร้อมด้วย:

```powershell
git --version
$PSVersionTable.PSVersion
dotnet --list-sdks
```

PowerShell ควรเป็น 7.x และรายการ SDK ต้องมี 10.0.x โปรเจกต์ใช้ `global.json` เลือก SDK 10.0.100 หรือ feature band ใหม่กว่าในสาย 10.0 มีเฉพาะ SDK 8/9/11 ยังไม่ตรงกับการตั้งค่าปัจจุบัน

ถ้าไม่มี WinGet ดาวน์โหลดตัวติดตั้งจาก [Git for Windows](https://git-scm.com/install/windows), [PowerShell บน Windows](https://learn.microsoft.com/en-us/powershell/scripting/install/install-powershell-on-windows) และ [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) โดยเลือก **SDK → Windows → x64** วิธีติดตั้งด้วย WinGet อ้างอิงจาก [Microsoft .NET](https://learn.microsoft.com/en-us/dotnet/core/install/windows) และคู่มือ PowerShell ด้านบน

Windows 11 มี .NET Framework 4.8/4.8.1 อยู่แล้ว; เครื่องอื่นตรวจหรืออัปเดตตาม [คู่มือ .NET Framework ของ Microsoft](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server) ไม่ต้องติดตั้ง Developer Pack แยกเพื่อ build host ใน repository นี้ เพราะ project ดาวน์โหลด reference assemblies ผ่าน NuGet ให้อัตโนมัติ

## 2. Clone และสร้างโปรแกรมจัดการ Asset

เปิด PowerShell 7 ในโฟลเดอร์ที่ต้องการเก็บโปรเจกต์ แล้วรัน:

```powershell
git clone https://github.com/Dezameria/Effekseer-Path-Extractor.git
cd Effekseer-Path-Extractor
pwsh -NoProfile -File .\scripts\build.ps1 -Publish
```

ต้องมีอินเทอร์เน็ตในการ build ครั้งแรก เพื่อดาวน์โหลด package จาก NuGet และ runtime ที่ใช้ publish ไม่ต้องติดตั้ง Visual Studio, Python, Node.js หรือฐานข้อมูลเพิ่ม

หลัง build สำเร็จ เปิดโปรแกรม:

```powershell
& '.\เปิดโปรแกรม.cmd'
```

หรือดับเบิลคลิก `เปิดโปรแกรม.cmd` ในโฟลเดอร์โปรเจกต์ โดยไฟล์ที่สร้างคือ:

| ตำแหน่ง | ใช้ทำอะไร |
|---|---|
| `release/app/ResourceManager.App.exe` | หน้าต่างสำหรับสแกน ค้น Asset และ Map ไฟล์ |
| `release/inspector/ResourceManager.Inspector.exe` | ตรวจไฟล์ผ่านบรรทัดคำสั่ง |
| `release/LICENSE.txt` และ `release/Effekseer-MIT.txt` | License ที่ต้องไปกับโปรแกรมเมื่อแจกจ่าย |

**EXE ที่ publish แล้วรวม .NET runtime ไว้** หากนำชุดโปรแกรมนี้ไปเครื่อง Windows x64 อื่น ให้คัดลอก `release/app`, `release/inspector` และไฟล์ License ไปด้วย เครื่องปลายทางไม่ต้องติดตั้ง SDK หรือ .NET สำหรับสอง EXE นี้ ส่วน `release/editor-host` มีเงื่อนไข runtime แยกตามข้อ 3

ชุดทดสอบที่ใช้ Asset ตัวอย่างเฉพาะเครื่องผู้พัฒนาอาจขึ้น Skipped เมื่อไม่มี corpus นั้น ซึ่งไม่ใช่ build ล้มเหลว และไม่ต้องดาวน์โหลด commercial samples เพื่อเปิดโปรแกรม

## 3. เตรียมส่วนสร้าง/แก้เอฟเฟกต์ (เลือกทำ)

ส่วนนี้เป็นเครื่องมือผ่านคำสั่ง ยังไม่มี AI chat หรือปุ่มสร้างพายุในหน้าต่างจัดการ Asset

ดาวน์โหลด **ชุดโปรแกรม Editor สำหรับ Windows x64** จาก release ที่ตรงรุ่น:

- [Effekseer 1.80.7](https://github.com/effekseer/Effekseer/releases/tag/1807): เลือก `Effekseer1.80.7Win.zip`
- [Effekseer 1.70e](https://github.com/effekseer/Effekseer/releases/tag/170e): เลือก `Effekseer170eWin.zip`

แตก ZIP ทั้งชุด อย่าคัดลอกเฉพาะ EXE และอย่าเลือกแพ็ก Unity/Unreal/runtime แทน Editor ตัวอย่างสมมติแตกไว้ใน `C:\Tools\Effekseer1.80.7Win` และ `C:\Tools\Effekseer170eWin` ให้เปลี่ยน path ตามเครื่องของคุณ

จาก root ของ repository สร้างเฉพาะ host รุ่นที่จะใช้:

```powershell
pwsh -NoProfile -File .\scripts\build-editor-host.ps1 `
  -Version 1.80.7 -EditorDirectory 'C:\Tools\Effekseer1.80.7Win'

# ทำบรรทัดนี้เพิ่มเมื่อจะใช้ 1.70e
pwsh -NoProfile -File .\scripts\build-editor-host.ps1 `
  -Version 1.70e -EditorDirectory 'C:\Tools\Effekseer170eWin'
```

สคริปต์หา Tool/bin ให้อัตโนมัติ และตรวจว่า Core ตรงรุ่นก่อนแจ้งสำเร็จ ผลลัพธ์อยู่ใน `release/editor-host/<version>` ขั้นตอนนี้ต้องมี NuGet เช่นกัน

- Host 1.80.7 ต้องมี **.NET 10 runtime**; SDK ที่ติดตั้งในข้อ 1 มี runtime นี้ด้วย แม้ Editor ต้นทางจะใช้ .NET 9 ตัว host ของเราใช้ .NET 10
- Host 1.70e ต้องมี **.NET Framework 4.8 ขึ้นไป** และ PowerShell 7 สำหรับสคริปต์
- ไม่จำเป็นต้องดาวน์โหลดทั้งสองรุ่นหากใช้เพียงรุ่นเดียว
- Build โปรแกรมหลักด้วย `-Publish` ไม่ได้ build host ให้อัตโนมัติ

ลองสร้าง Effect สีม่วง โดยเลือกชื่อปลายทางที่ยังไม่มี:

```powershell
pwsh -NoProfile -File .\scripts\edit-effect.ps1 -Version 1.80.7 -Create `
  -Color '#A032FF' -Scale 0.5 -Name 'Purple' `
  -OutputPath (Join-Path $PWD 'purple-test.efkefc')
```

ลองสร้างพายุ ซึ่งรองรับ 1.80.7:

```powershell
pwsh -NoProfile -File .\scripts\create-tornado.ps1 `
  -OutputDirectory (Join-Path $PWD 'My-Tornado')
```

เปิด `My-Tornado/Raging-Tornado.efkefc` ใน Effekseer 1.80.7 แล้วกด Play สคริปต์นี้สร้างไฟล์ Effect และ Textures; **ไม่ได้สร้าง preview GIF หรือ launcher** ที่เคยแสดงในตัวอย่างของผู้พัฒนา อ่านคำสั่งตรวจ/แก้ Node และข้อจำกัดต่อได้ที่ [คู่มือ Editor Host](editor-host.md)

## 4. อัปเดตหลังติดตั้ง

ทำจาก root ของ repository เมื่อไม่มีงานแก้ source ที่ยังไม่ได้จัดการ:

```powershell
git pull --ff-only
pwsh -NoProfile -File .\scripts\build.ps1 -Publish
```

หากใช้ Editor Host ให้เรียก `build-editor-host.ps1` ของรุ่นนั้นอีกครั้งเมื่อ source เปลี่ยน Asset library/settings เก็บแยกในเครื่องของผู้ใช้ ไม่ได้อยู่ใน Git

## ปัญหาที่พบบ่อย

| อาการ | วิธีแก้ |
|---|---|
| `App not found` เมื่อเปิด launcher | ต้องรัน `scripts/build.ps1 -Publish` ให้สำเร็จก่อน; `release` ไม่ได้ติดมากับ clone |
| หา `git`, `pwsh` หรือ `dotnet` ไม่เจอ | ตรวจว่าติดตั้งแล้ว ปิด Terminal แล้วเปิดใหม่เพื่อรับ PATH |
| SDK not found / `global.json` | ติดตั้ง .NET **10 SDK x64**; Runtime อย่างเดียวหรือ SDK คนละ major ยังไม่พอ |
| Script requires PowerShell 7 | เปิด `pwsh` หรือแอป PowerShell 7 ไม่ใช่ Windows PowerShell 5.1 |
| `running scripts is disabled` | ถ้านโยบายเครื่องอนุญาต ใช้ `Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned` ในหน้าต่างปัจจุบัน แล้วลองใหม่; ไม่เปลี่ยนนโยบายทั้งเครื่อง |
| ZIP ที่ดาวน์โหลดมีไฟล์ถูก block | แตกไฟล์จาก ZIP ที่เชื่อถือได้; หากต้องปลด block ใช้ `Unblock-File -LiteralPath <path-to-zip>` ก่อนแตกใหม่ ไม่ปลด block ทั้งไดรฟ์ |
| NuGet restore ล้มเหลว | ตรวจอินเทอร์เน็ต/proxy และการเข้าถึง `api.nuget.org` แล้ว build ใหม่ |
| Host หา EffekseerCore.dll/Viewer.dll ไม่เจอ | ชี้ EditorDirectory ไปชุด Editor ที่แตกครบ ห้ามใช้เพียง EXE หรือแพ็ก runtime |
| `VersionMismatch` | ใช้ host ที่ตรง ToolVersion ของไฟล์; ระยะนี้ยังไม่ย้ายรุ่นให้อัตโนมัติ แม้ sample จะมากับ ZIP รุ่นใหม่ก็อาจยังเป็นไฟล์รุ่นเก่า |
| `OutputExists` | เลือกชื่อไฟล์/โฟลเดอร์ใหม่ สคริปต์สร้าง Effect ไม่เขียนทับไฟล์เดิม |

ตัวอย่างเช็ก build/test อย่างเดียวโดยไม่ publish:

```powershell
pwsh -NoProfile -File .\scripts\build.ps1
```
