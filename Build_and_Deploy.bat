@echo off
chcp 65001 >nul
echo ============================================================================
echo   HS Comps Plugin v3.2.3 — Сборка, база статов Героев/Тринкетов и деплой в HDT
echo ============================================================================
cd /d "%~dp0"

echo [1/2] Проверка и предзагрузка 100%% артов карт (обычные + золотые)...
python -B tools\download_missing_full_cards.py 2>nul || py -3 -B tools\download_missing_full_cards.py 2>nul

echo [2/2] Компиляция HSCompsPlugin.csproj и автоматический деплой в HDT...
dotnet build HSCompsPlugin.csproj -c Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [OK] Сборка v3.2.3 и деплой в %%APPDATA%%\HearthstoneDeckTracker\Plugins\HSCompsPlugin успешно завершены!
) else (
    echo.
    echo [!] Ошибка сборки. Проверьте вывод выше.
)
pause
