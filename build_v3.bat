@echo off
echo ============================================
echo  Building Vault Print Tool v3.0 (WPF)...
echo ============================================

set FRAMEWORK=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set VBC=%FRAMEWORK%\vbc.exe
set WPF=%FRAMEWORK%\WPF
set OUT=VaultPrintTool_v3.exe

echo Compiling (zero Autodesk DLL references)...
"%VBC%" /target:winexe /win32icon:"icon.ico" /out:"%OUT%" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Xml.dll ^
  /reference:System.Xml.Linq.dll ^
  /reference:System.Data.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Net.Http.dll ^
  /reference:System.Web.Extensions.dll ^
  /reference:"%WPF%\WindowsBase.dll" ^
  /reference:"%WPF%\PresentationCore.dll" ^
  /reference:"%WPF%\PresentationFramework.dll" ^
  /reference:"%WPF%\System.Printing.dll" ^
  /reference:"%FRAMEWORK%\System.Xaml.dll" ^
  /reference:"%FRAMEWORK%\System.IO.Compression.FileSystem.dll" ^
  /reference:"itextsharp.dll" ^
  /reference:"PdfiumViewer.dll" ^
  VaultPrintTool_v3.vb

if %errorlevel% equ 0 (
    echo.
    echo ============================================
    echo  BUILD SUCCESSFUL: %OUT%
    echo ============================================
    echo.
    echo Run: %OUT%
    exit /b 0
) else (
    echo.
    echo BUILD FAILED.
    exit /b 1
)
