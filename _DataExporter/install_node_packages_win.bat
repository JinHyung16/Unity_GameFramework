@echo off
chcp 65001 >nul
setlocal

echo.
echo ==========================================
echo    DataExporter - Node 패키지만 설치
echo ==========================================
echo   (엑셀 없이 필요한 node_modules 만 받습니다.
echo    실제 변환은 run_win.bat 을 쓰세요.)
echo.

:: 이 배치가 있는 폴더 = _DataExporter
set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"

:: 1) Node.js 확인
where node >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] Node.js가 설치되어 있지 않습니다.
    echo    Node.js 14+ 를 설치한 뒤 다시 실행하세요: https://nodejs.org
    pause
    exit /b 1
)

:: 2) package.json 확인
if not exist "%SCRIPT_DIR%package.json" (
    echo [ERROR] package.json 을 찾을 수 없습니다: %SCRIPT_DIR%package.json
    pause
    exit /b 1
)

:: 3) 이미 설치돼 있으면 스킵
if exist "%SCRIPT_DIR%node_modules" (
    echo [SKIP] node_modules 가 이미 있습니다. 재설치가 필요하면 폴더를 지우고 다시 실행하세요.
    echo.
    pause
    exit /b 0
)

:: 4) 설치
echo [INSTALL] npm install 실행 중...
call npm install
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] 패키지 설치 실패.
    pause
    exit /b 1
)

echo.
echo [SUCCESS] Node 패키지 설치 완료!
echo    이제 GameData\ 에 엑셀을 넣고 run_win.bat 을 실행하면 변환됩니다.
echo.
pause
exit /b 0
