@echo off
cd /d "%~dp0"
cmd /k "set VITE_DEV_SERVER_URL=http://127.0.0.1:5173 && electron ."
