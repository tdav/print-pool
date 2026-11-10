using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;

namespace PrintAgent
{
    // Поиск устройств с открытым портом (принтеров) в диапазоне адресов.
    // Работает в фоновом потоке; админ-панель опрашивает ход и результат.
    //
    // Скорость ограничена прошивкой: вызов подключения занимает около 1,25 секунды на адрес без устройства
    // и на это время останавливает остальные потоки платы (замерено: 6 потоков не быстрее одного).
    // Поэтому адреса проверяются по одному, с паузой между ними для печати и панели: 1,5 секунды на адрес.
    public static class PortScanner
    {
        // Диапазон шире не принимается: на 1024 адреса уходит около 26 минут.
        public const int MaxAddresses = 1024;

        // Ожидание ответа после вызова подключения: сам вызов уже занял около секунды.
        private const int WaitMs = 100;

        // Пауза между адресами: в неё плата печатает чеки и отвечает панели.
        private const int PauseMs = 150;

        public static bool Running;
        public static bool StopRequested;
        public static int Done;
        public static int Total;
        public static string Found = "";   // адреса через запятую
        public static string Error = "";

        private static uint first;
        private static uint last;
        private static int port;

        // null — сканирование запущено; иначе текст ошибки для панели.
        public static string Start(string fromIp, string toIp, int scanPort)
        {
            if (Running)
            {
                return "Поиск уже идёт.";
            }

            uint from;
            uint to;
            try
            {
                byte[] a = IPAddress.Parse(fromIp).GetAddressBytes();
                byte[] b = IPAddress.Parse(toIp).GetAddressBytes();
                from = ((uint)a[0] << 24) | ((uint)a[1] << 16) | ((uint)a[2] << 8) | a[3];
                to = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
            }
            catch (Exception)
            {
                return "Адреса диапазона: четыре числа от 0 до 255 через точку.";
            }

            if (to < from)
            {
                return "Конец диапазона меньше начала.";
            }

            if (to - from >= MaxAddresses)
            {
                return "Диапазон слишком большой: не больше " + MaxAddresses + " адресов.";
            }

            if (scanPort < 1 || scanPort > 65535)
            {
                return "Порт: число от 1 до 65535.";
            }

            first = from;
            last = to;
            port = scanPort;
            Done = 0;
            Total = (int)(to - from) + 1;
            Found = "";
            Error = "";
            StopRequested = false;
            Running = true;
            new Thread(Run).Start();
            return null;
        }

        // Точка входа потока сканирования.
        private static void Run()
        {
            try
            {
                // Обычное подключение ждёт ответа без ограничения по времени: 22 секунды на адрес без устройства.
                // Признак «не ждать» у сокета есть, но закрыт; без него диапазон сканировался бы часами.
                FieldInfo blocking = typeof(Socket).GetField("m_fBlocking", BindingFlags.Instance | BindingFlags.NonPublic);
                if (blocking == null)
                {
                    Error = "Прошивка платы не поддерживает быстрый поиск.";
                    return;
                }

                // Вторая проверка ловит переполнение на адресе 255.255.255.255.
                for (uint ip = first; ip <= last && ip >= first && !StopRequested; ip++)
                {
                    // Адреса сети и широковещательные принтерами не бывают.
                    uint tail = ip & 0xFF;
                    if (tail != 0 && tail != 255)
                    {
                        string text = ((ip >> 24) & 0xFF) + "." + ((ip >> 16) & 0xFF) + "." + ((ip >> 8) & 0xFF) + "." + tail;

                        // Проба идёт под той же блокировкой, что и печать: чек не должен столкнуться с ней на принтере.
                        lock (PrinterClient.Gate)
                        {
                            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                            try
                            {
                                blocking.SetValue(socket, false);
                                try
                                {
                                    socket.Connect(new IPEndPoint(IPAddress.Parse(text), port));
                                }
                                catch (Exception)
                                {
                                    // «Подключение ещё идёт» приходит исключением; итог покажет опрос ниже.
                                }

                                if (socket.Poll(WaitMs * 1000, SelectMode.SelectWrite)
                                    && (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error) == 0)
                                {
                                    Found += (Found.Length == 0 ? "" : ",") + text;
                                }
                            }
                            catch (Exception)
                            {
                                // Адрес без ответа — не ошибка поиска.
                            }
                            finally
                            {
                                socket.Close();
                            }
                        }

                        Thread.Sleep(PauseMs);
                    }

                    Done++;
                }
            }
            catch (Exception ex)
            {
                Error = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                Running = false;
            }
        }
    }
}
