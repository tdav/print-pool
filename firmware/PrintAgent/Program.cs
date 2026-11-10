using System;
using System.Device.Gpio;
using System.Threading;
using nanoFramework.Runtime.Native;
using PrintAgent.Core;

namespace PrintAgent
{
    public class Program
    {
        // Зелёный светодиод TX на плате. Частое мигание — режим настройки, редкое — рабочий.
        private const int LedPin = 43;

        public static GpioController Gpio = new GpioController();

        // Полупериод мигания светодиода, меняется режимами.
        public static int BlinkMs = 500;

        // Диагностика необъяснимых перезагрузок: сколько раз плата включалась и сколько работает с последнего включения.
        public static int Boots;
        public static long UptimeMs;

        public static void Main()
        {
            new Thread(Blink).Start();

            bool work = false;
            try
            {
                Boots = ConfigStore.ReadInt(ConfigStore.Boots) + 1;
                ConfigStore.WriteText(ConfigStore.Boots, Boots.ToString());

                AgentConfig config = ConfigStore.Load();
                if (config == null)
                {
                    // ID создаётся один раз и сразу сохраняется, чтобы не меняться между включениями.
                    config = AgentConfig.CreateDefault(Guid.NewGuid().ToString());
                    config.PrinterIp = Defaults.PrinterIp;
                    config.PrinterPort = Defaults.PrinterPort;
                    config.BackendUrl = Defaults.BackendUrl;
                    config.WifiSsid = Defaults.WifiSsid;
                    config.WifiPassword = Defaults.WifiPassword;
                    ConfigStore.Save(config);
                }

                string flag = ConfigStore.ReadText(ConfigStore.ModeFlag);
                if (!config.IsComplete() || flag != null)
                {
                    SetupMode.Run(config, flag == "wifi");
                }
                else
                {
                    work = true;
                    WorkMode.Run(config);
                }
            }
            catch (Exception ex)
            {
                // Отладочного вывода нет: причина сохраняется в файл и показывается в админ-панели.
                try
                {
                    ConfigStore.WriteText(ConfigStore.LastError, ex.GetType().Name + ": " + ex.Message);

                    // Рабочий режим упал три раза подряд: следующий запуск — в режим настройки,
                    // иначе плата перезагружалась бы без конца и оставалась недоступной для настройки.
                    int crashes = ConfigStore.ReadInt(ConfigStore.Crashes) + 1;
                    if (work && crashes >= 3)
                    {
                        ConfigStore.WriteText(ConfigStore.ModeFlag, "wifi");
                        ConfigStore.Delete(ConfigStore.Crashes);
                    }
                    else
                    {
                        ConfigStore.WriteText(ConfigStore.Crashes, crashes.ToString());
                    }
                }
                catch (Exception)
                {
                }

                Thread.Sleep(5000);
                Power.RebootDevice();
            }

            Thread.Sleep(Timeout.Infinite);
        }

        // Точка входа потока светодиода.
        private static void Blink()
        {
            GpioPin led = Gpio.OpenPin(LedPin, PinMode.Output);
            while (true)
            {
                led.Toggle();
                Thread.Sleep(BlinkMs);
                // ponytail: время работы считается по паузам мигания — системные часы скачут при синхронизации времени.
                UptimeMs += BlinkMs;
            }
        }
    }
}
