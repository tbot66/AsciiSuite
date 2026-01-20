# Developer Notes

## Pixel mode

Run the new pixel renderer (default):

```bash
dotnet run --project SolarSystemApp/SolarSystemApp.csproj
```

Run the ASCII/console renderer:

```bash
dotnet run --project SolarSystemApp/SolarSystemApp.csproj -- --ascii
```

Run the pixel smoke tests:

```bash
dotnet run --project SolarSystemApp/SolarSystemApp.csproj -- --pixel-tests
```
