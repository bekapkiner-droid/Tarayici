@echo off
cd /d "%~dp0"
if not exist ".kurulum_tamam" (
  echo Ilk kurulum yapiliyor, birkac dakika surebilir...
  python -m pip install -r requirements.txt
  if errorlevel 1 (
    echo Kurulum basarisiz. Python kurulu mu?
    pause
    exit /b 1
  )
  echo ok> .kurulum_tamam
)
start "" pythonw tarayici.py
