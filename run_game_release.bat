REM --seed 1547232449
REM --seed 1547235188
REM --turn-limit 50
dotnet build -c Release
halite.exe --seed 1547235188 --replay-directory replays/ -vvv --width 64 --height 64 "dotnet %cd%\Halite3\bin\Release\netcoreapp2.0\MyBot.dll" "dotnet %cd%\reference\MyBot.dll 0 muted"
