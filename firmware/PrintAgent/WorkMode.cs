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
    // Рабочий режим: плата подключена к Wi-Fi роутера, забирает задания из бэкенда и печатает их.
    // Админ-панель доступна по адресу платы.
    public static class WorkMode
    {
        // Кнопка BOOT на плате: нажата — низкий уровень.
        private const int BootPin = 0;
        private const int WifiTimeoutMs = 60000;
        private const string JournalFile = "journal";

        private static AgentConfig config;

        public static void Run(AgentConfig agentConfig)
        {
            config = agentConfig;
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
            new Thread(PollLoop).Start();

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

        // Точка входа потока опроса бэкенда. Ни один путь не завершает цикл.
        private static void PollLoop()
        {
            BackendClient backend = new BackendClient(config.BackendUrl, config.DeviceId);
            PrinterClient printer = new PrinterClient(config.PrinterIp, config.PrinterPort);

            // Запись журнала: "started|ID" или "finished|ID|1 или 0|код ошибки".
            JournalEntry saved = null;
            try
            {
                string text = ConfigStore.ReadText(JournalFile);
                if (text != null)
                {
                    string[] parts = text.Split('|');
                    saved = new JournalEntry();
                    saved.State = parts[0];
                    saved.JobId = parts.Length > 1 ? parts[1] : null;
                    saved.Success = parts.Length > 2 && parts[2] == "1";
                    saved.Error = parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null;
                }
            }
            catch (Exception)
            {
                saved = null;
            }

            // Итог, который ещё не доставлен бэкенду. После включения это может быть прерванная печать.
            JournalEntry pending = JobJournal.OnBoot(saved);
            int rejected = 0;

            while (true)
            {
                try
                {
                    if (pending != null)
                    {
                        int status = backend.PostResult(pending.JobId, pending.Success, pending.Error);
                        if (status == 400)
                        {
                            rejected++;
                        }

                        // Ответ 400 три раза подряд: бэкенд не примет этот итог никогда, повтор прекращается.
                        if (JobJournal.IsDelivered(status) || rejected >= 3)
                        {
                            ConfigStore.Delete(JournalFile);
                            AdminServer.Backend = "ok";
                            pending = null;
                            rejected = 0;
                            continue;
                        }

                        AdminServer.Backend = status == 0 ? "unreachable" : "ok";
                    }
                    else
                    {
                        string jobId;
                        ReceiptLine[] lines;
                        int status = backend.TryGetNext(out jobId, out lines);
                        AdminServer.Backend = status == 0 ? "unreachable" : (status == 404 ? "bad-address" : "ok");

                        if (status == 200)
                        {
                            // Отметка «начато» до печати: после обрыва питания чек заново не печатается.
                            ConfigStore.WriteText(JournalFile, JobJournal.Started + "|" + jobId);

                            string error;
                            if (lines == null)
                            {
                                error = "BAD_JOB";
                            }
                            else
                            {
                                try
                                {
                                    error = printer.Print(EscPosBuilder.Build(lines));
                                }
                                catch (Exception)
                                {
                                    error = "BAD_JOB";
                                }
                            }

                            pending = new JournalEntry();
                            pending.JobId = jobId;
                            pending.State = JobJournal.Finished;
                            pending.Success = error == null;
                            pending.Error = error;
                            ConfigStore.WriteText(JournalFile, JobJournal.Finished + "|" + jobId + "|" + (error == null ? "1" : "0") + "|" + error);
                            AdminServer.LastJob = (error == null ? "printed " : error + " ") + jobId.Substring(0, 8);
                            continue;
                        }
                    }
                }
                catch (Exception)
                {
                    AdminServer.Backend = "unreachable";
                }

                Thread.Sleep(config.PollSeconds * 1000);
            }
        }
    }
}
