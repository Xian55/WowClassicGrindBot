@echo off
REM Download pre-generated Leaflet minimap tiles from the CDN into the local cache.
REM Double-click to grab every published era; pass -Era to limit, e.g.:
REM   download-leaflet.bat -Era mop
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\download-leaflet.ps1" %*
echo.
pause
