@echo off
chcp 65001 >nul
echo ============================================================================
echo   HS Comps Plugin v3.2.2 - Sbor, baza statov i deploy v HDT
echo ============================================================================
cd /d "%~dp0"

echo [1/2] Proverka i zagruzka kart...
python -B tools\download_missing_full_cards.py 2>nul || py -3 -B tools\download_missing_full_cards.py 2>nul

echo.
echo [2/2] Kompilyaciya HSCompsPlugin.csproj i avtomaticheskiy deploy v HDT...
dotnet build HSCompsPlugin.csproj -c Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [OK] Sborka v3.2.2 i deploy v HDT uspeshno zaversheny!
) else (
    echo.
    echo [!] Oshibka sborki. Proverte vyvod vyshe.
)
pause
