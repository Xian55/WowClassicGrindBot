@echo off
setlocal enabledelayedexpansion

cd /d ..\
set "pwd=%cd%"
cd /d HeadlessServer

set "root=%pwd%\JSON\class\"
set "success=0"
set "fail=0"
set "total=0"
set "failed="

for /r "%root%" %%f in (*.json) do (
    set "file=%%f"
    set "file=!file:%root%=!"
    echo.
    echo !file!

    dotnet run -c Release --no-build --no-restore -- !file! -m Local -r WGC --loadonly
    set /a total+=1

    if !ERRORLEVEL! equ 0 (
        set /a success+=1
    ) else (
        set /a fail+=1
        set "failed=!failed!!file!|"
    )
)

echo.
echo ========================================
echo   Profile Load Summary
echo ========================================
echo   Total:   !total!
echo   Success: !success!
echo   Failed:  !fail!

if !fail! gtr 0 (
    echo.
    echo   Failed profiles:
    for %%a in ("!failed:|=" "!") do (
        if not "%%~a"=="" echo     %%~a
    )
)

echo ========================================
echo.

pause
