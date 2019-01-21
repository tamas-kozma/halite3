REM --seed 1547232449
REM --seed 1547235188
REM --turn-limit 150
REM --seed 1547235188 
dotnet build
halite.exe --replay-directory replays/ -vvv --width 32 --height 32 "dotnet %cd%\Halite3\bin\Debug\netcoreapp2.0\MyBot.dll" "dotnet %cd%\reference\MyBot.dll 0 muted"
