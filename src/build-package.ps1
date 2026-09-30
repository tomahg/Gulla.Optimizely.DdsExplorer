dotnet build .\Gulla.Optimizely.DdsExplorer\Gulla.Optimizely.DdsExplorer.csproj -c Release
dotnet pack .\Gulla.Optimizely.DdsExplorer\Gulla.Optimizely.DdsExplorer.csproj -c Release

move .\Gulla.Optimizely.DdsExplorer\bin\Release\*.nupkg ..\..\Nuget
