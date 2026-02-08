cd /D "%~dp0"
dotnet run --configuration Release --no-build -- %*

pause