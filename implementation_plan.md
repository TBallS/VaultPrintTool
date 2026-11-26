# Implementation Plan: Pure PDF-Driven BOM Extraction for Vault Print Tool

## Project Goals
The user has requested a refinement to the VaultPrintTool approach. To completely avoid Vault BOM inconsistencies (like missing parts or extraneous modules), we will transition to a **Purely PDF-Driven Extraction** method. 
The new logic dictates:
1. When the user gives a specific reference, the application uses **only** the data from that referenced PDF.
2. Once the parts list has been extracted from that PDF, the application searches Vault for all PDFs that have a matching part number in the title.
3. If any extracted part has an associated `.iam` model file in Vault, it is treated as a sub-assembly, and the process repeats for that sub-assembly's PDF.

---

## Proposed Changes

### VaultPrintTool.vb
#### [MODIFY] VaultPrintTool.vb
*   **Export Dir Configuration:**
    *   Add logic referencing `%AppData%\VaultPrintTool\exportdir.txt` (via `System.Environment.GetFolderPath`) to retrieve the user's preferred export directory. Use this as the default path for all SaveDialog windows.
*   **Export List to Text feature:**
    *   Add a new button next to "Export to ZIP" titled "Export List to Text".
    *   Implement an event handler `btnExportText_Click` that invokes a `SaveFileDialog` initialized to the mapped export directory (defaulting to Desktop if unresolved) with a filter for `Text Files (*.txt)`.
    *   Iterate through `ViewModel.Items` recursively.
    *   Format each line mimicking the display list: `[Indent Spaces] [Check Status] [Filename] [Status text]`. Ensure missing PDFs are appended with `(MISSING)` for visibility.

### 1. Completely Replace Vault API BOM Traversal
We will remove the `GetAssociations` and `GetBOMByFileId` logic completely from `RecursiveBuild`. The structure of the tree will no longer rely on Vault's internal component hierarchies.

#### [MODIFY] VaultPrintTool.vb
- Rewrite `BuildTree` to find the PDF corresponding to the selected root component.
- Create a new method `ProcessPdfRecursive(conn As Object, pdfId As Long, pdfName As String, parentNode As PrintItem, list As List(Of PrintItem), ids As HashSet(Of Long))`
  - Step A: Download the PDF.
  - Step B: Extract part numbers from its Parts List table via `ExtractPartNumbersFromPdf()`.
  - Step C: For each part number found, perform a Vault File Search (`FindFileByName` or equivalent).
  - Step D: Identify children from search results:
    - Collect all `.pdf` files matching the part number to add as children.
    - Identify if there is an `.iam` or `.ipt` file for this part number.
  - Step E: Add the component (fake or real) and its associated PDFs to the Tree.
  - Step F: If an `.iam` file was found for this part number, identify the primary `.pdf` among the found PDFs and recurse `ProcessPdfRecursive` on it.

### 2. File Search Refinement
- Update Vault lookup to efficiently search for the extracted part numbers.
- For each part number (e.g., `22000-1080`), we need to gather `22000-1080.iam`, `22000-1080.ipt`, and `22000-1080*.pdf`.
- We'll rely on the existing `FindFileByName` combined with string matching to separate the `.iam`, `.ipt`, and `.pdf` files efficiently for each extracted part.

## Verification Plan

### Automated Tests
- The build will be tested using `build.bat` via VaultPrintTool workspace.
- The compiled `VaultPrintTool.exe` execution will be verified to ensure there are no startup crashes or obvious Vault connectivity errors.

### Manual Verification
- Ask the user to run `VaultPrintTool.exe` and search for a known Assembly reference (e.g., `22000-1080`).
- The user will check if:
  1. The top-level PDFs restrict themselves to **only** what is in the main PDF's parts list.
  2. The children PDFs are successfully populated based on the extracted part numbers.
  3. Deeper nested components (sub-assemblies, `.iam`) have their PDFs expanded properly to show their parts lists in the tool.
  4. Extra Reference components or sibling GAs previously shown by Vault's BOM are no longer present.
