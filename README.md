# Keyboard Analog Throttle

Keyboard Analog Throttle is a Windows 10/11 WPF application that maps global keyboard input to the trigger axes of a ViGEmBus Xbox 360 virtual controller.

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK to build the solution
- [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) installed separately to create the virtual controller

ViGEmBus is retired/archived. It remains an explicit dependency for this application, so assess its maintenance and compatibility risk before using it. This application never downloads or installs the driver.

## Build

```powershell
dotnet restore
dotnet build
dotnet test
```

Copy `config.example.json` to the application configuration location when configuration persistence is added in a later milestone. The example documents the baseline keyboard mapping and safety settings.
