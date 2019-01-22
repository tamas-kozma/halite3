dotnet build -c Release
halite.exe --replay-directory replays/ -vvv --width 64 --height 64 "dotnet %cd%\Halite3\bin\Release\netcoreapp2.0\MyBot.dll" "dotnet %cd%\reference\MyBot.dll 0 muted" "dotnet %cd%\reference\MyBot.dll 0 muted" "dotnet %cd%\reference\MyBot.dll 0 muted"
