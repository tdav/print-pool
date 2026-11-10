# Пул печати — план реализации

> **Для исполнителей-агентов:** ОБЯЗАТЕЛЬНЫЙ СКИЛЛ: `superpowers:subagent-driven-development` (рекомендуется) или `superpowers:executing-plans`. Шаги отмечаются флажками `- [ ]`.

**Цель:** бэкенд на .NET 10 с очередью заданий печати и агент на ESP32-S3 (C#, nanoFramework), который забирает задания по Wi-Fi и печатает чеки на сетевом принтере Xprinter.

**Архитектура:** бэкенд хранит задания в памяти и отдаёт их агенту по его GUID; агент опрашивает бэкенд, собирает чек в команды ESC/POS и шлёт по TCP на IP и порт принтера из настроек. Настройка агента — через адаптивную веб-страницу, которую отдаёт сама плата. Логика без железа (сборка чека, проверка настроек, журнал печати) лежит в отдельных файлах, которые компилируются и под nanoFramework, и под .NET 10 для тестов.

**Стек:** ASP.NET Core (.NET 10), Swashbuckle.AspNetCore 10.3.1, xUnit; .NET nanoFramework 1.18.0.84 (`ESP32_S3_OCTAL`), `nanoff` 2.5.171, `csc` из Visual Studio 2026, `nanoFramework.Tools.MetadataProcessor.CLI` 3.0.104.

**Спека:** `docs/superpowers/specs/2026-11-10-print-pool-design.md` (редакция 2). Исполнитель читает её вместе с планом.

**Журнал хода работ:** `docs/superpowers/plans/2026-11-10-print-pool-progress.md`. Создаётся первой же задачей. В него пишутся не только отметки «сделано», но и выводы, меняющие следующие задачи: уточнённые сигнатуры, найденные ограничения, отчёт пробного запуска, данные окружения от заказчика.

## Общие ограничения

- Ветка `feature/print-pool`. Не переключать ветки, не пушить.
- Комментарии в коде и сообщения коммитов — на русском. Идентификаторы — на английском.
- Приватные поля без префикса `_`, обращение через `this.`. Private-методы-помощники не создавать без необходимости: логика публичного метода пишется в его теле.
- Коммит только по точным путям файлов: `git commit -m "..." -- <пути>`. `git add -A`, `git add .` и папки запрещены.
- Каждый коммит заканчивается строкой `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`.
- Чеки только ASCII. Символы вне диапазона 0x20–0x7E агент заменяет на `?`.
- Ширина строки чека — 48 символов, для `size: 2` — 24. Значение уточняется по самотесту принтера; хранится одной константой.
- Бэкенд: HTTP, порт **5080**, слушает `0.0.0.0`. Без авторизации. Задания только в памяти.
- Пределы задания: не больше 100 строк, тело не больше 8 КБ.
- Таймаут выданного задания — 60 с (настройка `PrintPool:DispatchTimeoutSeconds`). Период опроса агента — 5 с по умолчанию.
- В модальных окнах админ-панели кнопки справа, основная — крайняя справа.
- Тексты админ-панели на русском. Страница — один файл без внешних ресурсов.

## Окружение

| Что | Значение |
|---|---|
| Репозиторий | `E:\Works_Sata\raspberry`, Windows 11, PowerShell 7 |
| .NET SDK | 10.0.403 (есть также 8, 9, 11-rc) |
| Плата | ESP32-S3-WROOM-1 N16R8, разъём USB, **COM5** |
| Прошивка платы | `ESP32_S3_OCTAL` 1.18.0.84, уже записана |
| Порт бэкенда | 5080; с этого же компьютера обращаться по `127.0.0.1`, не `localhost` (бэкенд слушает только IPv4) |
| База данных, Docker | не нужны |
| Сеть Wi-Fi, адрес ПК, IP принтера | даёт заказчик, записываются в журнал хода работ, раздел «Окружение» |

**Плата одна и COM-порт один.** Все задачи с пометкой «железо» выполняются строго по очереди, в основной сессии, без параллельных субагентов.

**Известные особенности `nanoff` на этой плате** (проверено в этой же ветке работ):

- `--update` без `--nobackupconfig` обрывается с E4000 на копировании раздела настроек.
- После успешной записи `nanoff` всё равно завершается с E4000: порт пропадает при сбросе. Успех проверяется отдельным вызовом `nanoff --nanodevice --devicedetails --serialport COM5`, а не кодом возврата.
- Отладочного вывода (`Debug.WriteLine`) нет: расширение Visual Studio не установлено. Состояние агента видно только через его веб-страницу и светодиод TX (GPIO43).
- GPIO19/20 (USB) не занимать.

## Проверенные внешние API

Сверено по XML-документации пакетов и по байтам сборок 2026-11-10. Нативные версии всех пакетов ниже совпадают с прошивкой платы.

| Пакет | Версия | Что используется |
|---|---|---|
| `nanoFramework.CoreLibrary` | 1.17.11 | `System.Guid.NewGuid()`, `Guid.ToString()`, `System.Text.Encoding.UTF8` |
| `nanoFramework.System.Device.Wifi` | 1.5.152 | `nanoFramework.Networking.WifiNetworkHelper.ConnectDhcp(string ssid, string password, WifiReconnectionKind, bool requiresDateTime, int wifiAdapterId, CancellationToken)`, `.Status`, `.Reconnect(...)` |
| `nanoFramework.System.Net` | 1.11.64 | `System.Net.Sockets.Socket` (`Connect(EndPoint)`, `Send(byte[])`, `Receive(byte[])`, `SendTimeout`, `ReceiveTimeout`, `Close()`); `NetworkInterface.GetAllNetworkInterfaces()`, `.IPv4Address`, `.PhysicalAddress`, `.EnableStaticIPv4(string, string, string)`; `WirelessAPConfiguration` и `Wireless80211Configuration` (`GetAll...Configurations()`, `Ssid`, `Password`, `Authentication`, `Options`, `MaxConnections`, `SaveConfiguration()`) |
| `nanoFramework.Iot.Device.DhcpServer` | 1.2.1016 | `DhcpServer.Start(IPAddress, IPAddress, ushort)`, `.Stop()`, `.CaptivePortalUrl` |
| `nanoFramework.WebServer` | 1.2.162 | `WebServer(int port, HttpProtocol protocol)`, `.Start()`, `.Stop()`, событие `CommandReceived`, `WebServerEventArgs.Context`, `WebServer.OutPutStream(HttpListenerResponse, string)` |
| `nanoFramework.System.Net.Http.Client` | 1.5.214 | `HttpClient.Get(string)`, `.Post(string, HttpContent)`, `.Timeout`; `StringContent(string, Encoding, string)`; `HttpResponseMessage.StatusCode`, `.Content.ReadAsString()` |
| `nanoFramework.Json` | 2.2.213 | `JsonConvert.DeserializeObject(string, Type)`, `JsonConvert.SerializeObject(object)` |
| `nanoFramework.System.IO.FileSystem` | 1.1.96 | `File.Exists`, `File.ReadAllText`, `File.WriteAllText`, `File.Delete` |
| `nanoFramework.Runtime.Native` | 1.7.11 | `Power.RebootDevice(RebootOption)` |
| `nanoFramework.Hardware.Esp32` | 1.6.42 | `NativeMemory.GetMemoryInfo(MemoryType, out uint, out uint, out uint)` |
| `nanoFramework.System.Device.Gpio` | 1.1.64 | `GpioController.OpenPin`, `GpioPin.Read/Write` |
| `Swashbuckle.AspNetCore` | 10.3.1 | `AddEndpointsApiExplorer`, `AddSwaggerGen`, `UseSwagger`, `UseSwaggerUI` |

**Проверено пробным запуском 2026-11-10** (подробности и рабочий порядок вызовов — в журнале хода работ, раздел «Отчёт пробного запуска»): точка доступа с одной перезагрузкой, DHCP, веб-сервер, диск `I:`, GUID, команды принтера и QR. Разбор JSON на плате чувствителен к регистру: свойства классов называть в точности как ключи JSON, иначе поля молча остаются пустыми. **Ещё не проверено:** возврат из точки доступа в режим клиента, подключение к Wi-Fi роутера, HTTP-клиент, ответы принтера без бумаги и с открытой крышкой.

**Правило версий.** После любого изменения состава пакетов сверить нативные версии: строки вида `100.x.x.x` в `.dll` пакетов должны совпасть с выводом `nanoff --nanodevice --devicedetails --serialport COM5`. При расхождении программа загружается, но не выполняется, и сообщения об этом не будет.

## На что смотреть ревьюеру

Пять случаев, которые спека подразумевает, но прямо не описывает. Для каждого в задачах ниже есть тест или проверка.

1. **Итог печати доставлен, но ответ потерян.** Агент повторит отправку. Повтор того же итога от того же устройства бэкенд принимает (204), а не отклоняет; иначе агент застрянет в вечном повторе. Тест — задача 2.2.
2. **Бэкенд перезапущен, пока агент держит задание.** На итог придёт 404. Агент считает итог доставленным и идёт дальше. Тест — задача 6.1.
3. **Файл настроек повреждён** (обрыв питания при записи). Агент не падает, а входит в режим настройки. Тест — задача 3.1.
4. **Принтер принял соединение и пропал посреди передачи.** У сокета заданы таймауты; итог `SEND_FAILED`, зависания нет. Проверка — задача 7.1.
5. **Wi-Fi пропал в рабочем режиме.** Цикл опроса переживает исключения и восстанавливает связь; ни один путь цикла не завершает его молча. Проверка — задача 7.1, таблица путей — задача 6.2.

Молчаливые отказы этой технологии, которые нужно узнавать в лицо:

- несовпадение нативных версий — программа загружена, но не работает;
- необработанное исключение в `Main` — программа останавливается без сообщения;
- занятый вывод GPIO19/20 — плата пропадает с USB;
- задание с неизвестным полем — `nanoFramework.Json` может молча его пропустить.

## Что нужно от заказчика до этапов с железом

Этапы 1 и 3–7 не начинаются, пока эти данные не записаны в журнал хода работ.

1. **Принтер включён кабелем LAN в роутер.** Распечатка самотеста (держать `FEED` при включении): IP-адрес, маска, ширина строки в символах. Адрес постоянный и из подсети роутера.
2. **Имя сети Wi-Fi 2,4 ГГц** того же роутера. Пароль заказчик вписывает сам в файл `firmware/spike/Secrets.cs` (в Git не попадает) и позже вводит в админ-панели.
3. **Постоянный адрес тестового компьютера** в этой сети (закреплён в роутере).
4. **Правило брандмауэра Windows** на входящий TCP 5080. Заказчик создаёт сам; агент-исполнитель системные настройки не меняет.

Этап 2 и задачи 3.1, 5.1, 6.1 от этих данных не зависят.

## Структура файлов

```
backend/
  PrintPool.slnx
  PrintPool.Api/
    Program.cs               запуск, Swagger, порт 5080
    JobContracts.cs          записи запросов и ответов, JobStatus
    JobValidator.cs          проверка задания при добавлении
    JobStore.cs              очереди в памяти, состояния, таймаут
    JobEndpoints.cs          четыре метода API
    appsettings.json
  PrintPool.Api.Tests/
    JobStoreTests.cs
    JobValidatorTests.cs
    JobEndpointsTests.cs
firmware/
  build.ps1                  сборка и загрузка любого проекта агента
  PrintAgent.Core/           без зависимостей от железа, компилируется дважды
    ReceiptLine.cs           модель строки чека
    ByteBuffer.cs            растущий буфер байтов
    EscPosBuilder.cs         строки чека → байты ESC/POS
    PrinterStatus.cs         разбор ответов принтера о состоянии
    AgentConfig.cs           настройки и их проверка
    JobJournal.cs            решение «что делать с записью журнала при включении»
  PrintAgent.Core.Tests/     xUnit под .NET 10, включает файлы Core по ссылке
  PrintAgent/
    Program.cs               выбор режима, общий перехват исключений
    ConfigStore.cs           чтение и запись файла настроек
    SetupMode.cs             точка доступа, DHCP
    WorkMode.cs              Wi-Fi, цикл опроса
    AdminServer.cs           веб-сервер и маршруты админ-панели
    PrinterClient.cs         TCP к принтеру
    BackendClient.cs         HTTP к бэкенду
    admin.html               исходник страницы
    AdminPage.g.cs           создаётся build.ps1 из admin.html, в Git не попадает
    packages.config
  spike/                     пробный запуск, в Git не попадает
```

Файлы `PrintAgent.Core` пишутся на подмножестве C#, общем для nanoFramework и .NET 10: без обобщённых типов, LINQ, `Span`, `async`, интерполяции строк; коллекции — массивы и `ByteBuffer`.

---

## Этап 0. Репозиторий и ветка — выполнен

`main` содержит коммит `88353a1`, ветка `feature/print-pool` создана. Действий не требуется.

### Задача 0.1. Журнал хода работ и `.gitignore`

**Модель:** `haiku`. **Скиллы:** нет. **Параллельно:** можно с чем угодно.

**Файлы:** создать `docs/superpowers/plans/2026-11-10-print-pool-progress.md`; изменить `.gitignore`.

- [ ] Создать журнал с разделами: «Окружение» (пустая таблица: сеть Wi-Fi, адрес ПК, IP и порт принтера, ширина строки), «Отчёт пробного запуска», «Задачи» (список задач плана с флажками), «Выводы для следующих задач».
- [ ] Добавить в `.gitignore` строки `firmware/spike/` и `AdminPage.g.cs`.
- [ ] Проверка: `git status --short` показывает ровно два изменённых пути.
- [ ] Коммит: «Журнал хода работ и исключения для пробного запуска».

---

## Этап 1. Пробный запуск на ESP32-S3 — железо

Цель этапа — заменить наброски проверенными вызовами. Весь код этапа лежит в `firmware/spike/` и в Git не попадает, кроме `firmware/build.ps1`. Результаты видны через веб-страницу, которую отдаёт плата, потому что отладочного вывода нет.

**Параллельно:** этап 1 идёт одновременно с этапом 2 и задачами 3.1, 5.1, 6.1.

### Задача 1.1. Общий скрипт сборки и загрузки

**Модель:** `sonnet`. **Скиллы:** `superpowers:verification-before-completion`.

**Файлы:** создать `firmware/build.ps1`. Образец — `Blink/flash.ps1`, его не менять.

**Интерфейс:** `build.ps1 -Project <папка> -Port COM5 [-Firmware] [-NoDeploy]`.

- Собирает все `*.cs` из папки проекта и из `firmware/PrintAgent.Core/`, если в проекте есть файл-маркер `uses-core.txt`.
- Если в папке есть `admin.html`, перед компиляцией создаёт `AdminPage.g.cs` с классом `AdminPage` и константой `public const string Html`.
- Восстанавливает пакеты из `packages.config` проекта в `firmware/packages/`.
- Имя выходных файлов — по имени папки проекта, в её `bin\Debug\`.
- `-NoDeploy` останавливается после создания `.bin`: так сборку проверяют без платы.
- После загрузки вызывает `nanoff --nanodevice --devicedetails` и завершается ошибкой, если имени программы нет в списке сборок.

- [ ] Написать скрипт.
- [ ] Проверка без платы: `firmware/build.ps1 -Project Blink -NoDeploy` из корня создаёт `Blink\bin\Debug\Blink.bin` размером больше 60 КБ.
- [ ] Контрольный опыт: временно испортить `Blink/Program.cs` (убрать `;`), убедиться, что скрипт завершается ошибкой компиляции с ненулевым кодом; вернуть, `git diff -- Blink/Program.cs` пуст.
- [ ] Проверка с платой: `firmware/build.ps1 -Project Blink -Port COM5` — в выводе `Blink, 1.0.0.0`, зелёный светодиод TX мигает.
- [ ] Коммит: «Общий скрипт сборки и загрузки прошивки агента».

### Задача 1.2. Wi-Fi-клиент, веб-страница, принтер

**Модель:** `sonnet`, основная сессия. **Скиллы:** `fullstack-dev-skills:embedded-systems`, `superpowers:systematic-debugging`.

**Файлы:** `firmware/spike/NetSpike/Program.cs`, `packages.config`, `firmware/spike/Secrets.cs` (имя и пароль Wi-Fi, IP принтера; пароль вписывает заказчик).

Программа подключается к Wi-Fi, поднимает `WebServer` на порту 80 и по запросам выполняет опыты, отдавая результат текстом:

- `/` — адрес платы, свободная память (`NativeMemory.GetMemoryInfo`);
- `/print` — TCP на IP принтера, порт 9100: сброс, строка `SPIKE OK`, 3 пустые строки, отрезка;
- `/status` — запросы состояния `10 04 01`, `10 04 02`, `10 04 04`, ответы шестнадцатерично;
- `/qr` — QR-код встроенной командой принтера с текстом `https://example.com`.

Команды ESC/POS — из таблицы в задаче 5.1.

- [ ] Собрать и загрузить. Адрес платы взять из списка клиентов роутера.
- [ ] `/print`: на бумаге строка и отрезка.
- [ ] `/status` трижды: с бумагой и закрытой крышкой; с открытой крышкой; без бумаги. Записать все ответы.
- [ ] `/qr`: код читается телефоном.
- [ ] Контрольный опыт: `/print` при выключенном принтере. Записать, что вернулось и через сколько секунд.
- [ ] Записать в журнал, раздел «Отчёт пробного запуска»: работающий код подключения к Wi-Fi и к принтеру, таблицу «байт ответа → состояние», вывод о QR, свободную память.

**Остановка:** если принтер не отвечает на запросы состояния — записать это, в задаче 5.1 оставить только причины «не отвечает» и «сбой при передаче». Если не печатается QR — остановиться и спросить заказчика: спека требует убрать тип `qr` из формата.

### Задача 1.3. Точка доступа

**Модель:** `sonnet`, основная сессия. **Скиллы:** те же.

**Файлы:** `firmware/spike/ApSpike/Program.cs`, `packages.config`.

Программа поднимает сеть `PrintAgent-SPIKE` с паролем `printagent`, адрес `192.168.4.1`, DHCP-сервер, веб-страницу со строкой `AP OK`. Набросок порядка (не прогонялся, доверять отчёту после проверки): взять `WirelessAPConfiguration.GetAllWirelessAPConfigurations()[0]`, задать `Ssid`, `Password`, `Authentication = WPA2`, `MaxConnections`, `Options = AutoStart | Enable`, `SaveConfiguration()`; задать интерфейсу статический адрес; запустить `DhcpServer`. Возможно, после сохранения нужна перезагрузка.

- [ ] Собрать, загрузить, подключиться телефоном, открыть `http://192.168.4.1/`.
- [ ] Выяснить и записать: нужна ли перезагрузка после включения точки доступа; как вернуться в режим клиента (что выключить, нужна ли перезагрузка); работает ли страница, когда у телефона включён мобильный интернет.
- [ ] Записать в журнал работающий код включения и выключения точки доступа.

### Задача 1.4. Файл, JSON, GUID

**Модель:** `sonnet`, основная сессия.

**Файлы:** `firmware/spike/StoreSpike/Program.cs`, `packages.config`.

Программа при старте: создаёт GUID; пишет его в файл на внутреннем диске, читает обратно; разбирает строкой заданный JSON задания из спеки (с вложенным массивом `lines`) в классы; сериализует объект настроек. Результаты отдаёт страницей по Wi-Fi.

- [ ] Проверить: путь диска; файл переживает перезагрузку платы и перезапись программы (`build.ps1` без `-Firmware`); все семь строк задания разобраны с верными значениями полей; неизвестное поле в JSON не вызывает исключения.
- [ ] Контрольный опыт: разобрать обрезанный JSON. Записать, какое исключение брошено.
- [ ] Записать в журнал: путь диска, рабочий код чтения и записи, классы для разбора задания, поведение на битом JSON.
- [ ] Заменить в этом плане раздел «Не проверено» ссылкой на отчёт и закоммитить план и журнал: «Отчёт пробного запуска на ESP32-S3».

---

## Этап 2. Бэкенд — параллельно с этапом 1

**Окружение:** только .NET SDK 10. Плата и сеть не нужны. Тесты: `dotnet test backend/PrintPool.slnx`.

### Задача 2.1. Хранилище заданий

**Модель:** `sonnet`. **Скиллы:** `superpowers:test-driven-development`, `dotnet-claude-kit:testing`, `dotnet-test:run-tests`.

**Файлы:** создать `backend/PrintPool.slnx`, `backend/PrintPool.Api/PrintPool.Api.csproj`, `JobContracts.cs`, `JobStore.cs`; `backend/PrintPool.Api.Tests/PrintPool.Api.Tests.csproj`, `JobStoreTests.cs`.

**Создаёт интерфейс:**

```csharp
public enum JobStatus { Queued, Dispatched, Printed, Failed }

public sealed record ReceiptLineDto(string Type, string? Text, string? Align, bool? Bold,
    int? Size, string? Left, string? Right, string? Value, int? Lines);

public sealed record PrintJob(Guid JobId, Guid DeviceId, ReceiptLineDto[] Lines, JobStatus Status,
    string? Error, DateTimeOffset CreatedAt, DateTimeOffset? DispatchedAt, DateTimeOffset? FinishedAt);

public enum ResultOutcome { Accepted, JobNotFound, WrongDevice, WrongState }

public sealed class JobStore(TimeProvider timeProvider, TimeSpan dispatchTimeout)
{
    public PrintJob Add(Guid deviceId, ReceiptLineDto[] lines);
    public PrintJob? TakeNext(Guid deviceId);
    public ResultOutcome Complete(Guid jobId, Guid deviceId, bool success, string? error);
    public PrintJob? Find(Guid jobId);
}
```

Таймаут проверяется лениво: каждый публичный метод сначала переводит просроченные `Dispatched` в `Failed` с ошибкой `DEVICE_TIMEOUT`. Фоновой службы нет. Этот перевод вынесен в один private-метод: дублирование в четырёх методах признано «особой необходимостью» (решение по итогам ревью 2.1). Запись о таймауте помечается, чтобы поздний итог можно было принять. Все методы потокобезопасны под одной блокировкой.

- [ ] Создать решение и два проекта (`dotnet new web`, `dotnet new xunit`), добавить в тесты пакет с поддельным временем (`FakeTimeProvider`); имя и версию пакета сверить командой `dotnet package search`.
- [ ] Написать тесты, убедиться, что они падают:

| Тест | Дано → ожидается |
|---|---|
| `Add_ReturnsQueuedJobWithNewId` | `Add` → `Status == Queued`, `JobId != Guid.Empty` |
| `TakeNext_ReturnsJobsInAddOrder` | добавлены A, B; `TakeNext` → A; завершить A; `TakeNext` → B |
| `TakeNext_RepeatsDispatchedJobUntilCompleted` | `TakeNext` дважды подряд → оба раза A, B не выдан |
| `TakeNext_OtherDevice_ReturnsNull` | задание для D1; `TakeNext(D2)` → `null` |
| `Complete_Success_SetsPrinted` | `Complete(A, D1, true, null)` → `Accepted`, `Printed` |
| `Complete_Failure_KeepsErrorAndNextJobStillServed` | `Complete(A, …, false, "NO_PAPER")` → `Failed`, ошибка сохранена; `TakeNext` → B |
| `Complete_WrongDevice_Rejected` | → `WrongDevice`, состояние не изменилось |
| `Complete_QueuedJob_Rejected` | итог для невыданного задания → `WrongState` |
| `Complete_UnknownJob_NotFound` | → `JobNotFound` |
| `Dispatched_After60s_FailsWithDeviceTimeout` | время +61 с; `Find` → `Failed`, `DEVICE_TIMEOUT` |
| `Complete_AfterTimeout_SameDevice_Overrides` | после таймаута `Complete(…, true, null)` → `Accepted`, `Printed` |
| `Complete_RepeatedSameResult_Accepted` | второй такой же итог от того же устройства → `Accepted`, состояние прежнее |
| `Complete_AfterPrinted_DifferentResult_Rejected` | `Printed`, затем итог с ошибкой → `WrongState` |

- [ ] Реализовать `JobStore`, тесты зелёные.
- [ ] Доказательство мутацией: в `TakeNext` временно убрать повторную выдачу `Dispatched`-задания → `TakeNext_RepeatsDispatchedJobUntilCompleted` краснеет; вернуть, `git diff` пуст. То же для проверки устройства в `Complete`.
- [ ] Коммит: «Бэкенд: хранилище заданий печати с очередями по устройствам».

### Задача 2.2. Методы API, проверка задания, Swagger

**Модель:** `sonnet`. **Скиллы:** `superpowers:test-driven-development`, `dotnet-claude-kit:minimal-api`, `dotnet-claude-kit:openapi`.

**Файлы:** создать `JobValidator.cs`, `JobEndpoints.cs`, `appsettings.json`; изменить `Program.cs`; тесты `JobValidatorTests.cs`, `JobEndpointsTests.cs`.

**Использует:** `JobStore` из задачи 2.1.

**Создаёт контракт HTTP** (агент в задаче 6.2 опирается на него):

| Метод | Запрос | Ответы |
|---|---|---|
| `POST /api/jobs` | `{ "deviceId": guid, "lines": [...] }` | 201 `{ "jobId", "status": "Queued" }`; 400 с текстом причины |
| `GET /api/devices/{deviceId}/jobs/next` | — | 200 `{ "jobId", "lines": [...] }`; 204 |
| `POST /api/jobs/{jobId}/result` | `{ "deviceId": guid, "success": bool, "error": string? }` — `deviceId` и `success` обязательны, `error` не длиннее 200 символов, тело не больше 2048 байт | 204; 400; 404; 409; 413 |
| `GET /api/jobs/{jobId}` | — | 200 `{ jobId, deviceId, status, error, createdAt, dispatchedAt, finishedAt }`; 404 |

Имена полей JSON — camelCase, состояние — строкой. В ответе метода 2 пустые поля строк не передаются: так тело меньше для агента.

`JobValidator.Validate(ReceiptLineDto[] lines)` возвращает текст ошибки или `null`. Правила: от 1 до 100 строк; `type` из `text`, `pair`, `divider`, `qr`, `feed`, `cut`; `align` — `left`, `center`, `right` или отсутствует; `size` — 1, 2 или отсутствует; `text`, `left`, `right` не длиннее 200 символов; у `qr` `value` обязателен, от 1 до 300 символов ASCII; у `feed` `lines` от 1 до 10. Предел тела 8 КБ задаётся на уровне запроса и даёт 413.

- [ ] Написать тесты, убедиться, что падают. `JobValidatorTests` — по одному тесту на каждое правило плюс `ValidJobFromSpec_ReturnsNull` с заданием из спеки дословно. `JobEndpointsTests` через `WebApplicationFactory`:

| Тест | Ожидается |
|---|---|
| `PostJob_Valid_Returns201WithJobId` | 201, `status == "Queued"` |
| `PostJob_UnknownLineType_Returns400` | 400 |
| `PostJob_EmptyLines_Returns400` | 400 |
| `PostJob_BodyOver8Kb_Returns413` | 413 |
| `GetNext_NoJobs_Returns204` | 204 |
| `FullCycle_AddTakeCompleteStatus` | 201 → 200 → 204 → `status == "Printed"` |
| `PostResult_WrongDevice_Returns409` | 409 |
| `PostResult_UnknownJob_Returns404` | 404 |
| `PostResult_Repeated_Returns204` | второй такой же итог → 204 |

- [ ] Реализовать. В `Program.cs`: `JobStore` одиночкой с `TimeProvider.System`, Swagger, адрес `http://0.0.0.0:5080`.
- [ ] Тесты зелёные; `dotnet build backend/PrintPool.slnx` без предупреждений.
- [ ] Мутация: временно убрать проверку `type` в валидаторе → `PostJob_UnknownLineType_Returns400` краснеет; вернуть.
- [ ] Коммит: «Бэкенд: четыре метода API, проверка задания и Swagger».

### Задача 2.3. Проверка бэкенда рендером

**Модель:** основная сессия. **Скиллы:** `superpowers:verification-before-completion`, скилл `run`.

- [ ] Запустить бэкенд, запомнить PID своего процесса.
- [ ] Открыть `http://localhost:5080/swagger` во встроенном браузере. Сделать снимок: видны четыре метода, у `POST /api/jobs` раскрывается схема с массивом `lines`.
- [ ] Через Swagger пройти путь: добавить задание из спеки → забрать → отправить успех → состояние `Printed`.
- [ ] Подождать 61 с с выданным заданием без итога → состояние `Failed`, `DEVICE_TIMEOUT`.
- [ ] Остановить процесс по своему PID. Отметить этап 2 в журнале.

**Ревью этапа 2:** агент `dotnet-claude-kit:code-reviewer`, модель `sonnet`. Диф передать файлом. Риск назвать прямо: «найди путь, при котором одно задание выдаётся дважды или два устройства получают чужие задания; перечисли таблицей все переходы состояний, включая отклонённые». Предупредить: конец хунка не равен концу файла.

---

## Этап 3. Агент: настройки и режим настройки

### Задача 3.1. Настройки и их проверка — параллельно с этапом 1

**Модель:** `sonnet`. **Скиллы:** `superpowers:test-driven-development`.

**Файлы:** создать `firmware/PrintAgent.Core/AgentConfig.cs`, `firmware/PrintAgent.Core.Tests/PrintAgent.Core.Tests.csproj` (xUnit, .NET 10, включает `..\PrintAgent.Core\*.cs` по ссылке), `AgentConfigTests.cs`.

**Создаёт интерфейс:**

```csharp
public class AgentConfig
{
    public string DeviceId;      // GUID строкой
    public string WifiSsid;
    public string WifiPassword;
    public string BackendUrl;    // http://host:port, без завершающего /
    public string PrinterIp;
    public int PrinterPort;      // по умолчанию 9100
    public int PollSeconds;      // по умолчанию 5

    public static AgentConfig CreateDefault(string deviceId);
    public string Validate();    // null или текст ошибки на русском
    public bool IsComplete();    // заданы сеть, бэкенд и принтер
}
```

Правила `Validate`: `PrinterIp` — четыре числа 0–255 через точку; `PrinterPort` 1–65535; `PollSeconds` 1–3600; `BackendUrl` начинается с `http://` и не заканчивается на `/`; `WifiSsid` от 1 до 32 символов; `WifiPassword` пустой или от 8 до 63 символов.

- [ ] Тесты (падают): по одному на каждое правило с граничными значениями (`256.1.1.1`, порт `0` и `65536`, пароль из 7 символов, URL с `https://` и с `/` в конце), `CreateDefault_SetsPort9100AndPoll5`, `IsComplete_EmptySsid_False`.
- [ ] Реализовать. Тесты зелёные: `dotnet test firmware/PrintAgent.Core.Tests`.
- [ ] Проверка двойной компиляции: `firmware/build.ps1 -Project PrintAgent -NoDeploy` после задачи 3.2; до неё — отдельной командой `csc /nostdlib` против `mscorlib.dll` из `nanoFramework.CoreLibrary`, код возврата 0.
- [ ] Мутация: расширить допустимый октет до 999 → тест на `256.1.1.1` краснеет; вернуть.
- [ ] Коммит: «Агент: модель настроек и их проверка».

### Задача 3.2. Хранение настроек и выбор режима — железо

**Модель:** `sonnet`, основная сессия. **Скиллы:** `fullstack-dev-skills:embedded-systems`.

**Файлы:** создать `firmware/PrintAgent/Program.cs`, `ConfigStore.cs`, `packages.config`, `uses-core.txt`.

**Использует:** `AgentConfig`; путь диска и код работы с файлом — из отчёта пробного запуска, задача 1.4.

**Создаёт интерфейс:** `ConfigStore.Load()` возвращает `AgentConfig` или `null`, если файла нет **или он не разбирается**; `ConfigStore.Save(AgentConfig)`.

`Program.Main`: всё тело в `try/catch`; при необработанном исключении — пауза 5 с и перезагрузка. Выбор режима: нет настроек или `IsComplete() == false` → режим настройки; иначе рабочий. Если настроек нет, создаётся и сразу сохраняется новая запись с новым GUID: ID не должен меняться между включениями.

- [ ] Реализовать. Режимы пока заглушки: настройка — частое мигание TX (5 раз в секунду), рабочий — редкое (раз в 2 секунды).
- [ ] Загрузить. Проверка: на чистой плате TX мигает часто.
- [ ] Контрольный опыт на повреждённый файл: записать в файл настроек мусор (временной строкой в `Main`), перезагрузить → частое мигание, плата не зависла. Убрать строку.
- [ ] Коммит: «Агент: хранение настроек и выбор режима при включении».

### Задача 3.3. Точка доступа и веб-сервер — железо

**Модель:** `sonnet`, основная сессия.

**Файлы:** создать `firmware/PrintAgent/SetupMode.cs`, `AdminServer.cs`; изменить `Program.cs`.

**Использует:** рабочий код точки доступа из отчёта, задача 1.3.

**Создаёт маршруты** (общие для обоих режимов, обработка в одном обработчике `CommandReceived`):

| Маршрут | Назначение |
|---|---|
| `GET /` | страница `AdminPage.Html` |
| `GET /api/state` | `{ mode, wifi, ip, backend, printer, lastJob, deviceId }` |
| `GET /api/config` | настройки; `wifiPassword` всегда пустой |
| `POST /api/config` | проверка `Validate()`; при ошибке 400 с текстом; при успехе сохранить и перезагрузить через 2 с. Пустой `wifiPassword` означает «оставить прежний» |
| `POST /api/device-id/new` | новый GUID, сохранить, вернуть его |
| `POST /api/printer/check` | в режиме настройки — 409 с пояснением; в рабочем — задача 5.2 |
| `POST /api/printer/test` | то же |

Имя сети — `PrintAgent-` и четыре последних шестнадцатеричных знака MAC-адреса. Пароль по умолчанию — `printagent`. Таймер: если режим настройки включился из-за недоступного Wi-Fi и за 5 минут настройки не сохранены — перезагрузка.

- [ ] Реализовать; страница на этом шаге — простая форма без оформления.
- [ ] Загрузить. С телефона: подключиться к сети платы, открыть `http://192.168.4.1/`, задать сеть и принтер, сохранить.
- [ ] Проверка: после перезагрузки TX мигает редко — рабочий режим.
- [ ] Контрольный опыт: отправить порт `70000` → ответ 400 с текстом, настройки не изменились.
- [ ] Коммит: «Агент: режим настройки с точкой доступа и веб-сервером».

---

## Этап 4. Агент: админ-панель в рабочем режиме — железо

### Задача 4.1. Wi-Fi, состояние, кнопка BOOT

**Модель:** `sonnet`, основная сессия.

**Файлы:** создать `firmware/PrintAgent/WorkMode.cs`; изменить `Program.cs`, `AdminServer.cs`.

- Подключение к Wi-Fi кодом из отчёта (задача 1.2). Не удалось за 60 с → режим настройки с пометкой «из-за Wi-Fi».
- После подключения запускается тот же `AdminServer`.
- Отдельный поток следит за GPIO0 (кнопка BOOT, нажата = низкий уровень): 3 секунды подряд → записать признак «принудительная настройка» и перезагрузиться. `Program` при включении читает и сбрасывает этот признак.
- `/api/state` отдаёт настоящие данные: режим, имя сети, адрес платы.

- [ ] Реализовать, загрузить.
- [ ] Проверка: `http://<адрес платы>/api/state` с компьютера отдаёт режим `work` и верный адрес.
- [ ] Проверка: держать BOOT 3 с → появляется сеть `PrintAgent-XXXX`; сохранить настройки → плата снова в рабочем режиме.
- [ ] Контрольный опыт: выключить роутер на 90 с → появилась сеть настройки; включить роутер, ничего не сохранять → через 5 минут плата сама вернулась в рабочий режим.
- [ ] Коммит: «Агент: рабочий режим, состояние и вход в настройку кнопкой BOOT».

### Задача 4.2. Страница админ-панели

**Модель:** `sonnet`. **Скиллы:** `impeccable`, `frontend-design:frontend-design`, `design:accessibility-review`.

**Файлы:** изменить `firmware/PrintAgent/admin.html`.

**Использует:** маршруты из задачи 3.3. Состав и раскладка — раздел «Админ-панель» спеки, включая абзац «Раскладка, одобренная заказчиком по макету».

Требования к файлу: одна колонка от 320 пикселей; поля и кнопки не ниже 44 пикселей; размер шрифта в полях не меньше 16 пикселей (иначе телефон увеличивает страницу при вводе); `inputmode="decimal"` для IP, `numeric` для порта и периода, `url` для адреса бэкенда; системный шрифт; светлая и тёмная тема по `prefers-color-scheme`; без внешних файлов; размер не больше 20 КБ. Сохранение и проверки — через `fetch`, без перезагрузки страницы. «Создать новый ID» — с подтверждением в окне, кнопки справа, основная крайняя справа.

- [ ] Сверстать. Загрузить на плату.
- [ ] **Рендер:** открыть `http://<адрес платы>/` во встроенном браузере, размер 375×812, светлая и тёмная тема — два снимка. Проверить: нет прокрутки вбок; пять блоков в порядке из спеки; метки состояния цветные.
- [ ] Рендер при 320×568 и при 1280×800: ничего не обрезано.
- [ ] **Телефон:** открыть страницу на настоящем телефоне. Ввести в каждое поле по одному символу, читая поле после каждого. Снимок экрана с открытой клавиатурой: у IP и порта клавиатура цифровая, кнопка «Сохранить и перезагрузить» достижима прокруткой, страница не увеличилась.
- [ ] Контрольный опыт: ввести IP `300.1.1.1` → сообщение об ошибке рядом с полем, запрос на плату не ушёл.
- [ ] В режиме настройки: кнопки проверки принтера отключены, рядом пояснение.
- [ ] Коммит: «Агент: адаптивная админ-панель».

**Ревью этапов 3–4:** агент `voltagent-qa-sec:code-reviewer`, модель `sonnet`. Риск: «найди путь, при котором плата после неудачного сохранения или смены пароля Wi-Fi остаётся без доступа к настройке; проверь, что пароль Wi-Fi не возвращается ни одним маршрутом».

---

## Этап 5. Агент: печать

### Задача 5.1. Сборка чека в команды принтера — параллельно с этапом 1

**Модель:** `sonnet`. **Скиллы:** `superpowers:test-driven-development`.

**Файлы:** создать в `firmware/PrintAgent.Core/`: `ReceiptLine.cs`, `ByteBuffer.cs`, `EscPosBuilder.cs`, `PrinterStatus.cs`; в тестах: `EscPosBuilderTests.cs`, `PrinterStatusTests.cs`.

**Создаёт интерфейс:**

```csharp
public class ReceiptLine
{
    public string Type; public string Text; public string Align; public bool Bold;
    public int Size; public string Left; public string Right; public string Value; public int Lines;
}

public static class EscPosBuilder
{
    public const int LineWidth = 48;
    public static byte[] Build(ReceiptLine[] lines);          // бросает ArgumentException на неизвестном типе
    public static string Sanitize(string text);               // null → "", не-ASCII → '?'
    public static string FormatPair(string left, string right, int width);
}

public static class PrinterStatus
{
    public static readonly byte[] QueryOffline;   // 10 04 02
    public static readonly byte[] QueryPaper;     // 10 04 04
    public static string Decode(int offlineByte, int paperByte); // null — готов, иначе "COVER_OPEN" / "NO_PAPER"
}
```

Команды ESC/POS (стандарт; поведение на этой модели подтверждает задача 1.2):

| Действие | Байты |
|---|---|
| Сброс, первым в каждом чеке | `1B 40` |
| Выравнивание: слева / центр / справа | `1B 61 00` / `01` / `02` |
| Жирный: вкл / выкл | `1B 45 01` / `00` |
| Размер: обычный / двойной | `1D 21 00` / `11` |
| Перевод строки | `0A` |
| Пустые строки, n | `1B 64 n` |
| Отрезка | `1D 56 42 00` |
| QR: модель | `1D 28 6B 04 00 31 41 32 00` |
| QR: размер точки 6 | `1D 28 6B 03 00 31 43 06` |
| QR: уровень коррекции M | `1D 28 6B 03 00 31 45 31` |
| QR: данные, длина L = число байт + 3 | `1D 28 6B pL pH 31 50 30` + данные |
| QR: печать | `1D 28 6B 03 00 31 51 30` |

Правила: после каждой строки `text` и `pair` состояние возвращается к обычному (выравнивание слева, жирный выкл, размер обычный). `divider` — 48 дефисов. `FormatPair`: левая часть, пробелы, правая часть, всего ровно `width`; если не помещается, левая обрезается так, чтобы между частями остался один пробел; если правая сама длиннее `width - 1`, она обрезается до `width`, левая пропадает. Для `size: 2` ширина 24. QR печатается по центру. `PrinterStatus.Decode` — по таблице «байт → состояние» из отчёта задачи 1.2; до отчёта принять стандарт: бит 2 (0x04) в ответе на `10 04 02` — открыта крышка, биты 5–6 (0x60) в ответе на `10 04 04` — нет бумаги.

- [ ] Тесты (падают):

| Тест | Ожидается |
|---|---|
| `Build_StartsWithReset` | первые два байта `1B 40` |
| `Text_CenterBoldDouble_EmitsCommandsThenResets` | `1B 61 01`, `1B 45 01`, `1D 21 11`, текст, `0A`, затем `1D 21 00`, `1B 45 00`, `1B 61 00` |
| `Divider_Is48Dashes` | 48 байт `2D` и `0A` |
| `Pair_PadsToWidth` | `FormatPair("TOTAL", "45 000", 48)` — длина 48, начало `TOTAL`, конец `45 000` |
| `Pair_LongLeft_TruncatedKeepingOneSpace` | левая 60 символов, правая `9.99` → длина 48, перед `9.99` пробел |
| `Pair_Size2_UsesWidth24` | длина строки 24 |
| `Sanitize_NonAscii_ReplacedWithQuestionMark` | `"Чай"` → `"???"`; `null` → `""` |
| `Qr_StoresDataWithCorrectLength` | значение 19 символов → `pL = 22`, `pH = 0` |
| `Feed_EmitsEscD` | `lines: 3` → `1B 64 03` |
| `Cut_EmitsPartialCut` | `1D 56 42 00` |
| `Build_UnknownType_Throws` | `ArgumentException` |
| `Build_SpecExample_EndsWithCut` | задание из спеки целиком → последние 4 байта — отрезка |
| `Decode_Ready_ReturnsNull`, `Decode_CoverOpen`, `Decode_NoPaper` | по таблице состояний |

- [ ] Реализовать, тесты зелёные.
- [ ] Мутация: убрать сброс состояния после строки → `Text_CenterBoldDouble_EmitsCommandsThenResets` краснеет; вернуть. Поменять `pL` на длину без `+3` → `Qr_StoresDataWithCorrectLength` краснеет; вернуть.
- [ ] После отчёта задачи 1.2 сверить `Decode` и ширину строки с отчётом, поправить код и тесты.
- [ ] Коммит: «Агент: сборка чека в команды ESC/POS».

### Задача 5.2. Связь с принтером — железо

**Модель:** `sonnet`, основная сессия.

**Файлы:** создать `firmware/PrintAgent/PrinterClient.cs`; изменить `AdminServer.cs`.

**Создаёт интерфейс:**

```csharp
public class PrinterClient
{
    public PrinterClient(string ip, int port);
    public string CheckStatus();        // null — готов; иначе код причины
    public string Print(byte[] data);   // null — отправлено; иначе код причины
}
```

Коды причин: `PRINTER_UNREACHABLE` (нет соединения), `NO_PAPER`, `COVER_OPEN`, `SEND_FAILED` (исключение или таймаут при передаче). Таймауты сокета на подключение, отправку и приём — 5 с. `Print` сам вызывает проверку состояния перед отправкой и закрывает соединение в любом исходе. У каждого пути метода есть возвращаемое значение; пути, который молча возвращает «успех» после исключения, быть не должно.

`/api/printer/check` возвращает результат `CheckStatus`; `/api/printer/test` печатает пробный чек: название, ID устройства, адрес платы, QR с ID, отрезка.

- [ ] Реализовать, загрузить.
- [ ] Проверка: «Проверить связь» в панели → «Принтер готов». «Тестовая печать» → чек на бумаге, QR читается.
- [ ] Коммит: «Агент: связь с принтером, проверка и тестовая печать».

---

## Этап 6. Агент: работа с бэкендом

### Задача 6.1. Журнал печати — параллельно с этапом 1

**Модель:** `sonnet`. **Скиллы:** `superpowers:test-driven-development`.

**Файлы:** создать `firmware/PrintAgent.Core/JobJournal.cs`, тесты `JobJournalTests.cs`.

**Создаёт интерфейс:**

```csharp
public class JournalEntry { public string JobId; public string State; public bool Success; public string Error; }

public static class JobJournal
{
    public const string Started = "started";
    public const string Finished = "finished";
    public static JournalEntry OnBoot(JournalEntry saved);         // что сообщить бэкенду при включении, или null
    public static bool IsDelivered(int httpStatus);                 // итог можно больше не повторять
}
```

`OnBoot`: `null` или запись без ID задания → `null`; запись с состоянием ровно `finished` → она же (итог не был доставлен); запись с любым другим состоянием (`started`, неизвестное, пустое, `null`, другой регистр) → новая запись с `Success = false`, `Error = "REBOOT_DURING_PRINT"`. Отказ в безопасную сторону: испорченная запись не должна привести ко второму чеку (исправлено по итогам ревью 6.1). `IsDelivered`: 204, 404 и 409 — да; остальное, включая отсутствие ответа (0), — нет.

- [ ] Тесты (падают): `OnBoot_Null_ReturnsNull`; `OnBoot_Finished_ReturnsSameResult`; `OnBoot_Started_ReturnsRebootFailure`; `IsDelivered_204_404_409_True`; `IsDelivered_500_And_0_False`.
- [ ] Реализовать, тесты зелёные.
- [ ] Мутация: вернуть из `OnBoot` для `started` значение `null` → тест краснеет; вернуть.
- [ ] Коммит: «Агент: журнал печати для защиты от двойного чека».

### Задача 6.2. Цикл опроса — железо

**Модель:** `sonnet`, основная сессия. **Скиллы:** `fullstack-dev-skills:embedded-systems`, `superpowers:systematic-debugging`.

**Окружение:** бэкенд запущен на тестовом компьютере, порт 5080; адрес компьютера — из журнала; правило брандмауэра создано заказчиком.

**Файлы:** создать `firmware/PrintAgent/BackendClient.cs`; изменить `WorkMode.cs`, `AdminServer.cs`.

**Использует:** контракт HTTP из задачи 2.2; классы разбора задания из отчёта задачи 1.4; `EscPosBuilder`, `PrinterClient`, `JobJournal`.

**Создаёт интерфейс:**

```csharp
public class BackendClient
{
    public BackendClient(string baseUrl, string deviceId);
    public bool TryGetNext(out string jobId, out ReceiptLine[] lines, out bool reachable);
    public int PostResult(string jobId, bool success, string error);   // код HTTP, 0 — нет ответа
}
```

Порядок цикла:

1. При входе в рабочий режим: прочитать журнал, `JobJournal.OnBoot`; если есть итог — доставлять его до `IsDelivered`, затем очистить журнал.
2. `TryGetNext`. Нет задания — пауза `PollSeconds`.
3. Задание есть: записать в журнал `started` → `EscPosBuilder.Build` (исключение → итог `BAD_JOB`) → `PrinterClient.Print` → записать в журнал `finished` с итогом.
4. `PostResult` до `IsDelivered`, пауза между попытками `PollSeconds`. Очистить журнал.
5. Любое исключение внутри итерации перехватывается, итерация завершается паузой; цикл не прекращается.

Уточнения по итогам ревью бэкенда: в итоге всегда передаются `deviceId` и `success`; текст `error` — один из коротких кодов причин, не длиннее 200 символов. Ответ 400 на итог означает ошибку в самом запросе: после трёх таких ответов подряд агент прекращает повтор, очищает журнал и показывает причину в панели, иначе повтор был бы вечным. Ответ 404 на запрос задания означает неверный адрес или ID устройства, а не пустую очередь: панель показывает «Бэкенд: ошибка адреса».

Состояние для `/api/state`: связь с бэкендом (по флагу `reachable`), последнее задание и его итог со временем.

- [ ] Реализовать, загрузить.
- [ ] Составить в журнале хода работ таблицу всех путей одной итерации (включая: бэкенд недоступен, 204, битый JSON, неизвестный тип строки, принтер не готов, итог не доставлен) с тем, чем каждый путь заканчивается. Пути без исхода быть не должно.
- [ ] Проверка: добавить через Swagger задание из спеки с ID устройства из панели → чек напечатан за время не больше 10 с, состояние `Printed`.
- [ ] Проверка порядка: добавить подряд три задания с текстами `1`, `2`, `3` → чеки вышли в этом порядке, каждый один раз.
- [ ] Панель: блок «Состояние» показывает «Бэкенд на связи» и последнее задание.
- [ ] Коммит: «Агент: получение заданий из бэкенда и отчёт о печати».

---

## Этап 7. Проверка ошибок — железо

### Задача 7.1. Сценарии отказов

**Модель:** основная сессия. **Скиллы:** `superpowers:verification-before-completion`, `superpowers:systematic-debugging`.

Каждый сценарий — отдельная строка в журнале: что сделано, что напечатано, какое состояние задания в бэкенде.

| № | Сценарий | Ожидается |
|---|---|---|
| 1 | Принтер выключен, добавить задание | `Failed`, `PRINTER_UNREACHABLE`, не дольше 15 с |
| 2 | Нет бумаги, добавить два задания | оба `Failed`, `NO_PAPER`; после заправки бумаги ничего не печатается |
| 3 | Открыта крышка | `Failed`, `COVER_OPEN` |
| 4 | Выдернуть кабель принтера во время длинного чека (100 строк) | `Failed`, `SEND_FAILED`; агент продолжает опрос |
| 5 | Отключить питание платы во время печати длинного чека, включить | `Failed`, `REBOOT_DURING_PRINT`; второго экземпляра чека нет |
| 6 | Остановить бэкенд на минуту с заданием в очереди, запустить | панель показывала «Бэкенд недоступен»; агент продолжил опрос без перезагрузки |
| 7 | Перезапустить бэкенд, пока агент печатает | агент получил 404 на итог, очистил журнал, продолжает опрос |
| 8 | Выключить роутер на 30 с | агент восстановил связь сам, в режим настройки не ушёл |
| 9 | Задание для чужого GUID | агент его не получил |
| 10 | Задание с `text` из кириллицы | на чеке знаки `?`, состояние `Printed` |

- [ ] Пройти все десять сценариев. Несовпадение — разбор по `superpowers:systematic-debugging`, исправление в коде **и** в этом плане, если причина в его формулировке.
- [ ] Полная проверка: `dotnet build backend/PrintPool.slnx`, `dotnet test backend/PrintPool.slnx`, `dotnet test firmware/PrintAgent.Core.Tests` — всё зелёное.
- [ ] Коммит исправлений и журнала: «Проверка сценариев отказов».

### Задача 7.2. Итоговое ревью ветки

**Модель:** `opus`. **Агент:** `dotnet-claude-kit:code-reviewer` или `voltagent-qa-sec:code-reviewer`. **Скилл:** `superpowers:requesting-code-review`.

- [ ] Передать ревьюеру диф `main..feature/print-pool` файлом, спеку, этот план и журнал. Риски назвать прямо: двойная печать; потерянный или застрявший итог; утечка пароля Wi-Fi через маршруты панели; путь цикла опроса без исхода; расхождение контракта HTTP между `JobEndpoints.cs` и `BackendClient.cs`. Разрешить читать смежные файлы; предупредить, что конец хунка не равен концу файла.
- [ ] Спорные находки проверить самому до исправления.
- [ ] После исправлений — скилл `superpowers:finishing-a-development-branch`. Слияние в `main` и пуш — только по слову заказчика.

---

## Порядок и параллельность

```
0.1 ──┬── Этап 2 (2.1 → 2.2 → 2.3)          ┐
      ├── 3.1, 5.1, 6.1  (чистый код, тесты) ├─ параллельно, до 5 агентов
      └── Этап 1 (1.1 → 1.2 → 1.3 → 1.4)     ┘  железо — в основной сессии
                    │
                    v
      3.2 → 3.3 → 4.1 → 4.2 → 5.2 → 6.2 → 7.1 → 7.2     строго по очереди
```

Параллельным агентам в одном рабочем дереве: коммит только своих файлов по точным путям; свой PID для своих процессов; при блокировке вывода сборки — собирать в отдельный каталог через `-o`; порт 5080 занимает только задача 2.3.
