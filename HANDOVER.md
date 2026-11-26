# Project Handover: Vault Print Tool (v2.3)

This handover summarizes the v2.3 architecture and build environment of **VaultPrintTool**.

---

## 1. Current Status & Version

- **Latest Version:** **v2.3** (`AssemblyVersion("2.3.0.0")`, `AssemblyFileVersion("2.3.0.0")`)
- **Last Modified / Built:** 24 September 2026
- **Source:** `VaultPrintTool.vb` (single-file VB.NET WPF application, 2,643 lines)
- **Compiled Binary:** `VaultPrintTool.exe`
- **Latest Release Package:** `VaultPrintTool_Release_v2.3.zip` and `Release/`

---

## 2. Environment & Build Setup

Before building or running, verify:

1. **OS & .NET Framework:** Windows 10/11 (x64) with .NET Framework 4.5+ (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\vbc.exe` must exist).
2. **Autodesk Vault Client 2025:** Must be installed at:
   `C:\Program Files\Autodesk\Vault Client 2025\Explorer`
   (`build.bat` references `Autodesk.Connectivity.WebServices.dll`, `Autodesk.DataManagement.Client.Framework.dll`, `Autodesk.DataManagement.Client.Framework.Vault.dll`, and `Autodesk.DataManagement.Client.Framework.Vault.Forms.dll` from this path.)
3. **Bundled Dependencies (in repo root & `Release/`):**
   - `itextsharp.dll`
   - `BouncyCastle.Crypto.dll`
   - `PdfiumViewer.dll`
   - `pdfium.dll` (x64 native binary — must stay alongside `VaultPrintTool.exe`)
4. **Build Command:** Run `build.bat` from the project root to recompile `VaultPrintTool.vb` into `VaultPrintTool.exe` with `icon.ico`.

---

## 3. Machine-Local Settings

These user preferences are stored locally in `%AppData%\VaultPrintTool\` on each machine:
- `%AppData%\VaultPrintTool\exportdir.txt` — Default export directory for Save dialogs (falls back to Desktop if absent).
- `%AppData%\VaultPrintTool\guidetoggle.txt` — Controls whether the in-app User Guide opens automatically on startup.

---

## 4. Summary of Recent Versions (v2.0 → v2.3)

- **v2.0:** Replaced Vault API BOM traversal (`GetAssociations` / `GetBOMByFileId`) with **Pure PDF-Driven BOM Extraction** (parses parts lists directly from `.idw.pdf` drawings and recurses into sub-assembly `.iam` PDFs). Added Text/CSV BOM list export and missing drawing reports.
- **v2.1:** Updated high-resolution `icon.ico` branding and release packaging.
- **v2.2:**
  - Excluded Autodesk Vault `_V` cache folders and 1KB `.v`/`.xml` metadata files from ZIP exports (`CreateMetaDataFile = False` + recursive cleanup).
  - Included drawing revision letters in default ZIP filenames (e.g., `22000-1080 Rev A.zip`) and displayed `FileRev.Label` alongside lifecycle state in the BOM tree.
- **v2.3 (Current):**
  - **Smart Top-Level Drawing (`*.idw`) Recommendation:** File search dialog highlights and pre-selects the primary top-level drawing with a `★ RECOMMENDED DRAWING` badge.
  - **In-App User Guide & Onboarding:** Added collapsible `📖 User Guide` panel, quick-dismiss (`✕`), startup toggle, and an empty-queue getting-started card.
  - **Layout & Controls:** Dynamic startup window sizing (up to 1480×940) for a zero-scroll layout, enhanced `View:` (Tree/List) and `Filter:` (Selected Only/All Files) toolbar controls, and tooltips across all interactive controls.

---

## 5. Key Files in Workspace

| File / Folder | Purpose |
| :--- | :--- |
| `VaultPrintTool.vb` | Main application source code (UI + Vault/PDF/Print/Export logic) |
| `build.bat` | Compiles `VaultPrintTool.vb` via `vbc.exe` |
| `VaultPrintTool.exe` | Latest compiled v2.3 executable |
| `README.md` | Project overview, features, and full changelog |
| `Release/` | Staging folder for distribution binaries & release notes (`Release_Notes_v2.3.txt`) |
| `VaultPrintTool_Release_v2.3.zip` | Latest packaged release archive |
| `test*.vb`, `vault_test*.vb`, `test_regex*.vb` | Legacy scratch/prototype test files from Feb 2026 |
