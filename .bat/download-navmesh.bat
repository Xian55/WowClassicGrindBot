@echo off
REM Download pre-baked navmesh tiles from the CDN into the local cache.
REM Double-click to grab all continents; pass a name to limit, e.g.:
REM   download-navmesh.bat Northrend
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\download-navmesh.ps1" %*
echo.
pause
