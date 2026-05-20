@echo off
cd /d "%~dp0"

start "Vite Dev Server" cmd /k "vite --host 127.0.0.1"
echo Waiting for Vite server to start...
:waitloop
ping -n 2 127.0.0.1 >nul 2>&1
curl -s http://127.0.0.1:5173 >nul 2>&1
if errorlevel 1 goto waitloop
echo Vite server is ready!
start "Electron" cmd /k "set VITE_DEV_SERVER_URL=http://127.0.0.1:5173 && electron ."
