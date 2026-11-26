@echo off
echo ============================================
echo  Building Vault Print Tool v3.0 Installer...
echo ============================================

set FRAMEWORK=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FRAMEWORK%\csc.exe
set OUT=VaultPrintTool_Setup_v3.0.exe

if not exist "VaultPrintTool_Release_v3.0.zip" (
    echo ERROR: VaultPrintTool_Release_v3.0.zip not found.
    exit /b 1
)

if not exist "icon.ico" (
    echo ERROR: icon.ico not found.
    exit /b 1
)

if exist "%OUT%" del /f /q "%OUT%"

echo Compiling installer...
"%CSC%" /target:winexe /optimize+ /win32icon:"icon.ico" /out:"%OUT%" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:"%FRAMEWORK%\System.IO.Compression.dll" ^
  /reference:"%FRAMEWORK%\System.IO.Compression.FileSystem.dll" ^
  /resource:"VaultPrintTool_Release_v3.0.zip",VaultPrintTool_Release_v3.0.zip ^
  /resource:"icon.ico",icon.ico ^
  /resource:"icon.png",icon.png ^
  installer\Setup.cs

if %errorlevel% equ 0 (
    echo.
    echo ============================================
    echo  BUILD SUCCESSFUL: %OUT%
    echo ============================================
    exit /b 0
) else (
    echo.
    echo BUILD FAILED.
    exit /b 1
)
