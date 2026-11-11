# Print Pool

Network receipt printing for a café: a service puts a print job into the backend queue, an ESP32-S3 board picks it up over Wi-Fi and prints it on a receipt printer connected to the local network.

```
sender service ──HTTP──▶ backend (job queue) ◀──HTTP polling── ESP32-S3 board ──TCP 9100──▶ printer
```

The board lays out the receipt itself: the sender provides a receipt type and order data, not a rendered image.

Русская версия: [README.ru.md](README.ru.md).

## Features

- Three kinds of jobs: kitchen ticket, customer bill, and a free-form receipt built from lines.
- A queue per device; print order is preserved and a job is not lost when the connection drops.
- Protection against double printing after a power loss.
- Printer status reporting: out of paper, cover open, not responding.
- Board setup from a phone through a web panel, including a printer scan of the network.

## Built with

| Part | Technology |
|---|---|
| Backend | ASP.NET Core Minimal API on .NET 10, Swagger (Swashbuckle) |
| Board program | C# on .NET nanoFramework 1.18 |
| Board | ESP32-S3-WROOM-1 N16R8 (16 MB flash, 8 MB PSRAM), Wi-Fi |
| Printer | Xprinter XP-Q80DA, 80 mm paper, ESC/POS over TCP port 9100 |
| Tests | xUnit |

Full list with versions: [Tech stack and hardware](docs/tech-stack.md).

## Documentation

| Document | Contents |
|---|---|
| [Architecture](docs/architecture.md) | Parts of the system, how they talk, repository layout |
| [Tech stack and hardware](docs/tech-stack.md) | Frameworks, libraries, tools and devices, with versions |
| [Backend API](docs/backend-api.md) | Four endpoints, job states, error codes |
| [Receipt formats](docs/receipt-formats.md) | JSON for kitchen tickets, bills and free-form lines, field limits, printed layout |
| [Board and admin panel](docs/agent.md) | Operating modes, settings, the panel, failure behaviour |
| [Build and run](docs/build-and-run.md) | Prerequisites, running the backend, building and flashing the board, tests |
| [Limitations and open questions](docs/limitations.md) | What is not done, not verified, and what to decide before customer installs |

## Quick start

Backend:

```bash
dotnet run --project backend/PrintPool.Api
```

Swagger opens at `http://localhost:5080/swagger`.

Board program (board on COM5):

```powershell
firmware\build.ps1 -Project firmware\PrintAgent -Port COM5
```

Before the first build, copy `firmware/PrintAgent/Secrets.cs.example` to `Secrets.cs` and fill in the Wi-Fi name and password. Details: [Build and run](docs/build-and-run.md).

## Project history

The project was built from specifications and plans; the journals record hardware checks and decisions made along the way. These working documents are in Russian.

| Document | Contents |
|---|---|
| [Spec: print pool](docs/superpowers/specs/2026-11-10-print-pool-design.md) | Backend, board, admin panel |
| [Spec: receipt types](docs/superpowers/specs/2026-11-11-receipt-types-design.md) | Kitchen and bill receipts, time on the receipt |
| [Plan: print pool](docs/superpowers/plans/2026-11-10-print-pool.md) · [journal](docs/superpowers/plans/2026-11-10-print-pool-progress.md) | Reviews, failure scenarios, network scanner |
| [Plan: receipt types](docs/superpowers/plans/2026-11-11-receipt-types.md) · [journal](docs/superpowers/plans/2026-11-11-receipt-types-progress.md) | Spike program, reviews, customer changes |

## Also in this repository

- [report.html](report.html) — a survey of the customer's boards: which of them can run C# (in Russian).
- [Blink](Blink) — the first trial program for the board (LED blink), a Visual Studio project.
- [images](images) — board photos for the report.
