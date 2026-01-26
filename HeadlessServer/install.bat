@ECHO OFF

cd /D "%~dp0"
set Headless=%~dp0
set Blazor=..\BlazorServer

set dataf=data_config.json
set addonf=addon_config.json
set framef=frame_config.json

IF EXIST "%Blazor%\%dataf%" (XCOPY /Y "%Blazor%\%dataf%" "%Headless%") ELSE (echo "%Blazor%\%dataf%" not exists)
IF EXIST "%Blazor%\%addonf%" (XCOPY /Y "%Blazor%\%addonf%" "%Headless%") ELSE (echo "%Blazor%\%addonf%" not exists)
IF EXIST "%Blazor%\%framef%" (XCOPY /Y "%Blazor%\%framef%" "%Headless%") ELSE (echo "%Blazor%\%framef%" not exists)

pause