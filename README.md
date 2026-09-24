# emu8086ln

Windows 11 için modern bir **8086 mikroişlemci emülatörü, assembler ve öğrenme ortamı**.
emu8086 kaynak kodlarıyla (`#make_COM#`, `emu8086.inc`, sanal cihazlar) uyumludur.

## Özellikler

- **Assembler:** MASM/emu8086 sözdizimi, çok geçişli; COM / EXE (MZ) / BIN / BOOT çıktısı.
  Makrolar (`MACRO`, `LOCAL`, `REPT`), `INCLUDE`, `SEGMENT`, `.MODEL/.DATA/.CODE`, `PROC`, `EQU`, `DUP`,
  koşullu derleme. Menzil dışı koşullu atlamalar otomatik genişletilir.
- **CPU:** 8086 komut setinin tamamı (+ emu8086'nın kabul ettiği 80186 eklemeleri), doğru bayrak davranışı.
- **BIOS/DOS:** INT 10h (metin 80x25 ve 320x200x256 grafik), 16h, 15h, 1Ah, 17h, 33h (fare), 21h
  (konsol, tamponlu giriş, dosya işlemleri, tarih/saat...). Dosyalar sanal `C:` sürücüsünde tutulur.
- **Hata ayıklayıcı:** adım (F11), üzerinden adım (F10), **geri adım** (Shift+F11), imlece kadar çalıştır,
  kesme noktaları (F9), hız ayarı; registerlar, bayraklar, bellek (hex), yığın, disassembly, değişkenler.
- **CPU mimarisi görselleştirici (F12):** her komutun BIU/EU içinde getir-çöz-yürüt-yaz aşamalarını,
  adres hesabını, ALU işlemini, veri akışını canlı ve açıklamalı gösterir.
- **Sanal cihazlar:** trafik ışıkları, adım motoru, LED gösterge, termometre/ısıtıcı, robot, yazıcı, port izleyici.
- **IDE:** projeler ve şablonlar, çoklu sekme, sözdizimi renklendirme, otomatik tamamlama, komut başvurusu,
  18 örnek program, açık/koyu tema, Türkçe / English / Deutsch arayüz, ayarlanabilir yazı tipi ve boyutlar.

## Çalıştırma

```bash
dotnet run --project src/Emu8086.App
```

Tek dosyalık dağıtım paketi (`publish/emu8086ln/`):

```bash
dotnet publish src/Emu8086.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish/emu8086ln
```

## Testler

```bash
dotnet test
```

## Yapı

| Klasör | İçerik |
|---|---|
| `src/Emu8086.Core` | CPU, assembler, disassembler, BIOS/DOS, sanal cihazlar, adım analizi |
| `src/Emu8086.App` | WPF arayüzü; metinler `config/lang`, şablonlar `config/templates`, başvuru `config/reference` |
| `tests/Emu8086.Tests` | Kodlama, CPU, çalıştırma, örnek program ve analiz testleri |
| `tools/make_logo.py` | Uygulama ikonunu üretir |

Kullanıcı verileri: projeler ve sanal sürücü `Belgeler\emu8086ln`, ayarlar `%APPDATA%\emu8086ln`.
