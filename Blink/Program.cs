using System;
using System.Device.Gpio;
using System.Diagnostics;
using System.Threading;
using Iot.Device.Ws28xx.Esp32;

namespace Blink
{
    public class Program
    {
        // Встроенный RGB-светодиод (WS2812). Подключён к GPIO48 через перемычку «RGB» на плате:
        // пока перемычка не запаяна, он не светится.
        private const int RgbLedPin = 48;

        // Маленький зелёный светодиод «TX» висит на линии UART0 TX (GPIO43) и работает без перемычек.
        // Пока программа занимает этот вывод, отладка через разъём UART (CH343) недоступна; прошивка работает.
        private const int TxLedPin = 43;

        // GPIO44 (светодиод RX) не занимать: на эту линию передаёт микросхема CH343.

        // Обычный светодиод, подключённый снаружи: GPIO2 → резистор 220–330 Ом → LED → GND.
        private const int PlainLedPin = 2;

        private const int PeriodMs = 500;

        // WS2812 на полной яркости слепит и потребляет до 60 мА, для теста хватает малой доли.
        private const byte Brightness = 24;

        public static void Main()
        {
            Debug.WriteLine("Blink: старт");

            GpioController gpio = new GpioController();
            GpioPin plainLed = gpio.OpenPin(PlainLedPin, PinMode.Output);
            GpioPin txLed = gpio.OpenPin(TxLedPin, PinMode.Output);

            // Ошибка в драйвере RGB не должна останавливать мигание обычных выводов.
            Ws2812c rgbLed = null;
            try
            {
                rgbLed = new Ws2812c(RgbLedPin, 1, 1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Blink: RGB недоступен: " + ex.Message);
            }

            bool red = true;
            while (true)
            {
                // RGB: красный и зелёный по очереди.
                if (rgbLed != null)
                {
                    rgbLed.Image.SetPixel(0, 0, red ? Brightness : (byte)0, red ? (byte)0 : Brightness, 0);
                    rgbLed.Update();
                }

                // Зелёный TX и внешний светодиод мигают в противофазе друг другу.
                txLed.Write(red ? PinValue.High : PinValue.Low);
                plainLed.Write(red ? PinValue.High : PinValue.Low);

                red = !red;
                Thread.Sleep(PeriodMs);
            }
        }
    }
}
