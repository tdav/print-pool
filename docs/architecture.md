# Architecture

[← back to README](../README.md) · [Русская версия](ru/architecture.md)

## Parts

| Part | What it is | Location |
|---|---|---|
| Backend | ASP.NET Core on .NET 10, Minimal API with Swagger. In-memory job queue | `backend/PrintPool.Api` |
| Agent | A C# program on .NET nanoFramework for the ESP32-S3 board | `firmware/PrintAgent` |
| Agent core | Hardware-free logic: printer command building, receipt layout, print journal, settings validation | `firmware/PrintAgent.Core` |
| Printer | Xprinter XP-Q80DA receipt printer, 80 mm paper, on the local network, port 9100, ESC/POS | — |

Versions and the full list of libraries: [Tech stack and hardware](tech-stack.md).

## How a job travels

1. The sender service calls `POST /api/jobs` with the device ID, receipt type and data.
2. The board asks `GET /api/devices/{deviceId}/jobs/next` every 5 seconds.
3. Having received a job, the board writes a "started" mark to its own storage, asks the printer for its status, lays out the receipt and sends it over TCP.
4. The board reports the outcome: `POST /api/jobs/{jobId}/result`.
5. The sender reads the outcome through `GET /api/jobs/{jobId}`.

The board calls the backend, not the other way round: the backend does not need to know the board's address, and the board can sit behind any router.

## Why the core is separate

The board program cannot run on a computer, so everything that does not touch hardware lives in `PrintAgent.Core`. That code is compiled twice: together with the board program for nanoFramework, and on its own for .NET 10 tests. Hence its restrictions: no LINQ, no generic collections, no string interpolation, no `decimal` or `double`.

## Repository layout

```
backend/
  PrintPool.Api/          backend
  PrintPool.Api.Tests/    backend tests (xUnit)
firmware/
  PrintAgent/             board program; admin.html is the panel page
  PrintAgent.Core/        agent core
  PrintAgent.Core.Tests/  core tests (xUnit)
  build.ps1               builds the board program and deploys it
docs/                     documentation; ru/ holds the Russian versions;
                          superpowers/ holds specs, plans and journals
global.json               .NET SDK version
```

## What the system does not have

- A database: jobs live in backend memory and are gone after a restart.
- Authorization: neither the backend API nor the board panel has any.
- HTTPS: all traffic is plain HTTP inside the local network.

More in [Limitations](limitations.md).
