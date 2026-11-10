using System;
using System.Device.Gpio;
using System.Device.Wifi;
using System.Net.NetworkInformation;
using System.Threading;
using nanoFramework.Networking;
using nanoFramework.Runtime.Native;
using PrintAgent.Core;

namespace PrintAgent
{
    // Рабочий режим: плата подключена к Wi-Fi роутера, админ-панель доступна по адресу платы.
    public static class WorkMode
    {
        // Кнопка BOOT на плате: нажата — низкий уровень.
        private const int BootPin = 0;
        private const int WifiTimeoutMs = 60000;

        public static void Run(AgentConfig config)
        {
            Program.BlinkMs = 2000;

            // Точка доступа из режима настройки запускается сама при включении, пока её не выключить.
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.WirelessAP)
                {
                    WirelessAPConfiguration conf = WirelessAPConfiguration.GetAllWirelessAPConfigurations()[ni.SpecificConfigId];
                    if (conf.Options != WirelessAPConfiguration.ConfigurationOptions.None)
                    {
                        string saved = ConfigStore.ReadText(ConfigStore.ApTries);
                        int tries = saved == null ? 0 : int.Parse(saved);
                        if (tries < 2)
                        {
                            ConfigStore.WriteText(ConfigStore.ApTries, (tries + 1).ToString());
                            conf.Options = WirelessAPConfiguration.ConfigurationOptions.None;
                            conf.SaveConfiguration();
                            Thread.Sleep(500);
                            Power.RebootDevice();
                            Thread.Sleep(Timeout.Infinite);
                        }
                    }
                }
            }

            ConfigStore.Delete(ConfigStore.ApTries);

            bool connected = WifiNetworkHelper.ConnectDhcp(
                config.WifiSsid,
                config.WifiPassword,
                WifiReconnectionKind.Automatic,
                false,
                0,
                new CancellationTokenSource(WifiTimeoutMs).Token);

            if (!connected)
            {
                // Wi-Fi недоступен: после перезагрузки агент поднимет сеть настройки.
                ConfigStore.WriteText(ConfigStore.ModeFlag, "wifi");
                Power.RebootDevice();
                Thread.Sleep(Timeout.Infinite);
            }

            string ip = "";
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    ip = ni.IPv4Address;
                }
            }

            AdminServer.Start(config, "work", config.WifiSsid, ip);

            // Три секунды удержания BOOT — принудительный вход в режим настройки.
            GpioPin boot = Program.Gpio.OpenPin(BootPin, PinMode.InputPullUp);
            int held = 0;
            while (true)
            {
                Thread.Sleep(100);
                held = boot.Read() == PinValue.Low ? held + 1 : 0;
                if (held >= 30)
                {
                    ConfigStore.WriteText(ConfigStore.ModeFlag, "forced");
                    Power.RebootDevice();
                    Thread.Sleep(Timeout.Infinite);
                }
            }
        }
    }
}
