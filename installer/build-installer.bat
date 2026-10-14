@echo off
chcp 65001 >nul
setlocal

echo ============================================================
echo  USB Block Manager — сборка установщика
echo ============================================================
echo.

REM Путь к компилятору Inno Setup (user-установка)
set ISCC="C:\Users\Admin\AppData\Local\Programs\Inno Setup 6\ISCC.exe"

REM Перейти в папку со скриптом
cd /d "%~dp0"

echo [1/4] Остановка службы (если запущена)...
sc stop USBBlockService >nul 2>&1
timeout /t 2 /nobreak >nul

echo [2/4] Сборка publish GUI и службы...
pushd ..
dotnet publish src\USBBlockManager.csproj -c Release -r win-x64 --self-contained true
if errorlevel 1 (
    echo.
    echo ОШИБКА: не удалось собрать GUI. Прерываю.
    popd
    pause
    exit /b 1
)
dotnet publish service\USBBlockService.csproj -c Release -r win-x64 --self-contained true
if errorlevel 1 (
    echo.
    echo ОШИБКА: не удалось собрать службу. Прерываю.
    popd
    pause
    exit /b 1
)
popd

echo.
echo [3/4] Компиляция установщика через Inno Setup...
%ISCC% setup.iss
if errorlevel 1 (
    echo.
    echo ОШИБКА: Inno Setup не смог собрать установщик. Прерываю.
    pause
    exit /b 1
)

echo.
echo [4/4] Возврат службы в исходное состояние...
sc start USBBlockService >nul 2>&1

echo.
echo ============================================================
echo  Готово! Установщик собран:
echo  %~dp0Output\USBBlockManagerSetup-1.0.exe
echo ============================================================
echo.

pause