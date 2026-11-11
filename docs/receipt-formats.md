# Receipt formats

[← back to README](../README.md) · [Русская версия](ru/receipt-formats.md)

The kind of job is set by the `type` field in the `POST /api/jobs` body.

| `type` | Required | Forbidden | What is printed |
|---|---|---|---|
| absent or `"lines"` | `lines` | `data` | A free-form receipt: the sender defines the lines |
| `"kitchen"` | `data` | `lines` | A ticket for the cook |
| `"bill"` | `data` | `lines` | A bill for the customer |

Receipts are printed in Latin script. The board replaces non-ASCII characters with `?`. The fixed labels on the receipts are in Uzbek (Latin).

## Kitchen ticket

```json
{
  "deviceId": "3f2a1c9e-0000-0000-0000-000000000000",
  "type": "kitchen",
  "data": {
    "billNumber": "A20",
    "table": "12",
    "hall": "Siniy zal",
    "cashier": "Vlad",
    "items": [
      { "name": "SARBAST NEFILTR RAZLIV", "quantity": 1 },
      { "name": "SHASHLIK MOL GOSHTI", "quantity": 0.35 }
    ]
  }
}
```

On paper: centered date and time, `Hisob No`, `Stol No` with the hall, `Kassir`; below them the items in bold tall type (twice the normal height, normal width) as `name.....quantity` across the full line. A long name wraps. Then a feed and a cut.

## Customer bill

```json
{
  "deviceId": "3f2a1c9e-0000-0000-0000-000000000000",
  "type": "bill",
  "data": {
    "title": "BEREZKA",
    "billNumber": "A20",
    "table": "12",
    "hall": "Siniy zal",
    "cashier": "Vlad",
    "items": [
      { "name": "MNOGO MYASA 40SM", "quantity": 1, "sum": 80000 },
      { "name": "SARBAST NEFILTR RAZLIV", "quantity": 9, "sum": 126000 }
    ],
    "subtotal": 321000,
    "servicePercent": 10,
    "serviceSum": 32100,
    "total": 353100,
    "currency": "sum"
  }
}
```

On paper: the venue name in large type, a header (`Sana`, `Kassir`, `Hisob No`, `Stol No`), a `Nomi / Soni / Summa` table, the `Summa` and `Xizmat` lines (the latter only when the service sum is above zero), and a large `Jami` line.

## `data` fields

| Field | Rule |
|---|---|
| `title` | bill only, optional, up to 24 characters |
| `billNumber`, `table` | required, 1–20 characters |
| `hall` | optional, up to 40 characters |
| `cashier` | required, 1–40 characters |
| `items` | required, 1 to 60 items |
| `items[].name` | required, 1–100 characters |
| `items[].quantity` | a number above 0 and at most 9999, at most three decimal places, at most 5 characters when printed |
| `items[].sum` | bill only, required, 0–999 999 999 |
| `subtotal`, `total` | bill only, required, 0–999 999 999 |
| `servicePercent` | bill only, optional, 0–100 |
| `serviceSum` | bill only, optional, 0–999 999 999 |
| `currency` | bill only, optional, up to 8 characters |

Quantity may be fractional: `0.35` is printed as `0,35`, `1.0` as `1`.

For a kitchen ticket the bill fields are not validated and are not passed to the board.

## Date and time on the receipt

The sender does not provide a date. The board sets it: from its own clock when it is synchronized with the internet, otherwise from the backend time that arrives with the job. Five hours are added (UTC+5).

## Free-form receipt

```json
{
  "deviceId": "3f2a1c9e-0000-0000-0000-000000000000",
  "lines": [
    { "type": "text", "text": "CAFE ROMASHKA", "align": "center", "bold": true, "size": 2 },
    { "type": "divider" },
    { "type": "pair", "left": "Plov 1 x 35 000", "right": "35 000" },
    { "type": "qr", "value": "https://example.com/c/1045" },
    { "type": "feed", "lines": 3 },
    { "type": "cut" }
  ]
}
```

| Line type | Fields | Printed as |
|---|---|---|
| `text` | `text`, `align` (`left`/`center`/`right`), `bold`, `size` (1 or 2) | A line of text |
| `pair` | `left`, `right`, `bold` | Left part at the left edge, right part at the right edge |
| `divider` | — | A line of dashes across the full width |
| `qr` | `value` | A centered QR code |
| `feed` | `lines` | Blank lines |
| `cut` | — | Paper cut |

At most 100 lines per job.

## Line width

The number of characters per line is set in the board panel: 32 to 64, 48 by default (80 mm paper). It determines the bill column widths and the divider length.

Full layout rules and mock-ups are in the [receipt types spec](superpowers/specs/2026-11-11-receipt-types-design.md) (in Russian). The look of kitchen ticket items was changed later at the customer's request; see the [journal](superpowers/plans/2026-11-11-receipt-types-progress.md).
