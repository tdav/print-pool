using System;
using System.Net;
using System.Net.Sockets;
using PrintAgent.Core;

namespace PrintAgent
{
    // Связь с принтером по TCP: состояние и печать готовых байтов ESC/POS.
    public class PrinterClient
    {
        public const string Unreachable = "PRINTER_UNREACHABLE";
        public const string SendFailed = "SEND_FAILED";

        private const int TimeoutMs = 5000;
        private const int ChunkSize = 512;

        // Принтер держит одно соединение: панель и цикл опроса не должны обращаться к нему одновременно,
        // иначе настоящий чек получает ошибку из-за того, что кто-то открыл панель.
        private static readonly object Gate = new object();

        private readonly string ip;
        private readonly int port;

        public PrinterClient(string ip, int port)
        {
            this.ip = ip;
            this.port = port;
        }

        // null — принтер готов; иначе код причины.
        public string CheckStatus()
        {
            lock (Gate)
            {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.SendTimeout = TimeoutMs;
                socket.ReceiveTimeout = TimeoutMs;
                socket.Connect(new IPEndPoint(IPAddress.Parse(this.ip), this.port));

                byte[] answer = new byte[1];
                socket.Send(PrinterStatus.QueryOffline);
                int offline = socket.Receive(answer) == 1 ? answer[0] : -1;
                socket.Send(PrinterStatus.QueryPaper);
                int paper = socket.Receive(answer) == 1 ? answer[0] : -1;
                return PrinterStatus.Decode(offline, paper);
            }
            catch (Exception)
            {
                return Unreachable;
            }
            finally
            {
                socket.Close();
            }
            }
        }

        // null — чек отправлен; иначе код причины. Перед отправкой спрашивает состояние принтера.
        public string Print(byte[] data)
        {
            lock (Gate)
            {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.SendTimeout = TimeoutMs;
                socket.ReceiveTimeout = TimeoutMs;
                try
                {
                    socket.Connect(new IPEndPoint(IPAddress.Parse(this.ip), this.port));
                }
                catch (Exception)
                {
                    return Unreachable;
                }

                try
                {
                    byte[] answer = new byte[1];
                    socket.Send(PrinterStatus.QueryOffline);
                    int offline = socket.Receive(answer) == 1 ? answer[0] : -1;
                    socket.Send(PrinterStatus.QueryPaper);
                    int paper = socket.Receive(answer) == 1 ? answer[0] : -1;
                    string status = PrinterStatus.Decode(offline, paper);
                    if (status != null)
                    {
                        return status;
                    }

                    int sent = 0;
                    while (sent < data.Length)
                    {
                        int size = data.Length - sent > ChunkSize ? ChunkSize : data.Length - sent;
                        int n = socket.Send(data, sent, size, SocketFlags.None);
                        if (n <= 0)
                        {
                            return SendFailed;
                        }

                        sent += n;
                    }

                    return null;
                }
                catch (Exception)
                {
                    return SendFailed;
                }
            }
            finally
            {
                socket.Close();
            }
            }
        }
    }
}
