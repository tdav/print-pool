namespace PrintAgent.Core
{
    // Запросы состояния принтера (DLE EOT n) и разбор ответных байт
    public static class PrinterStatus
    {
        // Состояние устройства «не в сети»: бит 2 — открыта крышка
        public static readonly byte[] QueryOffline = new byte[] { 0x10, 0x04, 0x02 };

        // Состояние датчика бумаги: биты 5–6 — бумага закончилась
        public static readonly byte[] QueryPaper = new byte[] { 0x10, 0x04, 0x04 };

        // null — готов; отрицательное значение означает «принтер не ответил» и ничего не сообщает
        public static string Decode(int offlineByte, int paperByte)
        {
            if (offlineByte >= 0 && (offlineByte & 0x04) != 0)
            {
                return "COVER_OPEN";
            }
            if (paperByte >= 0 && (paperByte & 0x60) != 0)
            {
                return "NO_PAPER";
            }
            return null;
        }
    }
}
