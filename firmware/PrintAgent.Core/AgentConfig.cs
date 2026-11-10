namespace PrintAgent.Core
{
    /// <summary>Настройки агента печати. Только поля и простые типы: код общий для nanoFramework и .NET 10.</summary>
    public class AgentConfig
    {
        public string DeviceId;      // GUID строкой
        public string WifiSsid;
        public string WifiPassword;
        public string BackendUrl;    // http://host:port, без завершающего /
        public string PrinterIp;
        public int PrinterPort;      // по умолчанию 9100
        public int PollSeconds;      // по умолчанию 5

        public static AgentConfig CreateDefault(string deviceId)
        {
            AgentConfig c = new AgentConfig();
            c.DeviceId = deviceId;
            c.WifiSsid = "";
            c.WifiPassword = "";
            c.BackendUrl = "";
            c.PrinterIp = "";
            c.PrinterPort = 9100;
            c.PollSeconds = 5;
            return c;
        }

        /// <summary>Возвращает null, если настройки корректны, иначе короткий текст ошибки с именем поля.</summary>
        public string Validate()
        {
            // IPv4: ровно четыре числа 0-255 через точку, разбор вручную по символам
            string ip = this.PrinterIp;
            bool ipOk = ip != null && ip.Length > 0;
            int octets = 0;
            int digits = 0;
            int value = 0;
            for (int i = 0; ipOk && i < ip.Length; i++)
            {
                char ch = ip[i];
                if (ch >= '0' && ch <= '9')
                {
                    value = value * 10 + (ch - '0');
                    digits++;
                    if (digits > 3 || value > 255)
                    {
                        ipOk = false;
                    }
                }
                else if (ch == '.' && digits > 0)
                {
                    octets++;
                    digits = 0;
                    value = 0;
                }
                else
                {
                    ipOk = false;
                }
            }

            if (!ipOk || digits == 0 || octets != 3)
            {
                return "PrinterIp: нужен адрес вида 192.168.1.50";
            }

            if (this.PrinterPort < 1 || this.PrinterPort > 65535)
            {
                return "PrinterPort: допустимо 1-65535";
            }

            if (this.PollSeconds < 1 || this.PollSeconds > 3600)
            {
                return "PollSeconds: допустимо 1-3600";
            }

            string url = this.BackendUrl;
            if (url == null || !url.StartsWith("http://") || url.Length == 7 || url[url.Length - 1] == '/')
            {
                return "BackendUrl: нужен адрес вида http://host:port без завершающего /";
            }

            if (this.WifiSsid == null || this.WifiSsid.Length < 1 || this.WifiSsid.Length > 32)
            {
                return "WifiSsid: длина 1-32 символа";
            }

            if (this.WifiPassword == null || (this.WifiPassword.Length != 0 && (this.WifiPassword.Length < 8 || this.WifiPassword.Length > 63)))
            {
                return "WifiPassword: пусто или 8-63 символа";
            }

            return null;
        }

        /// <summary>Заданы сеть, бэкенд и принтер.</summary>
        public bool IsComplete()
        {
            return this.WifiSsid != null && this.WifiSsid.Length > 0
                && this.BackendUrl != null && this.BackendUrl.Length > 0
                && this.PrinterIp != null && this.PrinterIp.Length > 0;
        }
    }
}
