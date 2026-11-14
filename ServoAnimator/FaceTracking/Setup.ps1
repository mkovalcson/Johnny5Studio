$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
py -3.10 -m venv .venv
if ($LASTEXITCODE -ne 0) { throw 'Install Python 3.10 x64 first.' }
& ./.venv/Scripts/python.exe -m pip install -r requirements.txt
if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
Invoke-WebRequest 'https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task' -OutFile face_landmarker.task
& ./.venv/Scripts/python.exe tracker.py --self-test
if ($LASTEXITCODE -ne 0) { throw 'MediaPipe self-test failed.' }
