@echo off
setlocal EnableExtensions

for %%I in ("%~dp0..") do set "PLUGIN_DIR=%%~fI"
if "%~1"=="" (
  for %%I in ("%PLUGIN_DIR%\..\..\..\project\ai\deepseek\deepseek-harness") do set "HARNESS_DIR=%%~fI"
) else (
  for %%I in ("%~1") do set "HARNESS_DIR=%%~fI"
)

if not exist "%HARNESS_DIR%\package.json" (
  echo [dsh-micro-bridge] Harness source directory not found:
  echo   %HARNESS_DIR%
  echo Usage: install-windows.bat ^<harness-directory^>
  exit /b 2
)

echo [1/5] Checking Node.js and pnpm...
where node >nul 2>nul
if errorlevel 1 (
  echo [dsh-micro-bridge] Node.js was not found in PATH.
  echo Required: Node 22.19+ in the 22.x line, or Node 24+.
  exit /b 3
)
node -e "const [a,b]=process.versions.node.split('.').map(Number);process.exit((a===22&&b>=19)||a>=24?0:1)"
if errorlevel 1 (
  for /f "delims=" %%V in ('node --version') do echo [dsh-micro-bridge] Unsupported Node.js %%V.
  echo Required: Node 22.19+ in the 22.x line, or Node 24+.
  exit /b 3
)

where pnpm >nul 2>nul
if errorlevel 1 (
  echo [dsh-micro-bridge] pnpm was not found in PATH.
  echo Enable Corepack or install pnpm 11.7+ before retrying.
  exit /b 3
)

echo [2/5] Preparing DeepSeek Harness dependencies...
if not exist "%HARNESS_DIR%\node_modules\.pnpm" (
  pushd "%HARNESS_DIR%"
  call pnpm install --frozen-lockfile
  if errorlevel 1 goto :failed_pop_harness
  popd
) else (
  echo [dsh-micro-bridge] Harness dependencies already exist.
)

echo [3/5] Installing external plugin dependencies...
pushd "%PLUGIN_DIR%"
call pnpm install --frozen-lockfile
if errorlevel 1 goto :failed_pop_plugin

echo [4/5] Verifying and building the external plugin...
call pnpm run verify
if errorlevel 1 goto :failed_pop_plugin
popd

echo [5/5] Linking the bundle into the Harness web profile...
pushd "%HARNESS_DIR%"
call pnpm dsh plugin --profile web add "%PLUGIN_DIR%"
if errorlevel 1 goto :failed_pop_harness
call pnpm dsh --profile web --dump-config >nul
if errorlevel 1 goto :failed_pop_harness
popd

echo.
echo [dsh-micro-bridge] Installed without modifying DeepSeek Harness source files.
exit /b 0

:failed_pop_plugin
popd
echo [dsh-micro-bridge] Installation stopped because a plugin command failed.
exit /b 1

:failed_pop_harness
popd
echo [dsh-micro-bridge] Installation stopped because a Harness command failed.
exit /b 1
