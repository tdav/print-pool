# Backend API

[← back to README](../README.md) · [Русская версия](ru/backend-api.md)

The backend listens on `http://0.0.0.0:5080`. Live reference with examples — Swagger: `http://localhost:5080/swagger`.

## Endpoints

| Endpoint | Caller | What it does |
|---|---|---|
| `POST /api/jobs` | sender | Adds a job. Response 201: `{ "jobId": "...", "status": "Queued" }` |
| `GET /api/devices/{deviceId}/jobs/next` | board | Returns the device's next job (200) or "no jobs" (204) |
| `POST /api/jobs/{jobId}/result` | board | Accepts the outcome: `{ "deviceId": "...", "success": true }` or `{ ..., "success": false, "error": "NO_PAPER" }` |
| `GET /api/jobs/{jobId}` | sender | Job state with creation, dispatch and finish times |

Job bodies are described in [Receipt formats](receipt-formats.md).

## Job states

`Queued` → `Dispatched` → `Printed` or `Failed` (with a reason code in `error`).

## Rules

- The board gets one job at a time. While a job is `Dispatched`, the next-job endpoint returns that same job: order is preserved and a job is not lost when the connection drops.
- An outcome is accepted only from the device the job was dispatched to.
- A job dispatched more than 60 seconds ago with no outcome becomes `Failed` with `DEVICE_TIMEOUT`. If the board reports an outcome later, it replaces that error.
- A `Failed` job is not retried automatically; the following jobs in the queue keep being dispatched.
- Devices are not registered in advance: a queue appears with the first job for a new ID.

The outcome wait time is set by `PrintPool:DispatchTimeoutSeconds`.

## Error responses

| Code | When |
|---|---|
| 400 | Invalid data; the response text names the first invalid field |
| 404 | No job with this ID |
| 409 | Outcome from another device, or for a job in the wrong state |
| 413 | Request body larger than 8 KB (checked before the fields) |

Error texts returned by the backend are in Russian.

## Reason codes in `error`

| Code | Meaning |
|---|---|
| `PRINTER_UNREACHABLE` | The printer does not respond on the network |
| `NO_PAPER` | Out of paper |
| `COVER_OPEN` | The cover is open |
| `SEND_FAILED` | Failure while sending the receipt to the printer |
| `BAD_JOB` | The board could not parse or lay out the job |
| `REBOOT_DURING_PRINT` | The board rebooted while printing; the receipt is not printed again |
| `DEVICE_TIMEOUT` | The board did not report an outcome in time |

## Example

```powershell
$body = '{"deviceId":"<device ID from the panel>","lines":[{"type":"text","text":"Hello"},{"type":"feed","lines":3},{"type":"cut"}]}'
Invoke-RestMethod http://localhost:5080/api/jobs -Method Post -ContentType 'application/json' -Body $body
```
