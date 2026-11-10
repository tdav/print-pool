namespace PrintAgent
{
    // Настройки по умолчанию для новой платы: применяются, когда файла настроек ещё нет.
    // Имя и пароль Wi-Fi лежат в Secrets.cs, который в Git не попадает (образец — Secrets.cs.example).
    public static class Defaults
    {
        // Сетевое имя платы: в рабочем режиме панель открывается по адресу http://myprintagent.local/
        public const string HostName = "MyPrintAgent";

        public const string PrinterIp = "192.168.1.153";
        public const int PrinterPort = 9100;
        public const string BackendUrl = "http://192.168.1.210:5080";
        public const string WifiSsid = Secrets.WifiSsid;
        public const string WifiPassword = Secrets.WifiPassword;
    }
}
