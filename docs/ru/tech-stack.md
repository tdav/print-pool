# Технологии и устройства

[← к оглавлению](../../README.ru.md) · [English version](../tech-stack.md)

## Устройства

| Устройство | Сведения | Роль |
|---|---|---|
| ESP32-S3-WROOM-1 N16R8 | Двухъядерный микроконтроллер, 16 МБ flash, 8 МБ PSRAM (Octal SPI), Wi-Fi 2,4 ГГц, встроенный USB | Агент печати: опрашивает бэкенд, верстает и печатает чеки, отдаёт админ-панель |
| Xprinter XP-Q80DA | Термопринтер чеков, бумага 80 мм, Ethernet (LAN), порт TCP 9100, команды ESC/POS, отрезчик | Печатает чеки |
| Роутер Wi-Fi | Любая сеть 2,4 ГГц, в которой находятся плата, принтер и компьютер с бэкендом | Связывает части |
| Телефон или компьютер | Любой браузер | Настройка платы через панель |

Цель прошивки nanoFramework для этой платы — `ESP32_S3_OCTAL`.

## Бэкенд

| Технология | Версия | Для чего |
|---|---|---|
| .NET SDK | 10.0 (закреплён в `global.json`: 10.0.302 или новее в пределах 10.0) | Сборка и выполнение |
| ASP.NET Core Minimal API | 10.0 | Методы HTTP |
| Swashbuckle.AspNetCore | 10.3.1 | Swagger и документ OpenAPI с примерами запросов |
| System.Text.Json | встроен | Работа с JSON |
| `TimeProvider` | встроен | Часы, подменяемые в тестах |

Очередь заданий хранится в памяти, базы данных нет.

## Программа платы

| Технология | Версия | Для чего |
|---|---|---|
| .NET nanoFramework | 1.18 (CoreLibrary 1.17.11) | Среда выполнения C# на микроконтроллере |
| nanoFramework.System.Device.Wifi | 1.5.152 | Wi-Fi: клиент и точка доступа |
| nanoFramework.System.Net, System.Net.Http | 1.11.64, 1.5.214 | Сокеты и HTTP-клиент бэкенда |
| nanoFramework.WebServer | 1.2.162 | HTTP-сервер админ-панели |
| nanoFramework.Json | 2.2.213 | Разбор заданий и файла настроек |
| nanoFramework.Iot.Device.DhcpServer | 1.2.1016 | Выдача адресов в режиме настройки |
| nanoFramework.Iot.Device.MulticastDns | 1.0.337 | Сетевое имя `myprintagent.local` |
| nanoFramework.System.IO.FileSystem | 1.1.96 | Настройки и журнал печати во внутренней памяти |
| nanoFramework.System.Device.Gpio | 1.1.64 | Светодиод и кнопка BOOT |

Протоколы: ESC/POS по TCP к принтеру, HTTP к бэкенду, mDNS для имени платы.

Админ-панель — одна страница HTML со встроенными стилями и скриптами, без фреймворков и обращений в интернет: в режиме настройки интернета у телефона нет.

## Инструменты

| Инструмент | Для чего |
|---|---|
| `csc.exe` (Roslyn из Visual Studio или Build Tools) | Компиляция программы платы с `/nostdlib` |
| nanoFramework Metadata Processor 3.0.104 | Перевод сборки в формат `.pe`, который исполняет плата |
| `nanoff` | Запись прошивки и загрузка программы по USB |
| `nuget.exe` | Пакеты nanoFramework |
| PowerShell | `firmware/build.ps1` связывает эти шаги |

## Тесты

| Технология | Версия | Для чего |
|---|---|---|
| xUnit | 2.9.3 | Оба тестовых проекта |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | Запуск бэкенда внутри теста (`WebApplicationFactory`) |
| Microsoft.Extensions.TimeProvider.Testing | 10.11.0 | `FakeTimeProvider` для таймаутов и меток времени |
| coverlet.collector | 6.0.4 | Покрытие кода |

Программу платы на компьютере не запустить, поэтому её логика без железа вынесена в `firmware/PrintAgent.Core` и компилируется дважды: для платы и для тестов под .NET 10.
