@ECHO OFF

dotnet run -c Release --no-build --no-restore -e:ASPNETCORE_ENVIRONMENT=Development -- %*
pause