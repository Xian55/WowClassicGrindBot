@echo off
REM Migrate the local Json data folder to the era-partitioned layout (precata/...).
REM Idempotent. Double-click to migrate; pass -DryRun to preview:
REM   migrate-json-era.bat -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\migrate-json-era.ps1" %*
echo.
pause
