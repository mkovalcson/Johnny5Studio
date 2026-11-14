@echo off
setlocal
rem ======================================================================
rem  Convert-To-Johnny5.bat
rem  Converts audio to the Johnny 5 voice using Applio's command line
rem  (older version: underscore options).
rem
rem  Single file(s):  Convert-To-Johnny5.bat "X:\Audio\line01.wav"
rem      Each result is saved beside its original as name_j5.wav.
rem      You can also drag one or more audio files onto this .bat file.
rem
rem  Folder:          Convert-To-Johnny5.bat "X:\inputs" ["X:\outputs"]
rem      Every audio file in the folder is converted into the output
rem      folder (OUT_DIR below if you don't give one).
rem
rem  No arguments:    converts IN_DIR into OUT_DIR (set below).
rem
rem  Calling from another program: set J5_NOPAUSE=1 first so the window
rem  doesn't wait for a key at the end. The exit code is 0 when every file
rem  converted and 1 when anything failed.
rem ======================================================================

rem ---- Locations ---------------------------------------------------------
set "APPLIO=X:\Johnny5\Applio"
set "MODEL_DIR=%APPLIO%\logs\ShortCircuit1"
set "PTH=%MODEL_DIR%\ShortCircuit1_500e_29000s.pth"
set "IN_DIR=X:\Johnny5\Audio\ToConvert"
set "OUT_DIR=X:\Johnny5\Audio\Converted"

rem ---- Voice settings: copy these from Applio's Inference tab ------------
rem  PITCH           -24 to 24 (whole numbers)
rem  INDEX_RATE      0.0 to 1.0 in steps of 0.01  ("Search Feature Ratio")
rem  PROTECT         0.0 to 0.5 in steps of 0.001 ("Protect Voiceless Consonants")
rem  VOLUME_ENV      0.0 to 1.0 in steps of 0.01  ("Volume Envelope")
rem  F0_METHOD       rmvpe, crepe, crepe-tiny, fcpe, or a hybrid[...] choice
rem  SPLIT_AUDIO     True or False
rem  CLEAN_AUDIO     True or False; CLEAN_STRENGTH 0.0 to 1.0 in steps of 0.1
set "PITCH=0"
set "INDEX_RATE=0.3"
set "PROTECT=0.33"
set "VOLUME_ENV=1.0"
set "F0_METHOD=rmvpe"
set "SPLIT_AUDIO=False"
set "CLEAN_AUDIO=False"
set "CLEAN_STRENGTH=0.7"
rem Added to converted file names. Files whose names already end with it
rem are skipped, so re-running never converts a converted file again.
set "SUFFIX=_j5"

set /a DONE=0, FAILED=0, SKIPPED=0

rem ---- Checks shared by every mode ---------------------------------------
if not exist "%APPLIO%\env\python.exe" (
    echo Applio's Python was not found at "%APPLIO%\env\python.exe".
    goto :fail
)
if not exist "%PTH%" (
    echo Voice model not found: "%PTH%"
    goto :fail
)
rem Use the model's index file: prefer the "added_" index, else any .index.
set "INDEX="
for %%I in ("%MODEL_DIR%\*.index") do set "INDEX=%%~fI"
for %%I in ("%MODEL_DIR%\added_*.index") do set "INDEX=%%~fI"
if not defined INDEX (
    echo No .index file found in "%MODEL_DIR%".
    goto :fail
)
echo Model : %PTH%
echo Index : %INDEX%
echo.

rem ---- Choose the mode from the first argument ---------------------------
if "%~1"=="" goto :folder_default
if exist "%~1\" goto :folder_args
if exist "%~1" goto :file_mode
echo Not found: "%~1"
goto :fail

rem ======================================================================
rem  Single-file mode: every argument is a file; each result goes beside it
rem ======================================================================
:file_mode
rem core.py finds its own files relative to the current folder.
cd /d "%APPLIO%"
:next_file
if "%~1"=="" goto :summary
if exist "%~1\" goto :file_is_folder
if not exist "%~1" goto :file_missing
call :convert "%~f1" "%~dpn1%SUFFIX%.wav"
shift
goto :next_file
:file_is_folder
echo Skipping "%~1": when the first argument is a file, the others must be files too.
set /a SKIPPED+=1
shift
goto :next_file
:file_missing
echo Not found: "%~1"
set /a FAILED+=1
shift
goto :next_file

rem ======================================================================
rem  Folder mode
rem ======================================================================
:folder_args
set "IN_DIR=%~f1"
if not "%~2"=="" set "OUT_DIR=%~f2"
:folder_default
if not exist "%IN_DIR%\" (
    echo Input folder not found: "%IN_DIR%"
    goto :fail
)
if not exist "%OUT_DIR%\" mkdir "%OUT_DIR%"
echo From  : %IN_DIR%
echo To    : %OUT_DIR%
echo.
cd /d "%APPLIO%"
for %%F in ("%IN_DIR%\*.wav" "%IN_DIR%\*.mp3" "%IN_DIR%\*.flac" "%IN_DIR%\*.ogg" "%IN_DIR%\*.m4a") do call :convert "%%~fF" "%OUT_DIR%\%%~nF%SUFFIX%.wav"
goto :summary

rem ======================================================================
rem  :convert "input file" "output file"
rem ======================================================================
:convert
set "NAME=%~n1"
call set "TAIL=%%NAME:~-3%%"
if /i "%TAIL%"=="%SUFFIX%" goto :convert_skip
echo Converting %~nx1 ...
rem Remove an old result first, so success is judged by a new file appearing.
if exist "%~2" del /q "%~2"
"%APPLIO%\env\python.exe" core.py infer ^
    --input_path "%~1" ^
    --output_path "%~2" ^
    --pth_path "%PTH%" ^
    --index_path "%INDEX%" ^
    --pitch %PITCH% ^
    --index_rate %INDEX_RATE% ^
    --protect %PROTECT% ^
    --volume_envelope %VOLUME_ENV% ^
    --f0_method %F0_METHOD% ^
    --split_audio %SPLIT_AUDIO% ^
    --clean_audio %CLEAN_AUDIO% ^
    --clean_strength %CLEAN_STRENGTH% ^
    --export_format WAV
if errorlevel 1 goto :convert_failed
if not exist "%~2" goto :convert_failed
echo    Saved: %~2
set /a DONE+=1
exit /b 0
:convert_failed
echo    FAILED: %~nx1
set /a FAILED+=1
exit /b 0
:convert_skip
echo Skipping %~nx1 (already a converted file)
set /a SKIPPED+=1
exit /b 0

rem ======================================================================
:summary
echo.
echo Finished: %DONE% converted, %FAILED% failed, %SKIPPED% skipped.
if not defined J5_NOPAUSE pause
if %FAILED% GTR 0 exit /b 1
exit /b 0

:fail
echo.
if not defined J5_NOPAUSE pause
exit /b 1
