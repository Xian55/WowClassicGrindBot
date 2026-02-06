start "" "http://localhost:5000"
cd /D "%~dp0"
dotnet run --configuration Release --no-build -- --Reader:Type=%~1

pause