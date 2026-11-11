# Tech stack and hardware

[← back to README](../README.md) · [Русская версия](ru/tech-stack.md)

## Devices

| Device | Details | Role |
|---|---|---|
| ESP32-S3-WROOM-1 N16R8 | Dual-core microcontroller, 16 MB flash, 8 MB PSRAM (Octal SPI), Wi-Fi 2.4 GHz, built-in USB | The print agent: polls the backend, lays out and prints receipts, serves the admin panel |
| Xprinter XP-Q80DA | Thermal receipt printer, 80 mm paper, Ethernet (LAN), raw TCP port 9100, ESC/POS commands, auto-cutter | Prints receipts |
| Wi-Fi router | Any 2.4 GHz network with the board, the printer and the backend host on it | Connects the parts |
| Phone or computer | Any browser | Board setup through the admin panel |

The nanoFramework firmware target for this board is `ESP32_S3_OCTAL`.

## Backend

| Technology | Version | Used for |
|---|---|---|
| .NET SDK | 10.0 (pinned in `global.json`: 10.0.302, roll forward within 10.0) | Build and runtime |
| ASP.NET Core Minimal API | 10.0 | HTTP endpoints |
| Swashbuckle.AspNetCore | 10.3.1 | Swagger UI and OpenAPI document with request examples |
| System.Text.Json | built in | JSON serialization |
| `TimeProvider` | built in | Clock abstraction, replaceable in tests |

The job queue is kept in memory; there is no database.

## Board program

| Technology | Version | Used for |
|---|---|---|
| .NET nanoFramework | 1.18 (CoreLibrary 1.17.11) | C# runtime on the microcontroller |
| nanoFramework.System.Device.Wifi | 1.5.152 | Wi-Fi client and access point |
| nanoFramework.System.Net, System.Net.Http | 1.11.64, 1.5.214 | Sockets and the HTTP client for the backend |
| nanoFramework.WebServer | 1.2.162 | Admin panel HTTP server |
| nanoFramework.Json | 2.2.213 | Parsing jobs and the settings file |
| nanoFramework.Iot.Device.DhcpServer | 1.2.1016 | Address assignment in setup mode |
| nanoFramework.Iot.Device.MulticastDns | 1.0.337 | The `myprintagent.local` network name |
| nanoFramework.System.IO.FileSystem | 1.1.96 | Settings and the print journal on internal flash |
| nanoFramework.System.Device.Gpio | 1.1.64 | LED and the BOOT button |

Protocols: ESC/POS over raw TCP to the printer, HTTP to the backend, mDNS for the board's name.

The admin panel is a single hand-written HTML page with inline CSS and JavaScript, no frameworks and no external requests: in setup mode the phone has no internet.

## Tools

| Tool | Used for |
|---|---|
| `csc.exe` (Roslyn, from Visual Studio or Build Tools) | Compiling the board program with `/nostdlib` |
| nanoFramework Metadata Processor 3.0.104 | Converting the assembly to the `.pe` format the board runs |
| `nanoff` | Flashing the firmware and deploying the program over USB |
| `nuget.exe` | Restoring nanoFramework packages |
| PowerShell | `firmware/build.ps1` ties the steps above together |

## Tests

| Technology | Version | Used for |
|---|---|---|
| xUnit | 2.9.3 | Test framework for both test projects |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | Running the backend in-process (`WebApplicationFactory`) |
| Microsoft.Extensions.TimeProvider.Testing | 10.11.0 | `FakeTimeProvider` for timeouts and timestamps |
| coverlet.collector | 6.0.4 | Code coverage |

The board program itself cannot run on a computer, so its hardware-free logic lives in `firmware/PrintAgent.Core` and is compiled twice: for the board and for .NET 10 tests.
