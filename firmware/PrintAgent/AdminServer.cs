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
                int query = url.IndexOf('?');
                if (query >= 0)
                {
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
                    response.ContentType = "application/json; charset=utf-8";
                    WebServer.OutputAsStream(response,
                        "{\"mode\":" + Quote(mode)
                        + ",\"wifi\":" + Quote(network)
                        + ",\"ip\":" + Quote(address)
                        + ",\"deviceId\":" + Quote(config.DeviceId)
                        + ",\"backend\":" + Quote(Backend)
                        + ",\"printer\":" + Quote(printer)
                        + ",\"lastJob\":" + Quote(LastJob)
                        + ",\"lastError\":" + Quote(lastError) + "}");
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
                        + ",\"pollSeconds\":" + config.PollSeconds + "}");
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
                    candidate.WifiPassword = file.wifiPassword == null || file.wifiPassword.Length == 0 ? config.WifiPassword : file.wifiPassword;
                    candidate.BackendUrl = file.backendUrl;
                    candidate.PrinterIp = file.printerIp;
                    candidate.PrinterPort = file.printerPort;
                    candidate.PollSeconds = file.pollSeconds;

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
                        ReceiptLine[] lines = new ReceiptLine[7];
                        lines[0] = new ReceiptLine { Type = "text", Text = "PrintAgent", Align = "center", Bold = true, Size = 2 };
                        lines[1] = new ReceiptLine { Type = "text", Text = "Test print", Align = "center" };
                        lines[2] = new ReceiptLine { Type = "divider" };
                        lines[3] = new ReceiptLine { Type = "pair", Left = "Agent IP", Right = address };
                        lines[4] = new ReceiptLine { Type = "qr", Value = config.DeviceId };
                        lines[5] = new ReceiptLine { Type = "feed", Lines = 3 };
                        lines[6] = new ReceiptLine { Type = "cut" };
                        result = printer.Print(EscPosBuilder.Build(lines));
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
        private static string Quote(string text)
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
