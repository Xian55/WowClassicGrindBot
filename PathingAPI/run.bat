start "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe" "http://localhost:5001"
cd /D "%~dp0"
dotnet run --configuration Release --no-restore

pause