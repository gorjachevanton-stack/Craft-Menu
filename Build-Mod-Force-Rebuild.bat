@echo off
setlocal
cd /d "%~dp0"

echo ========================================
echo       BLOCKS MENU FORCE REBUILD
echo ========================================
echo.

echo === TIME BEFORE BUILD ===
powershell -NoProfile -Command "Write-Output ('Local time: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')); Write-Output ('UTC time:   ' + [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss')); Write-Output ('Time zone:  ' + [TimeZoneInfo]::Local.DisplayName)"
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: .NET SDK was not found.
    pause
    exit /b 1
)

if not exist "BlocksMenu.csproj" (
    echo ERROR: BlocksMenu.csproj was not found.
    echo Put this BAT file next to BlocksMenu.csproj.
    pause
    exit /b 1
)

echo === FORCING FULL REBUILD ===
dotnet build "BlocksMenu.csproj" -c Release -t:Rebuild
if errorlevel 1 (
    echo.
    echo BUILD FAILED. Send the complete output.
    pause
    exit /b 1
)

echo.
echo === TIME AFTER BUILD ===
powershell -NoProfile -Command "Write-Output ('Local time: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')); Write-Output ('UTC time:   ' + [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss'))"
echo.
echo === DLL DETAILS AFTER REBUILD ===
powershell -NoProfile -Command "Get-Item '.\1.6\Assemblies\BlocksMenu.dll' | Format-List FullName,Length,CreationTime,LastWriteTime,LastWriteTimeUtc"
echo.
echo === DLL COPIES IN THIS MOD FOLDER ===
powershell -NoProfile -Command "Get-ChildItem -Path . -Filter 'BlocksMenu.dll' -Recurse | Select-Object FullName,Length,LastWriteTime | Format-Table -AutoSize"
echo.
echo Rebuild finished. Send the complete output.
pause
