# Vault Print Tool

A Windows desktop utility designed to streamline the extraction, printing, and exporting of engineering drawing packages and parts lists directly from **Autodesk Vault Professional**.

---

## Vault Workspace Prerequisites & Workflow Requirements

This tool is specifically intended to operate within an **Autodesk Vault Professional** workspace configured with lifecycle workflows and automated file publishing:

- **Lifecycle Workflow Statuses**: Your Vault workspace should use lifecycle states matching the statuses described in the in-app **User Guide**:
  - **Released** (White): Approved, official drawings in Vault — checked for export/printing by default.
  - **WIP / Unreleased** (Orange): Work-in-progress or non-released drawings — flagged for review before use.
  - **Missing PDF** (Salmon): Part listed in a drawing's BOM/parts list where no matching PDF was found in Vault.
  - **Source CAD** (Light Blue): Referenced Autodesk Inventor 3D models (`.iam` / `.ipt`).
  - **Duplicate** (Gray): Drawing referenced across multiple assemblies — unchecked automatically to avoid duplicate prints.
- **Automatic `.idw` to PDF Conversion**: The tool requires that Autodesk Inventor drawings (`.idw`) are **automatically converted to PDF** (e.g. `.idw.pdf` / `.pdf`) within Autodesk Vault when transitioning into a **Released** state (for example, via Autodesk Vault Job Processor lifecycle transition rules). BOM extraction and printing both operate directly on these Vault-managed PDFs.
- **Companion Export Formats Stored in Vault**: Any additional companion formats you choose to bundle via the **Also export** checkboxes (`.stp`, `.dxf`, `.zip`) **must also already be saved/checked into Autodesk Vault** alongside the drawings/parts:
  - `.stp` — 3D STEP CAD models stored in Vault.
  - `.dxf` — Flat-pattern / profile DXF exports stored in Vault.
  - `.zip` — Archives stored in Vault that collect all individual DXF exports generated from a **multi-sheet `.idw`** drawing.

---

## Features

- **Pure PDF-Driven BOM Extraction**: Directly parses parts lists and BOM tables from engineering drawing PDFs (`.idw.pdf`) to accurately resolve assemblies, sub-assemblies (`.iam`), and components.
- **Autodesk Vault Professional Integration**:
  - **v3.0 (Recommended)**: Supports **Auto** mode (uses local Autodesk Vault Client 2025 when installed and automatically falls back to Vault's HTTP Web API when Vault Client is not installed), **Web API Only**, **Local Client Only**, and **Windows Authentication**.
  - **v2.3 (Legacy)**: Connects via local Autodesk Vault Client 2025 Explorer API DLLs.
- **Automated ZIP Export**:
  - Exports selected drawings into a single ZIP archive.
  - Automatically incorporates the item's revision letter into the default archive name (e.g. `22000-1080 Rev A.zip`).
  - Supports bundling companion `.stp`, `.dxf`, and `.zip` (multi-sheet `.idw` DXF bundle) files already stored in Vault.
  - Supports organizing components into subfolders by sub-assembly.
  - Automatically suppresses and purges Autodesk Vault working folder cache directories (`_V` and 1KB metadata files).
- **Text & CSV Export**: Export structured drawing lists or missing item reports to `.txt` or `.csv`.
- **Integrated PDF Viewer & Batch Printing**: Preview PDF sheets and dispatch print jobs with paper size, scale, and orientation configuration.

---

## Changelog & Release Notes

### Version 3.0
- **Zero-Install Vault Web API Support**: Added HTTP Web API backend with automatic fallback when local Autodesk Vault Client 2025 is not installed, eliminating compile-time Autodesk DLL dependencies.
- **Connection Modes & Windows Authentication**: Added connection mode selector (`Auto`, `Web API Only`, `Local Vault Client Only`) and Windows Authentication toggle on the login screen.
- **Live Search Progress & Cancellation**: Background BOM extraction with live progress bar, activity log, and cancel button.
- **Per-User Installer**: Added standalone Windows installer (`installer/Setup.cs` / `build_installer.bat`).

### Version 2.3
- **Smart Top-Level Drawing (`*.idw`) Recommendation in File Search**:
  - Automatically detects and highlights the most relevant top-level drawing (`*.idw`) with a distinct `★ RECOMMENDED DRAWING` badge and pre-selects it by default.
  - Allows users to select any individual component drawing or model if they only require that specific file.
- **In-Interface User Guide & Workflow Reference**:
  - Embedded an interactive User Guide directly into the main interface covering Vault connection, pure PDF BOM extraction, queue verification, Drawing Status & Legend, and export/print actions.
  - Clarified default deselect behavior for General Arrangement (`-1000`) drawings.
- **Non-Intrusive Guide Toggle & Persistence**:
  - Added a dedicated `📖 User Guide` toggle button, quick-dismiss (`✕`) button, and "Show guide automatically on startup" option persisted in `%AppData%\VaultPrintTool\guidetoggle.txt`.
- **Enlarged Startup Window & Zero-Scroll Layout**:
  - Dynamically calculates window dimensions based on available screen work area (up to 1480×940).
- **Queue View & Filter Controls**:
  - `Switch to List View` / `Switch to Tree View` and `Show Selected Only` / `Show All Files` with descriptive tooltips.

### Version 2.2
- **ZIP Export Cleanliness**: Excluded Autodesk Vault `_V` client cache directories and 1KB metadata files (`.v`, `.xml`) from exported ZIP archives (`CreateMetaDataFile = False` + recursive cleanup).
- **Revision in Export Filename & Visibility**: Automatically includes drawing revision letter in default ZIP filename (e.g. `<PartNumber> Rev <Revision>.zip`) and displays `FileRev.Label` alongside lifecycle state (e.g. `Released (Rev A)`).

### Version 2.1
- Updated the application icon with high-resolution branding and consolidated release packaging.

### Version 2.0
- Transitioned from Vault API BOM traversal to a purely PDF-driven BOM extraction model.
- Added "Export List" to Text/CSV and "Export Missing" file reports.
- Support for default export directory configuration via `%AppData%\VaultPrintTool\exportdir.txt`.

---

## Requirements & Dependencies

- Windows 10 / 11 (x64)
- Microsoft .NET Framework 4.5+
- Autodesk Vault Professional server (with automatic `.idw` → PDF publishing on `Released` state)
- Autodesk Vault Client 2025 (optional for v3.0 when using Web API mode; required for v2.3)
- Bundled libraries:
  - `itextsharp.dll`
  - `BouncyCastle.Crypto.dll`
  - `PdfiumViewer.dll`
  - `pdfium.dll` (x64 native binary)

---

## Building the Application

### Version 3.0 (Recommended — Zero Autodesk DLL Build Dependencies)
```cmd
build_v3.bat
```
Compiles `VaultPrintTool_v3.vb` using the .NET Framework Visual Basic compiler (`vbc.exe`) and generates `VaultPrintTool_v3.exe`.

### Version 2.3 (Legacy — Requires Autodesk Vault Client 2025 Installed)
```cmd
build.bat
```
Compiles `VaultPrintTool.vb` into `VaultPrintTool.exe`.

### Windows Installer (v3.0)
```cmd
build_installer.bat
```
Compiles `installer\Setup.cs` into `VaultPrintTool_Setup_v3.0.exe` (requires `VaultPrintTool_Release_v3.0.zip` in the project root).
