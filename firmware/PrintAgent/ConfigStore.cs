using System;
using System.IO;
using nanoFramework.Json;
using PrintAgent.Core;

namespace PrintAgent
{
    // Вид настроек в файле и в запросах админ-панели.
    // Имена свойств совпадают с ключами JSON буква в букву: разбор на плате чувствителен к регистру.
    public class ConfigFile
    {
        public string deviceId { get; set; }
        public string wifiSsid { get; set; }
        public string wifiPassword { get; set; }
        public string backendUrl { get; set; }
        public string printerIp { get; set; }
        public int printerPort { get; set; }
        public int pollSeconds { get; set; }
    }

    // Файлы агента на внутреннем диске платы.
    public static class ConfigStore
    {
        // Имена служебных файлов-признаков.
        public const string ModeFlag = "mode";          // "wifi" или "forced": при включении войти в режим настройки
        public const string ApTries = "ap-tries";       // счётчик перезагрузок ради точки доступа
        public const string LastError = "last-error";   // текст последнего необработанного исключения

        private const string Root = "I:\\";
        private const string ConfigPath = "I:\\config.json";

        // Возвращает null, если файла нет или он не разбирается: агент тогда входит в режим настройки.
        public static AgentConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return null;
                }

                ConfigFile file = (ConfigFile)JsonConvert.DeserializeObject(File.ReadAllText(ConfigPath), typeof(ConfigFile));
                if (file == null || file.deviceId == null || file.deviceId.Length == 0)
                {
                    return null;
                }

                AgentConfig config = AgentConfig.CreateDefault(file.deviceId);
                config.WifiSsid = file.wifiSsid;
                config.WifiPassword = file.wifiPassword;
                config.BackendUrl = file.backendUrl;
                config.PrinterIp = file.printerIp;
                if (file.printerPort > 0)
                {
                    config.PrinterPort = file.printerPort;
                }

                if (file.pollSeconds > 0)
                {
                    config.PollSeconds = file.pollSeconds;
                }

                return config;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Save(AgentConfig config)
        {
            ConfigFile file = new ConfigFile();
            file.deviceId = config.DeviceId;
            file.wifiSsid = config.WifiSsid;
            file.wifiPassword = config.WifiPassword;
            file.backendUrl = config.BackendUrl;
            file.printerIp = config.PrinterIp;
            file.printerPort = config.PrinterPort;
            file.pollSeconds = config.PollSeconds;
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(file));
        }

        // Возвращает null, если файла нет или он не читается.
        public static string ReadText(string name)
        {
            try
            {
                string path = Root + name + ".txt";
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void WriteText(string name, string text)
        {
            File.WriteAllText(Root + name + ".txt", text);
        }

        public static void Delete(string name)
        {
            string path = Root + name + ".txt";
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
