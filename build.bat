@echo off
setlocal

echo ========================================
echo  Fiesta Launcher - Build
echo ========================================
echo.

cd /d "%~dp0"
if errorlevel 1 exit /b 1

echo Stop running local launcher processes...
for %%P in (NanoSecurityTray.exe NanOnlineLauncher.exe Nano.exe) do (
    taskkill /F /IM %%P >nul 2>&1
)

timeout /t 1 /nobreak >nul

if exist ".\Build" (
    rmdir /s /q ".\Build"
)

mkdir ".\Build\Client"
if errorlevel 1 exit /b 1

mkdir ".\Build\Tools"
if errorlevel 1 exit /b 1

echo Restore NuGet packages...
dotnet restore ".\FiestaLauncher.sln"
if errorlevel 1 exit /b 1

echo.
echo Build solution (Release)...
dotnet build ".\FiestaLauncher.sln" -c Release -t:Rebuild
if errorlevel 1 exit /b 1

echo.
echo Copy launcher runtime set...
copy /Y ".\FiestaLauncher\bin\Release\net8.0-windows\NanOnlineLauncher.exe" ".\Build\Client\NanOnlineLauncher.exe" >nul
if errorlevel 1 exit /b 1
copy /Y ".\FiestaLauncher\bin\Release\net8.0-windows\NanOnlineLauncher.dll" ".\Build\Client\NanOnlineLauncher.dll" >nul
if errorlevel 1 exit /b 1
copy /Y ".\FiestaLauncher\bin\Release\net8.0-windows\NanOnlineLauncher.deps.json" ".\Build\Client\NanOnlineLauncher.deps.json" >nul
if errorlevel 1 exit /b 1
copy /Y ".\FiestaLauncher\bin\Release\net8.0-windows\NanOnlineLauncher.runtimeconfig.json" ".\Build\Client\NanOnlineLauncher.runtimeconfig.json" >nul
if errorlevel 1 exit /b 1
copy /Y ".\FiestaLauncher\bin\Release\net8.0-windows\FiestaLauncher.Shared.dll" ".\Build\Client\FiestaLauncher.Shared.dll" >nul
if errorlevel 1 exit /b 1

echo.
echo Publish Nano wrapper...
dotnet publish ".\NanoWrapper\NanoWrapper.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ".\Build\Client"
if errorlevel 1 exit /b 1

echo.
echo Copy security tray runtime set...
copy /Y ".\NanoSecurityTray\bin\Release\net8.0-windows\NanoSecurityTray.exe" ".\Build\Client\NanoSecurityTray.exe" >nul
if errorlevel 1 exit /b 1
copy /Y ".\NanoSecurityTray\bin\Release\net8.0-windows\NanoSecurityTray.dll" ".\Build\Client\NanoSecurityTray.dll" >nul
if errorlevel 1 exit /b 1
copy /Y ".\NanoSecurityTray\bin\Release\net8.0-windows\NanoSecurityTray.deps.json" ".\Build\Client\NanoSecurityTray.deps.json" >nul
if errorlevel 1 exit /b 1
copy /Y ".\NanoSecurityTray\bin\Release\net8.0-windows\NanoSecurityTray.runtimeconfig.json" ".\Build\Client\NanoSecurityTray.runtimeconfig.json" >nul
if errorlevel 1 exit /b 1
copy /Y ".\NanoSecurityTray\bin\Release\net8.0-windows\FiestaLauncher.Shared.dll" ".\Build\Client\FiestaLauncher.Shared.dll" >nul
if errorlevel 1 exit /b 1

echo.
echo Sync launcher configuration...
copy /Y ".\FiestaLauncher\server.json" ".\Build\Client\server.json" >nul
if errorlevel 1 exit /b 1

echo.
echo Copy Python ops tools...
copy /Y ".\FiestaLauncher\Tools\*.py" ".\Build\Tools\" >nul
if errorlevel 1 exit /b 1

echo.
echo ========================================
echo  Build completed successfully.
echo  Client output: .\Build\Client\
echo  Tools output : .\Build\Tools\
echo ========================================

exit /b 0
