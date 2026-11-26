@echo off
echo ============================================
echo  Building Vault Print Tool (WPF)...
echo ============================================

set FRAMEWORK=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set VBC=%FRAMEWORK%\vbc.exe
set WPF=%FRAMEWORK%\WPF
set VAULT=C:\Program Files\Autodesk\Vault Client 2025\Explorer
set OUT=VaultPrintTool.exe

echo Compiling...
"%VBC%" /target:winexe /win32icon:"icon.ico" /out:"%OUT%" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Xml.dll ^
  /reference:System.Xml.Linq.dll ^
  /reference:System.Data.dll ^
  /reference:System.Drawing.dll ^
  /reference:"%WPF%\WindowsBase.dll" ^
  /reference:"%WPF%\PresentationCore.dll" ^
  /reference:"%WPF%\PresentationFramework.dll" ^
  /reference:"%WPF%\System.Printing.dll" ^
  /reference:"%FRAMEWORK%\System.Xaml.dll" ^
  /reference:"%FRAMEWORK%\System.IO.Compression.FileSystem.dll" ^
  /reference:"%VAULT%\Autodesk.Connectivity.WebServices.dll" ^
  /reference:"%VAULT%\Autodesk.DataManagement.Client.Framework.dll" ^
  /reference:"%VAULT%\Autodesk.DataManagement.Client.Framework.Vault.dll" ^
  /reference:"%VAULT%\Autodesk.DataManagement.Client.Framework.Vault.Forms.dll" ^
  /reference:"itextsharp.dll" ^
  /reference:"PdfiumViewer.dll" ^
  VaultPrintTool.vb

if %errorlevel% equ 0 (
    echo.
    echo ============================================
    echo  BUILD SUCCESSFUL: %OUT%
    echo ============================================
    echo.
    echo Run: %OUT%
) else (
    echo.
    echo BUILD FAILED.
    pause
)
