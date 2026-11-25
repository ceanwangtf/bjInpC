@echo off
setlocal

set "SRC_FILE=bjInpC.cs"
set "OUT_EXE=bjInpC.exe"

if not exist "%SRC_FILE%" (
    echo [ERROR] Could not find "%SRC_FILE%" in the current directory.
    echo Place this .bat file in the same folder as "%SRC_FILE%" and run it again.
    pause
    exit /b 1
)

where csc.exe >nul 2>&1
if %errorlevel%==0 (
    set "CSC_PATH=csc.exe"
) else (
    if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
        set "CSC_PATH=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    ) else if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
        set "CSC_PATH=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    ) else (
        echo [ERROR] csc.exe was not found on this system.
        pause
        exit /b 1
    )
)

echo Compiling "%SRC_FILE%" using "%CSC_PATH%"...
"%CSC_PATH%" /nologo /optimize+ /debug:full /out:"%OUT_EXE%" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "%SRC_FILE%"

if %errorlevel% neq 0 (
    echo.
    echo [FAILED] Compilation failed with errors.
    pause
    exit /b %errorlevel%
)

echo.
echo [SUCCESS] Built "%OUT_EXE%" successfully.
pause
