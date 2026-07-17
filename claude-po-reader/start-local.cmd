@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
where py >nul 2>nul && (
  py -3 "%SCRIPT_DIR%server.py"
  exit /b %errorlevel%
)
python "%SCRIPT_DIR%server.py"
