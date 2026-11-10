using System;
using System.Device.Gpio;
using System.Device.Wifi;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Iot.Device.MulticastDns;
using Iot.Device.MulticastDns.Entities;
using Iot.Device.MulticastDns.Enum;
using Iot.Device.MulticastDns.EventArgs;
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
        // Отметка «начато» и итог лежат в разных файлах: запись итога не стирает отметку,
        // и обрыв питания посреди записи не оставляет плату без сведений о напечатанном чеке.
        private const string JournalFile = "journal";
        private const string DoneFile = "journal-done";

        private static AgentConfig config;
        private static string address = "";

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
                        int tries = ConfigStore.ReadInt(ConfigStore.ApTries);
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

            address = ip;
            AdminServer.Start(config, "work", config.WifiSsid, ip);

            // Сетевое имя: плата сама отвечает на запросы mDNS об имени MyPrintAgent.local.
            // Сбой этой службы не должен останавливать печать.
            try
            {
                MulticastDnsService mdns = new MulticastDnsService();
                mdns.MessageReceived += OnNameQuery;
                mdns.Start();
            }
            catch (Exception)
            {
            }

            new Thread(PollLoop).Start();
            ConfigStore.Delete(ConfigStore.Crashes);

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

        // Обработчик запросов mDNS: отвечает своим адресом на вопрос об имени платы.
        private static void OnNameQuery(object sender, MessageReceivedEventArgs e)
        {
            if (e.Message == null)
            {
                return;
            }

            string wanted = Defaults.HostName.ToLower() + ".local";
            foreach (Question question in e.Message.GetQuestions())
            {
                string domain = question.Domain == null ? "" : question.Domain.ToLower().TrimEnd('.');
                if (question.QueryType == DnsResourceType.A && domain == wanted)
                {
                    Response response = new Response();
                    response.AddAnswer(new ARecord(question.Domain, IPAddress.Parse(address), 120));
                    e.Response = response;
                }
            }
        }

        // Точка входа потока опроса бэкенда. Ни один путь не завершает цикл.
        private static void PollLoop()
        {
            BackendClient backend = new BackendClient(config.BackendUrl, config);
            PrinterClient printer = new PrinterClient(config.PrinterIp, config.PrinterPort);

            // Итог, который ещё не доставлен бэкенду. После включения это может быть прерванная печать.
            // Сначала целый файл итога; если его нет или он оборван — отметка «начато».
            JournalEntry pending = JobJournal.OnBoot(JobJournal.Parse(ConfigStore.ReadText(DoneFile)));
            if (pending == null)
            {
                pending = JobJournal.OnBoot(JobJournal.Parse(ConfigStore.ReadText(JournalFile)));
            }

            int rejected = 0;

            while (true)
            {
                try
                {
                    if (pending != null)
                    {
                        int status = backend.PostResult(pending.JobId, pending.Success, pending.Error);
                        rejected = status == 400 ? rejected + 1 : 0;

                        // Ответ 400 три раза подряд: бэкенд не примет этот итог никогда, повтор прекращается.
                        if (JobJournal.IsDelivered(status) || rejected >= 3)
                        {
                            ConfigStore.Delete(DoneFile);
                            ConfigStore.Delete(JournalFile);
                            AdminServer.Backend = "ok";
                            pending = null;
                            rejected = 0;
                            continue;
                        }

                        AdminServer.Backend = status == 0 ? "unreachable" : "error";
                    }
                    else
                    {
                        string jobId;
                        ReceiptLine[] lines;
                        int status = backend.TryGetNext(out jobId, out lines);
                        AdminServer.Backend = status == 0 ? "unreachable"
                            : (status == 404 ? "bad-address" : (status == 200 || status == 204 ? "ok" : "error"));

                        if (status == 200)
                        {
                            // Отметка «начато» до печати: после обрыва питания чек заново не печатается.
                            JournalEntry started = new JournalEntry();
                            started.JobId = jobId;
                            started.State = JobJournal.Started;
                            ConfigStore.WriteText(JournalFile, JobJournal.Format(started));

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
                            // Отметка «начато» удаляется только после того, как итог записан целиком.
                            ConfigStore.WriteText(DoneFile, JobJournal.Format(pending));
                            ConfigStore.Delete(JournalFile);
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
