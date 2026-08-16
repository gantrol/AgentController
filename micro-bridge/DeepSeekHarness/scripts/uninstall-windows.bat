@echo off
setlocal EnableExtensions

if "%~1"=="" (
  set "HARNESS_DIR=D:\project\ai\deepseek\deepseek-harness"
) else (
  for %%I in ("%~1") do set "HARNESS_DIR=%%~fI"
)

if not exist "%HARNESS_DIR%\package.json" (
  echo [dsh-micro-bridge] Harness source directory not found: %HARNESS_DIR%
  exit /b 2
)

pushd "%HARNESS_DIR%"
call pnpm dsh plugin --profile web remove @agentcontroller/dsh-micro-bridge-deepseek-harness
set "DSH_BRIDGE_EXIT=%ERRORLEVEL%"
popd

if "%DSH_BRIDGE_EXIT%"=="0" echo [dsh-micro-bridge] Removed from the web profile; settings and credentials were retained.
exit /b %DSH_BRIDGE_EXIT%
