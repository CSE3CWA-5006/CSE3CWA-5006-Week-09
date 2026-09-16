@echo off
setlocal

rem This batch file publishes the ASP.NET Core app and creates a Linux-friendly
rem Azure App Service ZIP package. The important detail is that ZIP entries use
rem forward slashes (/), because Linux App Service deployment can fail or unpack
rem incorrectly when entries contain Windows backslashes (\).

set "PROJECT_DIR=%~dp0"
set "PROJECT_FILE=%PROJECT_DIR%MicrosoftPilot.csproj"
set "PUBLISH_DIR=%TEMP%\microsoftpilot-publish-linuxzip"
set "ZIP_FILE=%PROJECT_DIR%microsoftpilot-linux-forwardslash.zip"

echo.
echo Publishing MicrosoftPilot for Azure Linux App Service...
echo Project: "%PROJECT_FILE%"
echo Publish folder: "%PUBLISH_DIR%"
echo Zip file: "%ZIP_FILE%"
echo.

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "%ZIP_FILE%" del /f /q "%ZIP_FILE%"

dotnet publish "%PROJECT_FILE%" -c Release -o "%PUBLISH_DIR%"
if errorlevel 1 (
    echo.
    echo Publish failed. Check the error messages above.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$publishDir = '%PUBLISH_DIR%';" ^
  "$zip = '%ZIP_FILE%';" ^
  "Add-Type -AssemblyName System.IO.Compression;" ^
  "Add-Type -AssemblyName System.IO.Compression.FileSystem;" ^
  "$root = (Resolve-Path $publishDir).Path.TrimEnd('\') + '\';" ^
  "$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create);" ^
  "try {" ^
  "  Get-ChildItem -LiteralPath $publishDir -Recurse -File | ForEach-Object {" ^
  "    $relative = $_.FullName.Substring($root.Length).Replace('\','/');" ^
  "    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null;" ^
  "  }" ^
  "} finally { $archive.Dispose(); }" ^
  "$check = [System.IO.Compression.ZipFile]::OpenRead($zip);" ^
  "try {" ^
  "  $bad = ($check.Entries | Where-Object { $_.FullName.Contains('\') } | Measure-Object).Count;" ^
  "  $hasDll = [bool]($check.Entries | Where-Object { $_.FullName -eq 'MicrosoftPilot.dll' } | Select-Object -First 1);" ^
  "  Write-Host ('Backslash entries: ' + $bad);" ^
  "  Write-Host ('Has MicrosoftPilot.dll: ' + $hasDll);" ^
  "  if ($bad -ne 0 -or -not $hasDll) { exit 2 }" ^
  "} finally { $check.Dispose(); }"

if errorlevel 1 (
    echo.
    echo Zip verification failed.
    pause
    exit /b 1
)

echo.
echo Done.
echo Upload this file in Azure Portal:
echo "%ZIP_FILE%"
echo.
pause
