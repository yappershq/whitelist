@echo off
title Building WhiteList Solution (Fast Build)
rd ".build" /S /Q 2>nul
md ".build" 2>nul
cls

REM Build entire solution
echo Building solution...
dotnet build WhiteList.slnx -c Debug /m

if %ERRORLEVEL% neq 0 (
    echo:
    echo Build FAILED.
    echo:
    exit /b %ERRORLEVEL%
)

echo:
echo Build Completed...
echo:

REM Zip build output
echo Zipping build output...
if exist "WhiteList.zip" del "WhiteList.zip"
powershell -NoProfile -Command "Add-Type -Assembly System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::CreateFromDirectory('%CD%\.build\sharp', '%CD%\WhiteList.zip')"
move "WhiteList.zip" ".build\sharp\WhiteList.zip" >nul
echo Zipped to .build\sharp\WhiteList.zip
echo:

REM Copy to server (if server path exists)
if exist "D:\DEV\servers\1\serverfiles\game\sharp\" (
    echo Copying to server...
    xcopy ".build\sharp\*" "D:\DEV\servers\1\serverfiles\game\sharp\" /E /I /Y /Q >nul 2>&1
    echo Server Copy Completed...
)

echo:
