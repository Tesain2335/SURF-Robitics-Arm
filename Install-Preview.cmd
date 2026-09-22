@echo off
cd /d "%~dp0"
echo SURF Unity preview setup - does not start ROS or hardware.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-windows.ps1" -UnityOnly %*
if errorlevel 1 echo Setup failed. Copy the error above when requesting help.
pause
