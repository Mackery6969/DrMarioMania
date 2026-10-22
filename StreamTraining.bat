@echo off
rem Runs bot training (for streaming or just leaving it going) and starts it again if the game crashes
rem Progress is saved after every round, so a restart carries on from the last finished round
rem Closing the game window normally stops it for good, or press Ctrl+C in this window
rem Any options are passed on to the training, e.g. StreamTraining.bat --jars=30 --music-shuffle=10

rem Where Godot is - set a GODOT environment variable to use a different one
rem (the console version shows the training log in this window)
if not defined GODOT set "GODOT=D:\Programs\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"

cd /d "%~dp0"

:run
"%GODOT%" --path . res://Scenes/GameScene.tscn -- --train %*

rem exit code 0 = the game was closed normally
if %ERRORLEVEL% equ 0 goto :eof

echo.
echo Training stopped unexpectedly (exit code %ERRORLEVEL%), restarting in 10 seconds... press Ctrl+C to stop
timeout /t 10 /nobreak >nul
goto run
