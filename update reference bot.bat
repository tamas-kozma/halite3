rmdir /S /Q reference
mkdir reference
dotnet build -c Release
copy Halite3\bin\Release\netcoreapp2.0\*.* reference\
