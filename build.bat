@echo off
echo ============================================
echo  TradeRouter Build Script
echo ============================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: dotnet SDK not found in PATH.
    echo Please install .NET 8 SDK from https://dot.net/
    pause
    exit /b 1
)

echo Building TradeRouter (Release, win-x64, self-contained)...
echo.

dotnet publish TradeRouter\TradeRouter.csproj ^
    -r win-x64 ^
    -c Release ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o dist\

if errorlevel 1 (
    echo.
    echo BUILD FAILED. Check errors above.
    pause
    exit /b 1
)

echo.
echo ============================================
echo  BUILD SUCCEEDED
echo  TradeRouter.exe is in: dist\
echo ============================================
echo.
pause
