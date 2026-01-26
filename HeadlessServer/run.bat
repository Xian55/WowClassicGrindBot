@ECHO OFF

dotnet run -c Release --no-build --no-restore -- %*
pause