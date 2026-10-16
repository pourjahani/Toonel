@echo off
rem Builds a single-file, self-contained Toonel.exe (no .NET install needed).
rem Output: dist\Toonel.exe  (run it; accept the UAC prompt; done)
dotnet publish src\Toonel.App\Toonel.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false ^
  -o dist
if errorlevel 1 exit /b 1
echo.
echo Done: dist\Toonel.exe
