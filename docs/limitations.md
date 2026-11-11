# Limitations and open questions

[← back to README](../README.md) · [Русская версия](ru/limitations.md)

The project was conceived as a trial. Below is what was deliberately simplified, what was not verified, and what should be decided before installing at customer sites.

## Deliberate simplifications

- **Jobs are kept in memory.** Restarting the backend wipes the queue and the history.
- **No authorization.** Anyone on the local network can add a job or open the board panel and change settings.
- **HTTP only.** There is no encryption between the sender, the backend and the board.
- **The Wi-Fi password is stored on the board in plain text.**
- **The setup network is the same on every board:** `MyPrintAgent` with password `printagent`.
- **The receipt time zone is hard-coded:** UTC+5. Another zone requires a change in `AgentConfig.CreateDefault`.
- **Latin script only on receipts.**
- **No reprint.** A failed job is not retried automatically — that decision belongs to the sender.
- **The panel interface, backend error texts, code comments and commit messages are in Russian.**

## Not verified

- Synchronizing the board clock with the internet: on the network used for development the gateway did not let the board out, so the time always came from the backend.
- The `BAD_JOB` outcome on the board for `kitchen` and `bill` jobs: the backend does not allow creating a job that would trigger it.
- A bill with 60 items: whether the board has enough memory to parse it.
- A receipt at a line width of 32 characters, and the test print ruler — on paper.
- A printer scan across the full range of two subnets (508 addresses), and the scan page in a browser.
- Switching the router off during operation.
- Changing the device ID without rebooting the board.
- The printer name and printer ID have no tests.

## Known shortcomings

- **The panel freezes for about 23 seconds** when the printer is off: the status request waits for the printer to answer.
- **The backend time in a job is stored on the board as a 32-bit number:** good until 19 January 2038.
- **The printer ID changes with the address.** If the router gives the same printer a different IP, the ID will be new after the address is corrected in the panel.
- **The printer ID is not sent to the backend:** it is only stored on the board and shown in the panel.

## To decide before customer installs

1. Protecting the board panel: a PIN or a password.
2. A unique setup network name for each board.
3. Backend API authorization and HTTPS, if the backend is reachable from outside the local network.
4. Storing jobs in a database, if history must survive a restart.
5. The venue's time zone, if it is not UTC+5.

Details of the checks and review findings are in the journals (in Russian): [print pool](superpowers/plans/2026-11-10-print-pool-progress.md), [receipt types](superpowers/plans/2026-11-11-receipt-types-progress.md).
