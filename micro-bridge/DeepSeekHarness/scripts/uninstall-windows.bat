@echo off
setlocal EnableExtensions

if not "%~1"=="" (
  for %%I in ("%~1") do set "HARNESS_DIR=%%~fI"
) else if defined DEEPSEEK_HARNESS_DIR (
  for %%I in ("%DEEPSEEK_HARNESS_DIR%") do set "HARNESS_DIR=%%~fI"
) else (
  echo [dsh-micro-bridge] Pass the Harness directory or set DEEPSEEK_HARNESS_DIR.
  exit /b 2
)

if not exist "%HARNESS_DIR%\package.json" (
  echo [dsh-micro-bridge] Harness source directory not found: %HARNESS_DIR%
  exit /b 2
)

pushd "%HARNESS_DIR%"
call pnpm dsh plugin --profile web remove @agentcontroller/dsh-micro-bridge-deepseek-harness
set "DSH_BRIDGE_EXIT=%ERRORLEVEL%"
popd

if "%DSH_BRIDGE_EXIT%"=="0" echo [dsh-micro-bridge] Removed from the web profile.
exit /b %DSH_BRIDGE_EXIT%
