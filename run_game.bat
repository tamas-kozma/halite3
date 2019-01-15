REM --seed 1547232449
REM --seed 1547235188
REM --turn-limit 50
dotnet build
halite.exe --seed 1547235188 --turn-limit 400 --replay-directory replays/ -vvv --width 64 --height 64 "dotnet %cd%\Halite3\bin\Debug\netcoreapp2.0\MyBot.dll" "dotnet %cd%\Halite3\bin\Debug\netcoreapp2.0\MyBot.dll"
