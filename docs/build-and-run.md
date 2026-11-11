# Build and run

[← back to README](../README.md) · [Русская версия](ru/build-and-run.md)

The commands are for Windows and run from the repository root.

## Prerequisites

| What | Why |
|---|---|
| .NET SDK 10 (pinned in `global.json`: 10.0.302 or newer within 10.0) | Backend and tests |
| Visual Studio or Build Tools | The `csc.exe` compiler for the board program |
| `nuget.exe` on `PATH` | nanoFramework packages on the first build |
| `nanoff` (`dotnet tool install -g nanoff`) | Deploying the program to the board |

## Backend

```bash
dotnet run --project backend/PrintPool.Api
```

It listens on port 5080 on all network interfaces. The board reaches the backend over the network, so the computer's firewall must allow inbound connections on that port.

Check: `http://localhost:5080/swagger`.

## Board program

1. Copy `firmware/PrintAgent/Secrets.cs.example` to `firmware/PrintAgent/Secrets.cs` and fill in the name and password of a 2.4 GHz Wi-Fi network.
2. If needed, adjust the printer and backend addresses in `firmware/PrintAgent/Defaults.cs`. They can also be set later in the board panel.
3. Connect the board over USB and find its COM port number.

Build and deploy:

```powershell
firmware\build.ps1 -Project firmware\PrintAgent -Port COM5
```

Build only, without a board:

```powershell
firmware\build.ps1 -Project firmware\PrintAgent -NoDeploy
```

Flash the nanoFramework runtime to a new board first, then the program:

```powershell
firmware\build.ps1 -Project firmware\PrintAgent -Port COM5 -Firmware
```

The script builds the program without the Visual Studio extension: compiler → Metadata Processor → image → `nanoff`. The panel page `admin.html` is turned into `AdminPage.g.cs` during the build; edit `admin.html`.

## First board setup

1. After deployment the board joins the Wi-Fi network from `Secrets.cs`. If that fails, it raises the `MyPrintAgent` network (password `printagent`); the panel is at `http://192.168.4.1/`.
2. In the panel set the backend address and the printer's IP address and port, then press "Save and reboot".
3. In work mode press "Check connection" and "Test print".
4. Copy the device ID: the sender puts it in the `deviceId` field.

More about the panel: [Board and admin panel](agent.md).

## Tests

```bash
dotnet test backend/PrintPool.Api.Tests
```

```bash
dotnet test firmware/PrintAgent.Core.Tests
```

A running backend locks its build output, so the backend tests will not build next to it: stop it for the test run.

The board program is not covered by tests on the computer — only its core is. The program itself is verified by building, deploying and printing.

## Known nuisance

`nuget.exe` 7.3 may fail to exit after it finishes. For that reason `build.ps1` calls it only when a package is missing on disk. If the first build hangs at the packages step, kill the `nuget.exe` process and run the build again: the packages are already downloaded by then.
