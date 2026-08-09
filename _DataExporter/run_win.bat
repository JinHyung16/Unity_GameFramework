@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo.
echo ====================================
echo    Hugh Excel to JSON Exporter
echo ====================================
echo.

:: Path settings (this folder is the project root)
set "SCRIPT_DIR=%~dp0"
:: This folder lives at the repo root (outside Assets\), so Unity ignores it entirely.
:: Input : _DataExporter\GameData\                    (excel source; git tracked)
:: Output: Assets\GameData\                           (json · Addressables group 'GameData', label 'game_data')
::         Assets\Scripts\DataLoader\Generated\       (generated data classes + Containers.Generated.cs)
set "GAMEDATA_PATH=%SCRIPT_DIR%GameData\"
set "JSON_OUTPUT_PATH=%SCRIPT_DIR%..\Assets\GameData\"
set "CS_OUTPUT_PATH=%SCRIPT_DIR%..\Assets\Scripts\DataLoader\Generated\"
set "CONFIG_PATH=%SCRIPT_DIR%config.json"
set "NODE_SCRIPT=%SCRIPT_DIR%smart_exporter.js"

:: Check Node.js installation
where node >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] Node.js is not installed.
    echo    Please install Node.js 14 or newer and try again.
    echo    https://nodejs.org
    pause
    exit /b 1
)

:: Check required files
if not exist "%NODE_SCRIPT%" (
    echo [ERROR] smart_exporter.js file not found: %NODE_SCRIPT%
    pause
    exit /b 1
)

if not exist "%SCRIPT_DIR%package.json" (
    echo [ERROR] package.json file not found: %SCRIPT_DIR%package.json
    pause
    exit /b 1
)

:: Check and install node_modules
if not exist "%SCRIPT_DIR%node_modules" (
    echo [INSTALL] Installing dependency packages...
    cd /d "%SCRIPT_DIR%"
    npm install
    if %errorlevel% neq 0 (
        echo [ERROR] Package installation failed.
        pause
        exit /b 1
    )
)

:: Parameter processing
set "COMMAND=all"
set "VERBOSE="
set "FILES="
set "CUSTOM_IN="
set "CUSTOM_OUT="
set "CUSTOM_CONFIG="

:parse_args
if "%~1"=="" goto end_parse

if /i "%~1"=="--in" (
    set "CUSTOM_IN=%~2"
    shift
    shift
    goto parse_args
)
if /i "%~1"=="--out" (
    set "CUSTOM_OUT=%~2"
    shift
    shift
    goto parse_args
)
if /i "%~1"=="--config" (
    set "CUSTOM_CONFIG=%~2"
    shift
    shift
    goto parse_args
)

if /i "%~1"=="--modified" set "COMMAND=modified"
if /i "%~1"=="-m" set "COMMAND=modified"
if /i "%~1"=="--verbose" set "VERBOSE=--verbose"
if /i "%~1"=="-v" set "VERBOSE=--verbose"
if /i "%~1"=="--help" goto show_help
if /i "%~1"=="-h" goto show_help

:: Check if it's an .xlsx file
echo %~1 | findstr /i "\.xlsx$" >nul
if %errorlevel% equ 0 (
    set "COMMAND=file"
    set "FILES=!FILES! %~1"
)

shift
goto parse_args

:end_parse

:: Apply custom paths (if provided)
if not "%CUSTOM_IN%"=="" set "GAMEDATA_PATH=%CUSTOM_IN%"
if not "%CUSTOM_OUT%"=="" set "JSON_OUTPUT_PATH=%CUSTOM_OUT%"
if not "%CUSTOM_CONFIG%"=="" set "CONFIG_PATH=%CUSTOM_CONFIG%"

:: Create output directory (after overrides)
if not exist "%JSON_OUTPUT_PATH%" (
    echo [CREATE] Creating JSON output folder: %JSON_OUTPUT_PATH%
    mkdir "%JSON_OUTPUT_PATH%" 2>nul
)

:: Execute
echo [START] Starting Export...
echo    Command: %COMMAND%
echo    Input (GAMEDATA_PATH): %GAMEDATA_PATH%
echo    Output JSON (JSON_OUTPUT_PATH): %JSON_OUTPUT_PATH%
echo    Output CS (CS_OUTPUT_PATH): %CS_OUTPUT_PATH%
echo    Config (CONFIG_PATH): %CONFIG_PATH%
echo.

cd /d "%SCRIPT_DIR%"

:: Pass paths via environment variables (smart_exporter.js reads these)
set "GAMEDATA_PATH=%GAMEDATA_PATH%"
set "JSON_OUTPUT_PATH=%JSON_OUTPUT_PATH%"
set "CS_OUTPUT_PATH=%CS_OUTPUT_PATH%"
set "CONFIG_PATH=%CONFIG_PATH%"

if "%COMMAND%"=="file" (
    node "%NODE_SCRIPT%" file %FILES% %VERBOSE%
) else (
    node "%NODE_SCRIPT%" %COMMAND% %VERBOSE%
)

set "RESULT=%errorlevel%"

echo.
echo ====================================
if %RESULT% equ 0 (
    echo    [SUCCESS] Export completed.
) else (
    echo    [ERROR] Export failed. Check the log above.
)
echo ====================================
echo.
echo Press any key to close this window...
pause >nul
exit /b %RESULT%

:show_help
echo.
echo Usage: run_win.bat [options] [files...]
echo.
echo Options:
echo   --modified, -m   Process modified files only
echo   --verbose, -v    Show detailed logs
echo   --in PATH        Input folder containing .xlsx files (default: .\GameData\)
echo   --out PATH       Output folder to write .json files (default: ..\Assets\GameData\)
echo   --config PATH    Config file path (default: .\config.json)
echo   --help, -h       Show this help
echo.
echo File specification:
echo   *.xlsx           Process specific Excel files (from input folder) only
echo.
pause
exit /b 0

