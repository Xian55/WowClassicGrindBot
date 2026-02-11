start "" "http://localhost:5000"
cd /D "%~dp0"
dotnet run --configuration Release --no-build -- --Reader:Type=%~1 --Pathing:Mode=%~2 --Reader:UseGpu=%~3

pause