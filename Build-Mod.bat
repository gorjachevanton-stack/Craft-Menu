@echo off
setlocal
cd /d "%~dp0"

echo ========================================
echo          BLOCKS MENU BUILD
echo ========================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: .NET SDK was not found.
    echo Install .NET SDK 8 or newer from:
    echo https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

if not exist "BlocksMenu.csproj" (
    echo ERROR: BlocksMenu.csproj was not found.
    echo Put this BAT file next to BlocksMenu.csproj.
    echo.
    pause
    exit /b 1
)
echo === BUILD SYSTEM TIME ===
dotnet --info
dotnet build "BlocksMenu.csproj" -c Release
if errorlevel 1 (
    echo.
    echo BUILD FAILED. Send a screenshot of this window.
    echo.
    pause
    exit /b 1
)

echo.
echo Build finished. Check the output path shown above.
echo.
pause
