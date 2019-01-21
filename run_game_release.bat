REM --seed 1547232449
REM --seed 1547235188
REM --turn-limit 50
REM --seed 1547235188 
dotnet build -c Release
halite.exe --replay-directory replays/ -vvv --width 32 --height 32 "dotnet %cd%\Halite3\bin\Release\netcoreapp2.0\MyBot.dll" "dotnet %cd%\reference\MyBot.dll 0 muted"
