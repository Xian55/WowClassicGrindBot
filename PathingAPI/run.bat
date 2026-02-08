start "" "http://localhost:5001"
cd /D "%~dp0"
dotnet run --configuration Release --no-build -- %*

pause