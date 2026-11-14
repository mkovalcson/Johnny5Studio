@echo off
setlocal
rem ======================================================================
rem  Sets up the Johnny 5 Animation Editor MCP server for Claude Code.
rem  Creates a private Python environment (.venv) in this folder, installs
rem  the MCP SDK into it, checks the server loads, and registers it with
rem  Claude Code if the claude command is available.
rem ======================================================================
cd /d "%~dp0"

rem ---- find Python 3.10 or newer --------------------------------------
rem Try each candidate and keep the first that really runs Python 3.10+
rem (on many PCs "python" is only a Microsoft Store shortcut).
set "PY="
call :try "py -3"
if not defined PY call :try "python"
if not defined PY (
    echo Couldn't find a working Python 3.10 or newer. Install it from python.org
    echo ^(tick "Add python.exe to PATH"^), then run this again.
    goto :fail
)
echo Using Python: %PY%

rem ---- private environment with the MCP SDK ---------------------------
if not exist ".venv\Scripts\python.exe" (
    echo Creating .venv ...
    %PY% -m venv .venv
    if errorlevel 1 goto :fail
)
echo Installing the MCP SDK ...
".venv\Scripts\python.exe" -m pip install --quiet --upgrade pip
".venv\Scripts\python.exe" -m pip install --quiet -r requirements.txt
if errorlevel 1 goto :fail
".venv\Scripts\python.exe" -c "import johnny5_editor_mcp; print('Server loads OK.')"
if errorlevel 1 goto :fail

set "SERVER_PY=%CD%\.venv\Scripts\python.exe"
set "SERVER=%CD%\johnny5_editor_mcp.py"
echo.
where claude >nul 2>nul
if errorlevel 1 goto :manual
echo Registering with Claude Code (available in all your projects) ...
claude mcp remove --scope user johnny5-editor >nul 2>nul
claude mcp add --scope user --transport stdio johnny5-editor -- "%SERVER_PY%" "%SERVER%"
if errorlevel 1 goto :manual
echo.
echo Done. In Claude Code, run /mcp to check that johnny5-editor is connected.
goto :end

:manual
echo The claude command wasn't found, so register the server yourself. In a terminal:
echo.
echo   claude mcp add --scope user --transport stdio johnny5-editor -- "%SERVER_PY%" "%SERVER%"
echo.
echo or add it in the Claude desktop app's MCP settings with that command and argument.
goto :end

:fail
echo.
echo Setup did not finish.
:end
pause
exit /b

:try
%~1 -c "import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)" >nul 2>nul
if not errorlevel 1 set "PY=%~1"
exit /b 0
