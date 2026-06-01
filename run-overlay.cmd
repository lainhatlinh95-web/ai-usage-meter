@echo off
if exist "%~dp0AiUsageOverlay.exe" (
  "%~dp0AiUsageOverlay.exe" %*
) else (
  powershell -ExecutionPolicy Bypass -File "%~dp0Start-AiUsageOverlay.ps1" %*
)
