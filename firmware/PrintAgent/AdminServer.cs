using System;
using System.Net;
using System.Text;
using System.Threading;
using nanoFramework.Json;
using nanoFramework.Runtime.Native;
using nanoFramework.WebServer;
using PrintAgent.Core;

namespace PrintAgent
{
    // Веб-сервер админ-панели: страница и её запросы. Работает в обоих режимах.
    public static class AdminServer
    {
        private const int MaxBodyBytes = 2048;

        private static AgentConfig config;
        private static string mode;
        private static string network;
        private static string address;

        // Состояние для панели, обновляется циклом опроса бэкенда.
        public static string Backend = "unknown";
        public static string LastJob = "";

        public static void Start(AgentConfig agentConfig, string agentMode, string networkName, string ip)
        {
            config = agentConfig;
            mode = agentMode;
            network = networkName;
            address = ip;

            WebServer server = new WebServer(80, HttpProtocol.Http);
            server.CommandReceived += OnCommand;
            server.Start();
        }

        private static void OnCommand(object sender, WebServerEventArgs e)
        {
            HttpListenerResponse response = e.Context.Response;
            try
            {
                string url = e.Context.Request.RawUrl;
                string queryText = "";
                int query = url.IndexOf('?');
                if (query >= 0)
                {
                    queryText = url.Substring(query + 1);
                    url = url.Substring(0, query);
                }

                string method = e.Context.Request.HttpMethod;

                if (method == "GET" && url == "/api/state")
                {
                    string printer = "unknown";
                    if (mode == "work")
                    {
                        string status = new PrinterClient(config.PrinterIp, config.PrinterPort).CheckStatus();
                        printer = status == null ? "ready" : status;
                    }

                    string lastError = ConfigStore.ReadText(ConfigStore.LastError);
                    // Время платы показывается, только если часы сверены с интернетом.
                    string time = "";
                    DateTime now = DateTime.UtcNow;
                    if (now.Year >= 2024)
                    {
                        now = now.AddHours(config.UtcOffsetHours);
                        time = ReceiptFormatter.FormatDateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute);
                    }

                    response.ContentType = "application/json; charset=utf-8";
                    WebServer.OutputAsStream(response,
                        "{\"mode\":" + Quote(mode)
                        + ",\"wifi\":" + Quote(network)
                        + ",\"ip\":" + Quote(address)
                        + ",\"deviceId\":" + Quote(config.DeviceId)
                        + ",\"backend\":" + Quote(Backend)
                        + ",\"printer\":" + Quote(printer)
                        + ",\"lastJob\":" + Quote(LastJob)
                        + ",\"lastError\":" + Quote(lastError)
                        + ",\"time\":" + Quote(time)
                        + ",\"uptime\":" + (Program.UptimeMs / 1000)
                        + ",\"boots\":" + Program.Boots
                        + ",\"freeMem\":" + Program.FreeMem + "}");
                    return;
                }

                // Изменяющие запросы принимаются только от самой страницы панели: она шлёт служебный заголовок.
                // Чужой сайт, открытый в браузере того же телефона, такой заголовок без разрешения платы послать не может.
                if (method == "POST" && e.Context.Request.Headers["X-PrintAgent"] != "1")
                {
                    Reply(response, 403, "Запрос принят только со страницы панели.");
                    return;
                }

                if (method == "GET" && url == "/api/config")
                {
                    // Пароль Wi-Fi наружу не отдаётся никогда.
                    response.ContentType = "application/json; charset=utf-8";
                    WebServer.OutputAsStream(response,
                        "{\"wifiSsid\":" + Quote(config.WifiSsid)
                        + ",\"wifiPassword\":\"\""
                        + ",\"hasPassword\":" + (config.WifiPassword != null && config.WifiPassword.Length > 0 ? "true" : "false")
                        + ",\"backendUrl\":" + Quote(config.BackendUrl)
                        + ",\"printerIp\":" + Quote(config.PrinterIp)
                        + ",\"printerPort\":" + config.PrinterPort
                        + ",\"pollSeconds\":" + config.PollSeconds
                        + ",\"lineWidth\":" + config.LineWidth
                        + ",\"printerName\":" + Quote(config.PrinterName)
                        + ",\"printerId\":" + Quote(config.PrinterId) + "}");
                    return;
                }

                if (method == "POST" && url == "/api/config")
                {
                    int length = (int)e.Context.Request.ContentLength64;
                    if (length <= 0 || length > MaxBodyBytes)
                    {
                        Reply(response, 400, "Пустой или слишком большой запрос.");
                        return;
                    }

                    byte[] body = new byte[length];
                    int read = 0;
                    while (read < length)
                    {
                        int n = e.Context.Request.InputStream.Read(body, read, length - read);
                        if (n <= 0)
                        {
                            break;
                        }

                        read += n;
                    }

                    ConfigFile file;
                    try
                    {
                        file = (ConfigFile)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(body, 0, read), typeof(ConfigFile));
                    }
                    catch (Exception)
                    {
                        Reply(response, 400, "Запрос не разобран.");
                        return;
                    }

                    // Проверяем копию: неверные значения не должны попасть в действующие настройки.
                    AgentConfig candidate = AgentConfig.CreateDefault(config.DeviceId);
                    candidate.WifiSsid = file.wifiSsid;
                    // Пустой пароль означает «оставить прежний» только для той же сети.
                    // При смене имени сети сохранённый пароль к новой сети не переносится:
                    // иначе плату можно было бы заставить предъявить его чужой точке доступа.
                    if (file.wifiPassword != null && file.wifiPassword.Length > 0)
                    {
                        candidate.WifiPassword = file.wifiPassword;
                    }
                    else
                    {
                        candidate.WifiPassword = file.wifiSsid == config.WifiSsid ? config.WifiPassword : "";
                    }
                    candidate.BackendUrl = file.backendUrl;
                    candidate.PrinterIp = file.printerIp;
                    candidate.PrinterPort = file.printerPort;
                    candidate.PollSeconds = file.pollSeconds;
                    candidate.LineWidth = file.lineWidth;
                    candidate.PrinterName = file.printerName == null ? "" : file.printerName.Trim();
                    if (candidate.PrinterName.Length > 40)
                    {
                        Reply(response, 400, "Название принтера: не длиннее 40 знаков.");
                        return;
                    }

                    // ID принтера назначает плата. Другой адрес или порт — это другой принтер, ему нужен новый ID.
                    candidate.PrinterId = config.PrinterId;
                    if (candidate.PrinterId == null || candidate.PrinterId.Length == 0
                        || candidate.PrinterIp != config.PrinterIp || candidate.PrinterPort != config.PrinterPort)
                    {
                        candidate.PrinterId = Guid.NewGuid().ToString();
                    }

                    string error = candidate.Validate();
                    if (error != null)
                    {
                        Reply(response, 400, error);
                        return;
                    }

                    ConfigStore.Save(candidate);
                    ConfigStore.Delete(ConfigStore.ModeFlag);
                    Reply(response, 200, "Сохранено. Плата перезагружается.");
                    new Thread(RebootSoon).Start();
                    return;
                }

                if (method == "POST" && url == "/api/device-id/new")
                {
                    config.DeviceId = Guid.NewGuid().ToString();
                    ConfigStore.Save(config);
                    Reply(response, 200, config.DeviceId);
                    return;
                }

                if (method == "GET" && url == "/api/scan")
                {
                    string found = "";
                    if (PortScanner.Found.Length > 0)
                    {
                        string[] ips = PortScanner.Found.Split(',');
                        for (int i = 0; i < ips.Length; i++)
                        {
                            found += (i == 0 ? "" : ",") + Quote(ips[i]);
                        }
                    }

                    response.ContentType = "application/json; charset=utf-8";
                    WebServer.OutputAsStream(response,
                        "{\"running\":" + (PortScanner.Running ? "true" : "false")
                        + ",\"done\":" + PortScanner.Done
                        + ",\"total\":" + PortScanner.Total
                        + ",\"stopped\":" + (PortScanner.StopRequested ? "true" : "false")
                        + ",\"found\":[" + found + "]"
                        + ",\"error\":" + Quote(PortScanner.Error) + "}");
                    return;
                }

                if (method == "POST" && url == "/api/scan/stop")
                {
                    PortScanner.StopRequested = true;
                    Reply(response, 200, "Поиск останавливается.");
                    return;
                }

                if (method == "POST" && url == "/api/scan/start")
                {
                    if (mode != "work")
                    {
                        Reply(response, 409, "Поиск принтеров заработает после подключения к Wi-Fi.");
                        return;
                    }

                    // Параметры идут в строке запроса: from=адрес&to=адрес&port=число.
                    string from = "";
                    string to = "";
                    int port = 0;
                    string[] pairs = queryText.Split('&');
                    for (int i = 0; i < pairs.Length; i++)
                    {
                        int eq = pairs[i].IndexOf('=');
                        if (eq < 0)
                        {
                            continue;
                        }

                        string key = pairs[i].Substring(0, eq);
                        string value = pairs[i].Substring(eq + 1);
                        if (key == "from")
                        {
                            from = value;
                        }
                        else if (key == "to")
                        {
                            to = value;
                        }
                        else if (key == "port")
                        {
                            try
                            {
                                port = int.Parse(value);
                            }
                            catch (Exception)
                            {
                                port = 0;
                            }
                        }
                    }

                    string scanError = PortScanner.Start(from, to, port);
                    if (scanError == null)
                    {
                        Reply(response, 200, "Поиск запущен.");
                    }
                    else
                    {
                        Reply(response, 400, scanError);
                    }

                    return;
                }

                if (method == "POST" && (url == "/api/printer/check" || url == "/api/printer/test"))
                {
                    if (mode != "work")
                    {
                        Reply(response, 409, "Проверка принтера заработает после подключения к Wi-Fi.");
                        return;
                    }

                    PrinterClient printer = new PrinterClient(config.PrinterIp, config.PrinterPort);
                    string result;
                    if (url == "/api/printer/check")
                    {
                        result = printer.CheckStatus();
                    }
                    else
                    {
                        // Линейка во всю ширину строки: по ней видно, верно ли задана ширина.
                        string ruler = "";
                        while (ruler.Length < config.LineWidth)
                        {
                            ruler += "1234567890";
                        }

                        ReceiptLine[] lines = new ReceiptLine[8];
                        lines[0] = new ReceiptLine { Type = "text", Text = "PrintAgent", Align = "center", Bold = true, Size = 2 };
                        lines[1] = new ReceiptLine { Type = "text", Text = "Test print", Align = "center" };
                        lines[2] = new ReceiptLine { Type = "divider" };
                        lines[3] = new ReceiptLine { Type = "pair", Left = "Agent IP", Right = address };
                        lines[4] = new ReceiptLine { Type = "text", Text = ruler.Substring(0, config.LineWidth) };
                        lines[5] = new ReceiptLine { Type = "qr", Value = config.DeviceId };
                        lines[6] = new ReceiptLine { Type = "feed", Lines = 3 };
                        lines[7] = new ReceiptLine { Type = "cut" };
                        result = printer.Print(EscPosBuilder.Build(lines, config.LineWidth));
                    }

                    if (result == null)
                    {
                        Reply(response, 200, url == "/api/printer/check" ? "Принтер готов." : "Тестовый чек отправлен.");
                    }
                    else
                    {
                        Reply(response, 502, result);
                    }

                    return;
                }

                if (method == "GET")
                {
                    // Любой другой адрес отдаёт страницу: так телефон сам открывает панель после подключения к сети платы.
                    response.ContentType = "text/html; charset=utf-8";
                    WebServer.OutputAsStream(response, AdminPage.Html);
                    return;
                }

                Reply(response, 404, "Нет такого адреса.");
            }
            catch (Exception ex)
            {
                try
                {
                    Reply(response, 500, ex.GetType().Name + ": " + ex.Message);
                }
                catch (Exception)
                {
                }
            }
        }

        // Ответ простым текстом с кодом состояния.
        private static void Reply(HttpListenerResponse response, int status, string text)
        {
            response.StatusCode = status;
            response.ContentType = "text/plain; charset=utf-8";
            WebServer.OutputAsStream(response, text);
        }

        // Строка JSON в кавычках с экранированием; null превращается в пустую строку.
        public static string Quote(string text)
        {
            if (text == null)
            {
                return "\"\"";
            }

            string result = "\"";
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\\')
                {
                    result += "\\" + c;
                }
                else if (c < ' ')
                {
                    result += " ";
                }
                else
                {
                    result += c;
                }
            }

            return result + "\"";
        }

        // Точка входа потока: даёт ответу уйти к телефону и перезагружает плату.
        private static void RebootSoon()
        {
            Thread.Sleep(2000);
            Power.RebootDevice();
        }
    }
}
