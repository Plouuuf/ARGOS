@echo off
rem Installation du serveur ARGOS : clic droit > Executer en tant qu'administrateur.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0INSTALLER-ARGOS.ps1" %*
echo.
pause
