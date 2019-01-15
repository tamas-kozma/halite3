rmdir /S /Q submit
mkdir submit
copy install.sh submit\
copy Halite3\MyBot.csproj submit\
copy Halite3\*.cs submit\
del sotarto.zip
powershell.exe -nologo -noprofile -command "& { Add-Type -A 'System.IO.Compression.FileSystem'; [IO.Compression.ZipFile]::CreateFromDirectory('submit', 'sotarto.zip'); }"