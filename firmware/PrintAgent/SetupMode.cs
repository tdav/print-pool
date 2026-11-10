using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Iot.Device.DhcpServer;
using nanoFramework.Runtime.Native;
using PrintAgent.Core;

namespace PrintAgent
{
    // Режим настройки: плата раздаёт собственную сеть Wi-Fi и показывает админ-панель по адресу 192.168.4.1.
    public static class SetupMode
    {
        public const string ApIp = "192.168.4.1";
        private const string ApPassword = "printagent";
        private const int WifiRetrySeconds = 300;

        // dueToWifi: режим включён из-за недоступного Wi-Fi; через 5 минут без сохранения агент пробует Wi-Fi снова.
        public static void Run(AgentConfig config, bool dueToWifi)
        {
            Program.BlinkMs = 100;

            NetworkInterface ap = null;
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.WirelessAP)
                {
                    ap = ni;
                }
            }

            if (ap == null)
            {
                throw new InvalidOperationException("no WirelessAP interface");
            }

            // Имя сети: PrintAgent- и четыре последних шестнадцатеричных знака MAC-адреса.
            byte[] mac = ap.PhysicalAddress;
            string hex = "0123456789ABCDEF";
            string ssid = "PrintAgent-"
                + hex[mac[mac.Length - 2] >> 4] + hex[mac[mac.Length - 2] & 15]
                + hex[mac[mac.Length - 1] >> 4] + hex[mac[mac.Length - 1] & 15];

            WirelessAPConfiguration conf = WirelessAPConfiguration.GetAllWirelessAPConfigurations()[ap.SpecificConfigId];
            WirelessAPConfiguration.ConfigurationOptions wanted =
                WirelessAPConfiguration.ConfigurationOptions.Enable | WirelessAPConfiguration.ConfigurationOptions.AutoStart;

            if (conf.Options != wanted || conf.Ssid != ssid || ap.IPv4Address != ApIp)
            {
                // Настройки точки доступа применяются только после перезагрузки.
                // Счётчик не даёт уйти в бесконечный цикл перезагрузок, если точка так и не поднялась.
                string saved = ConfigStore.ReadText(ConfigStore.ApTries);
                int tries = saved == null ? 0 : int.Parse(saved);
                if (tries >= 2)
                {
                    ConfigStore.Delete(ConfigStore.ApTries);
                    throw new InvalidOperationException("access point did not start after 2 reboots");
                }

                ConfigStore.WriteText(ConfigStore.ApTries, (tries + 1).ToString());
                ap.EnableStaticIPv4(ApIp, "255.255.255.0", ApIp);
                conf.Options = wanted;
                conf.Ssid = ssid;
                conf.Password = ApPassword;
                conf.Authentication = System.Net.NetworkInformation.AuthenticationType.WPA2;
                conf.MaxConnections = 2;
                conf.SaveConfiguration();
                Thread.Sleep(500);
                Power.RebootDevice();
                Thread.Sleep(Timeout.Infinite);
            }

            ConfigStore.Delete(ConfigStore.ApTries);

            DhcpServer dhcp = new DhcpServer();
            dhcp.CaptivePortalUrl = "http://" + ApIp;
            dhcp.Start(IPAddress.Parse(ApIp), new IPAddress(new byte[] { 255, 255, 255, 0 }), 1200);

            AdminServer.Start(config, "setup", ssid, ApIp);

            int seconds = 0;
            while (true)
            {
                Thread.Sleep(1000);
                seconds++;
                if (dueToWifi && seconds >= WifiRetrySeconds)
                {
                    // Сбой роутера мог быть временным: снимаем признак и пробуем рабочий режим.
                    ConfigStore.Delete(ConfigStore.ModeFlag);
                    Power.RebootDevice();
                }
            }
        }
    }
}
