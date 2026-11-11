# Типы чеков: кухня и счёт — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** бэкенд принимает задание с типом чека (`kitchen`, `bill`, `lines`) и данными заказа, плата по типу собирает и печатает чек с датой и временем.

**Architecture:** бэкенд проверяет данные по типу, приводит их к виду для платы (пустые строки вместо `null`, количество готовой строкой) и добавляет к выдаче `serverTime`. Вёрстка чека — чистая логика в `firmware/PrintAgent.Core`: `ReceiptFormatter` превращает данные в массив `ReceiptLine`, который печатает уже существующий `EscPosBuilder`. Программа платы только разбирает JSON, выбирает время и вызывает форматтер.

**Tech Stack:** ASP.NET Core .NET 10 minimal API, xUnit, `FakeTimeProvider`; .NET nanoFramework 1.18 на ESP32-S3, `nanoFramework.Json` 2.2.213.

**Spec:** `docs/superpowers/specs/2026-11-11-receipt-types-design.md` (дополняет `2026-11-10-print-pool-design.md`).

## Global Constraints

- Ответы, комментарии в коде и сообщения коммитов — на русском; идентификаторы на английском.
- Приватные поля без `_`, обращение через `this.`; private-методы-помощники не создавать без необходимости.
- Чеки только латиницей; не-ASCII заменяется на `?` (`EscPosBuilder.Sanitize`).
- Предел тела `POST /api/jobs` — 8192 байта, проверяется раньше полей (413).
- Ширина строки `W`: от 32 до 64, по умолчанию 48. Деление `W / 2` целочисленное.
- Постоянные надписи: `Sana`, `Kassir`, `Hisob No`, `Stol No`, `Nomi`, `Soni`, `Summa`, `Xizmat`, `Jami` — в одном месте (`ReceiptFormatter`).
- Формат даты: `dd.MM.yyyy HH:mm`. Суммы: пробел между тройками разрядов. Количество на печати: запятая, без лишних нулей.
- Имена свойств классов разбора JSON на плате совпадают с ключами буква в букву (разбор чувствителен к регистру).
- Код `firmware/PrintAgent.Core` компилируется и под nanoFramework (`csc /nostdlib`), и под .NET 10: без LINQ, без интерполяции строк, без `string.Format`, без `decimal`/`double`, без `DateTime.ToString(format)`; сравнение строк посимвольное, без `StartsWith(string)`.
- Прежний формат `lines` и все 72 + 91 существующих теста продолжают проходить.
- Git: `git add` и `git commit` только по точным путям файлов; не пушить; ветку не переключать. Свои процессы останавливать по PID.

## Review Focus

1. **Строка шапки длиннее `W`** (зал на 40 знаков): должна переноситься, а не обрезаться и не ломать центрирование. Тест — задача 4.
2. **`Xizmat: 100%` с суммой из 11 знаков при `W = 32`**: надпись сдвигается влево, между ней и суммой остаётся пробел. Тест — задача 4.
3. **Нечётная ширина** (`W = 33`): крупная строка 16 знаков, ничего не выходит за край. Тест — задача 4.
4. **Количество `0.350`, `1.0`, `12.125`**: первое печатается `0,35`, второе `1`, третье отклоняется 400. Тест — задача 1.
5. **Задание прежней версии без `type`** и плата с несверенными часами при отсутствии `serverTime`. Тесты — задачи 2 и 5 (на плате).

## Порядок, агенты и модели

| Волна | Задачи | Кто | Модель |
|---|---|---|---|
| 0 | Зелёная база: `dotnet test` обоих тестовых проектов | ведущий | — |
| 1 (параллельно) | 1, 3, 4 | три исполнителя | sonnet |
| 1 (одновременно) | 0 — пробная программа на плате | ведущий | — |
| 2 | 2 | исполнитель | sonnet |
| 3 | 5 — программа платы | ведущий: нужна плата на COM5 | — |
| 4 | 6 — проверка на железе, итоговое ревью | ведущий + ревьюер | opus |

Ревью после каждой задачи — `superpowers:subagent-driven-development` (ревьюер sonnet; для задачи 4 назвать риск: расхождение с макетами спеки). Итоговое ревью ветки — `superpowers:requesting-code-review`, opus.

Окружение для исполнителей: Windows, репозиторий `E:\Works_Sata\raspberry`, ветка `feature/print-pool`; БД и Docker не нужны; бэкенд слушает `127.0.0.1:5080` и запущен заказчиком — **не останавливать и не запускать второй**; тесты бэкенда поднимают свой экземпляр через `WebApplicationFactory`. Задачи 3 и 4 работают в одном тестовом проекте одновременно: при блокировке вывода сборки (MSB3026/MSB3027) собирать в свой каталог через `-o`, вне репозитория. Тест, написанный в задаче, доказывается мутацией: временно сломать проверяемое правило, убедиться, что тест краснеет, вернуть, `git diff` пуст.

Ход работ: `docs/superpowers/plans/2026-11-11-receipt-types-progress.md` и `.claude/process.md`.

---

### Задача 0: пробная программа на плате

Проверяет три предположения спеки до того, как на них ляжет код. Одноразовая, в `firmware/spike/` (вне Git), без тестов и ревью.

- [ ] `nanoFramework.Json` разбирает в класс тело вида `{"jobId":"x","type":"bill","serverTime":1794345180,"data":{"title":"","billNumber":"A20","items":[{"name":"A","quantity":"0,35","sum":80000}],"total":353100}}`: вложенный объект, массив объектов, число в `int`, отсутствующий ключ `lines`.
- [ ] `WifiNetworkHelper.ConnectDhcp(ssid, password, WifiReconnectionKind.Automatic, true, 0, token)`: что возвращает и за сколько при работающем интернете; чему равен `DateTime.UtcNow.Year` после него.
- [ ] `new DateTime(1970, 1, 1).AddSeconds(1794419580)` даёт 11.11.2026 17:53 (UTC).
- [ ] Записать результаты в журнал хода работ. Если разбор вложенного объекта не работает — остановиться и вынести решение заказчику (запасной путь: плоский JSON для платы).

Поведение `ConnectDhcp` без интернета на этой сети проверить нельзя (роутер заказчик не отключает); в задаче 5 оно закрыто кодом, а не предположением.

---

### Задача 1: бэкенд — данные чека и их проверка

**Files:**
- Modify: `backend/PrintPool.Api/JobContracts.cs`
- Modify: `backend/PrintPool.Api/JobValidator.cs`
- Test: `backend/PrintPool.Api.Tests/JobValidatorTests.cs` (дополнить)

**Interfaces — Produces:**

```csharp
// Вход от отправителя: всё необязательное, проверяет валидатор.
public sealed record ReceiptItemDto(string? Name, decimal? Quantity, long? Sum);
public sealed record ReceiptDataDto(string? Title, string? BillNumber, string? Table, string? Hall,
    string? Cashier, ReceiptItemDto[]? Items, long? Subtotal, int? ServicePercent, long? ServiceSum,
    long? Total, string? Currency);

// Вид для платы: без null, количество — печатной строкой. Для kitchen поля счёта равны null и не сериализуются.
public sealed record ReceiptItem(string Name, string Quantity, long? Sum);
public sealed record ReceiptData(string? Title, string BillNumber, string Table, string Hall, string Cashier,
    ReceiptItem[] Items, long? Subtotal, int? ServicePercent, long? ServiceSum, long? Total, string? Currency);

public static class JobValidator
{
    public const string TypeLines = "lines", TypeKitchen = "kitchen", TypeBill = "bill";
    public static string? Validate(ReceiptLineDto[] lines);                 // без изменений
    public static string? ValidateData(string type, ReceiptDataDto? data);  // null — данные допустимы
    public static ReceiptData Normalize(string type, ReceiptDataDto data);  // вызывать только после ValidateData == null
    public static string FormatQuantity(decimal quantity);                  // 0.350 → "0,35", 1.0 → "1"
}
```

`PrintJob` получает два поля: `string Type` и `ReceiptData? Data`; `Lines` становится `ReceiptLineDto[]?`.

- [ ] **Шаг 1: падающие тесты.** Теория по таблице спеки «Проверка данных» — для каждой строки таблицы нижняя граница, верхняя граница и значение за границей, отдельно для `kitchen` и `bill`. Обязательные случаи с точными ожиданиями:
  - `ValidateData("kitchen", null)` и `ValidateData("bill", null)` → не `null`.
  - `kitchen` с полями счёта (`sum`, `total`) → `null` (не ошибка), а `Normalize` отдаёт `Sum == null`, `Total == null`.
  - `bill` без `subtotal`, без `total`, без `items[0].sum` → ошибка, называющая это поле.
  - `items` из 0 и из 61 позиции → ошибка; из 60 → `null`.
  - `quantity`: `0`, `-1`, `10000`, `0.1234`, `12.125` → ошибка; `0.001`, `9999`, `0.35`, `99.99` → `null`.
  - `FormatQuantity`: `1m → "1"`, `1.0m → "1"`, `0.5m → "0,5"`, `0.350m → "0,35"`, `9999m → "9999"`, `99.99m → "99,99"`.
  - `Normalize`: `hall`, `title`, `currency`, равные `null`, → `""` (для `bill`); `servicePercent`, `serviceSum`, равные `null`, → `0`; для `kitchen` `Title`, `Currency`, `Subtotal`, `ServicePercent`, `ServiceSum`, `Total` → `null`.
  - Суммы `999_999_999` → `null`, `1_000_000_000` и `-1` → ошибка.
- [ ] **Шаг 2:** `dotnet test backend/PrintPool.Api.Tests` — новые тесты не компилируются или падают.
- [ ] **Шаг 3:** реализовать записи и три метода. `FormatQuantity` — через `decimal`, инвариантная культура, замена точки на запятую.
- [ ] **Шаг 4:** `dotnet test backend/PrintPool.Api.Tests` — все зелёные, прежние 72 в их числе. Мутация: убрать проверку длины печатного вида количества → тест `12.125` краснеет.
- [ ] **Шаг 5:** коммит по точным путям трёх файлов.

`JobStore` и `JobEndpoints` в этой задаче не меняются сверх того, что нужно для компиляции (`Lines!`).

---

### Задача 2: бэкенд — методы API

**Files:**
- Modify: `backend/PrintPool.Api/JobEndpoints.cs`
- Modify: `backend/PrintPool.Api/JobStore.cs`
- Test: `backend/PrintPool.Api.Tests/JobEndpointsTests.cs`, `JobStoreTests.cs` (дополнить)

**Interfaces — Consumes:** всё из задачи 1. **Produces:**

```csharp
public sealed record CreateJobRequest(Guid DeviceId, string? Type, ReceiptLineDto[]? Lines, ReceiptDataDto? Data);
// JobStore: вместо Add(Guid, ReceiptLineDto[])
public PrintJob Add(Guid deviceId, string type, ReceiptLineDto[]? lines, ReceiptData? data);
```

`JobEndpoints` получает `TimeProvider` из DI для `serverTime` (он уже зарегистрирован для `JobStore`; если нет — зарегистрировать `TimeProvider.System` в `Program.cs`).

- [ ] **Шаг 1: падающие тесты** (через `WebApplicationFactory`, время — `FakeTimeProvider`):
  - `POST` с JSON кухни и JSON счёта **дословно из спеки** → 201.
  - `POST` без `type` с `lines` → 201; `GET next` отдаёт `"type":"lines"` и `lines`, ключа `data` нет.
  - `POST` с `type: "kitchen"` и `lines` → 400; с `type: "lines"` и `data` → 400; с `type: "receipt"` → 400; `kitchen` без `data` → 400.
  - `GET next` для задания кухни: `type == "kitchen"`, `serverTime` равно секундам `FakeTimeProvider` (задать время `2026-11-11T17:53:00Z` и ожидать `1794419580`), `data.items[1].quantity == "0,35"` (строка), ключей `lines`, `sum`, `total`, `title` в теле нет.
  - `GET next` для счёта без `hall` и `currency`: `data.hall == ""`, `data.currency == ""`, `data.servicePercent == 0`.
  - `GET /api/jobs/{id}`: есть `type` и `data` (или `lines`).
  - Тело больше 8192 байт с допустимыми полями (60 позиций по 100 знаков) → 413, не 400.
  - Повторная выдача выданного задания отдаёт те же `type` и `data`.
- [ ] **Шаг 2:** тесты падают.
- [ ] **Шаг 3:** реализация. Порядок в `POST`: `deviceId` → выбор вида по таблице спеки «Правила выбора вида» → `Validate` или `ValidateData` → `Normalize` → `store.Add`. В ответе `GET next` `serverTime = timeProvider.GetUtcNow().ToUnixTimeSeconds()`. Для Swagger — два примера тела (кухня и счёт).
- [ ] **Шаг 4:** `dotnet test backend/PrintPool.Api.Tests` — зелёные. Мутация: отдавать `quantity` числом → тест краснеет.
- [ ] **Шаг 5:** коммит по точным путям.

---

### Задача 3: ядро агента — ширина строки и новые настройки

**Files:**
- Modify: `firmware/PrintAgent.Core/EscPosBuilder.cs`
- Modify: `firmware/PrintAgent.Core/AgentConfig.cs`
- Test: `firmware/PrintAgent.Core.Tests/EscPosBuilderTests.cs`, `AgentConfigTests.cs` (дополнить)
- Modify (только чтобы собиралось): `firmware/PrintAgent/WorkMode.cs`, `firmware/PrintAgent/AdminServer.cs` — передать `config.LineWidth` в `Build`.

**Interfaces — Produces:**

```csharp
public static byte[] Build(ReceiptLine[] lines, int width);   // константа LineWidth удаляется
// AgentConfig
public int LineWidth;        // по умолчанию 48, допустимо 32..64
public int UtcOffsetHours;   // по умолчанию 5, допустимо -12..14
```

- [ ] **Шаг 1: падающие тесты:** `Build(divider, 32)` даёт 32 дефиса; `Build(pair, 32)` — строка 32 знака, при `Size = 2` — 16; `CreateDefault` → `LineWidth == 48`, `UtcOffsetHours == 5`; `Validate` отклоняет `LineWidth` 31 и 65, `UtcOffsetHours` −13 и 15, принимает 32, 64, −12, 14. Прежние тесты переводятся на `Build(lines, 48)`.
- [ ] **Шаг 2–4:** падение → реализация → `dotnet test firmware/PrintAgent.Core.Tests` зелёный; `firmware\build.ps1 -Project firmware\PrintAgent -NoDeploy` собирается.
- [ ] **Шаг 5:** коммит по точным путям.

Файл настроек платы (`ConfigStore`), панель и маршруты `/api/config` — задача 5.

---

### Задача 4: ядро агента — вёрстка чеков

**Files:**
- Create: `firmware/PrintAgent.Core/ReceiptData.cs`
- Create: `firmware/PrintAgent.Core/ReceiptFormatter.cs`
- Test: `firmware/PrintAgent.Core.Tests/ReceiptFormatterTests.cs`

**Interfaces — Consumes:** `ReceiptLine` (поля `Type`, `Text`, `Align`, `Bold`, `Size`, `Lines`). Не зависит от задачи 3. **Produces:**

```csharp
public class ReceiptItem { public string Name; public string Quantity; public int Sum; }
public class ReceiptData
{
    public string Title, BillNumber, Table, Hall, Cashier, Currency;
    public ReceiptItem[] Items;
    public int Subtotal, ServicePercent, ServiceSum, Total;
}

public static class ReceiptFormatter
{
    public static ReceiptLine[] Kitchen(ReceiptData data, string dateTime, int width);
    public static ReceiptLine[] Bill(ReceiptData data, string dateTime, int width);
    public static string FormatDateTime(int year, int month, int day, int hour, int minute); // "11.11.2026 22:53"
    public static string FormatMoney(int value);                                             // 126000 → "126 000"
    public static string[] Wrap(string text, int width);                                     // перенос по знакам; "" → одна пустая строка
}
```

Результат — строки типов `text`, `divider`, `feed`, `cut`. Строки по центру отдаются с `Align = "center"` и без дополняющих пробелов (центрирует принтер); строки таблицы и итогов — готовым текстом шириной ровно `width` с `Align = null`. Крупные строки — `Size = 2`.

- [ ] **Шаг 1: падающие тесты.** Ожидаемое берётся из макетов спеки («Кухня», «Счёт»), данные — из JSON-примеров спеки плюс позиция с длинным названием из макета:
  - Кухня, `W = 48`: последовательность `Text`/`Align`/`Size`/`Bold` всех строк совпадает с макетом; в частности `"SARBAST NEFILTR RAZL...1"` (`Size 2`, `Bold`) и следом `"IV"`; `"MNOGO MYASA 40SM.......2"`; шапка — `"11.11.2026 22:53"`, `"Hisob No: A20"`, `"Stol No: 12, Siniy zal"`, `"Kassir: Vlad"` с `Align = "center"`; в конце `feed` с `Lines = 3` и `cut`.
  - Кухня: `hall == ""` → `"Stol No: 12"`.
  - Счёт, `W = 48`: строки таблицы и итогов совпадают с макетом посимвольно, каждая длиной ровно 48: заголовок `Nomi/Soni/Summa`, `"MNOGO MYASA 40SM"` + количество в 5 + сумма в 12, перенос `"KARTOFEL PO-DEREVENSKI S SOUS"` / `"OM"`, `"Summa:"` с позиции 20, `"Xizmat: 10%"` с позиции 20; порядок шапки `Sana`, `Kassir`, `Hisob No`, `Stol No`; `"Jami: 353 100 sum"` — `Size 2`, `Bold`, по центру.
  - Счёт: `ServiceSum == 0` → строки `Xizmat` нет; `ServiceSum > 0`, `ServicePercent == 0` → `"Xizmat:"` без процента.
  - Счёт: `Title == ""` → первой строкой идёт `Sana`; `Title` длиннее `W / 2` → `Size 1`, `Bold`.
  - Счёт: `Jami`-строка длиннее `W / 2` → `Size 1`, `Bold`; `Currency == ""` → `"Jami: 353 100"`.
  - `W = 32`: колонка названия 13 знаков, все строки таблицы длиной ровно 32; `"Xizmat: 100%"` с суммой `999 999 999` — в строке ровно один пробел между надписью и суммой, длина 32.
  - `W = 33`: крупные строки не длиннее 16, строки таблицы длиной ровно 33.
  - Шапка: зал из 40 знаков при `W = 32` → строка стола разбита `Wrap` на части не длиннее 32, все с `Align = "center"`, склейка частей равна исходной строке.
  - Количество `"0,35"` в кухонной строке, название `"SHASHLIK MOL GOSHTI"`: `"SHASHLIK MOL GOSH...0,35"` (название до `24 − 4 − 3 = 17` знаков, три точки), следом `"TI"`.
  - `FormatMoney`: `0 → "0"`, `999 → "999"`, `1000 → "1 000"`, `999999999 → "999 999 999"`.
  - `FormatDateTime(2026, 1, 5, 9, 7) → "05.01.2026 09:07"`.
  - `Wrap("ABCDEFG", 3) → ["ABC", "DEF", "G"]`; `Wrap("", 3) → [""]`.
- [ ] **Шаг 2:** тесты не компилируются.
- [ ] **Шаг 3:** реализация по разделу спеки «Вид чеков». Надписи — константами в начале `ReceiptFormatter`.
- [ ] **Шаг 4:** `dotnet test firmware/PrintAgent.Core.Tests` зелёный; файл компилируется под nanoFramework: `firmware\build.ps1 -Project firmware\PrintAgent -NoDeploy`. Мутации (каждая отдельно, затем вернуть): ширина колонки суммы 11 вместо 12; порядок шапки счёта как у кухни; убрать сдвиг надписи влево при `W = 32`.
- [ ] **Шаг 5:** коммит по точным путям трёх файлов.

---

### Задача 5: программа платы

Выполняет ведущий: нужна плата на COM5. Тестов на компьютере нет — проверка сборкой, загрузкой и печатью.

**Files:**
- Modify: `firmware/PrintAgent/BackendClient.cs` — `JobDto` получает `type`, `serverTime` (`int`), `data` (`DataDto`: все поля спеки, `items` — `ItemDto { name, quantity, sum }`); `TryGetNext` отдаёт наружу тип, время сервера, строки и данные.
- Modify: `firmware/PrintAgent/WorkMode.cs` — подключение: сначала `ConnectDhcp(..., requiresDateTime: true, ...)` на 60 с, при неудаче — второй вызов с `false` на 60 с, и только после его неудачи режим настройки. В цикле печати: `type == "lines"` → как сейчас; `kitchen`/`bill` → время: `DateTime.UtcNow`, если год ≥ 2024, иначе `new DateTime(1970, 1, 1).AddSeconds(serverTime)`, если `serverTime > 0`, иначе `BAD_JOB`; плюс `UtcOffsetHours`; `ReceiptFormatter` → `EscPosBuilder.Build(lines, config.LineWidth)`. Неизвестный `type`, `data == null`, пустые `items` → `BAD_JOB`.
- Modify: `firmware/PrintAgent/ConfigStore.cs` — `lineWidth`, `utcOffsetHours` в `ConfigFile`; при значении 0 в старом файле ширина берётся по умолчанию. Часовой пояс 0 допустим, поэтому для него в файле нужен отдельный признак наличия (например, хранить `utcOffsetHours + 100`) — решить при реализации и записать в журнал.
- Modify: `firmware/PrintAgent/AdminServer.cs` — оба поля в `GET/POST /api/config`; в `/api/state` — `time` (строка местного времени или пусто, если не сверено); тестовая печать получает линейку `1234567890…` длиной `W`.
- Modify: `firmware/PrintAgent/admin.html` — «Ширина строки, знаков» в разделе «Принтер», «Часовой пояс, часов от UTC» и строка «Время» в разделе «Состояние»; проверка 32–64 и −12–14 в `check()`.

- [ ] Собрать и загрузить: `firmware\build.ps1 -Project firmware\PrintAgent -Port COM5`.
- [ ] Через API бэкенда: чек кухни и чек счёта из примеров спеки → `Printed`; задание `lines` → `Printed`; задание с неизвестным типом (если бэкенд не даёт создать — пропустить и записать).
- [ ] Страница панели открыта в браузере на ширине телефона: новые поля видны, сохраняются, после перезагрузки значения на месте.
- [ ] Журнал, коммит по точным путям.

---

### Задача 6: проверка на железе и итоговое ревью

- [ ] Раздел спеки «Проверка на железе» — с заказчиком: оба чека сверены с макетами; длинное название и сумма `999 999 999`; `lineWidth` 32 и обратно, линейка; дата и время совпадают с часами заказчика; дробное количество; прежний формат.
- [ ] Итоговое ревью ветки с момента `2669a32` (opus). Назвать риски: расхождение вёрстки с макетами; `null` в данных, дошедший до платы; поломка прежнего формата; время чека в неверном поясе.
- [ ] Исправления, повторная проверка, журнал.
