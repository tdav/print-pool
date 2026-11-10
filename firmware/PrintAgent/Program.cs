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

        public static void Main()
        {
            new Thread(Blink).Start();

            try
            {
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
                    WorkMode.Run(config);
                }
            }
            catch (Exception ex)
            {
                // Отладочного вывода нет: причина сохраняется в файл и показывается в админ-панели.
                try
                {
                    ConfigStore.WriteText(ConfigStore.LastError, ex.GetType().Name + ": " + ex.Message);
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
            }
        }
    }
}
