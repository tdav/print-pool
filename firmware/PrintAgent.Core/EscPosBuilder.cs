using System;

namespace PrintAgent.Core
{
    // Сборка чека в байты ESC/POS. Только ASCII; перенос длинных text делает принтер
    public static class EscPosBuilder
    {
        // width — число знаков в строке принтера (32..64)
        public static byte[] Build(ReceiptLine[] lines, int width)
        {
            ByteBuffer buf = new ByteBuffer();
            buf.Append(new byte[] { 0x1B, 0x40 }); // сброс принтера

            for (int i = 0; i < lines.Length; i++)
            {
                ReceiptLine line = lines[i];
                switch (line.Type)
                {
                    case "text":
                    case "pair":
                        bool center = line.Align == "center";
                        bool right = line.Align == "right";
                        bool dbl = line.Size == 2;
                        if (center) { buf.Append(new byte[] { 0x1B, 0x61, 0x01 }); }
                        if (right) { buf.Append(new byte[] { 0x1B, 0x61, 0x02 }); }
                        if (line.Bold) { buf.Append(new byte[] { 0x1B, 0x45, 0x01 }); }
                        if (dbl) { buf.Append(new byte[] { 0x1D, 0x21, 0x11 }); }
                        if (line.Type == "text")
                        {
                            buf.Append(Sanitize(line.Text));
                        }
                        else
                        {
                            buf.Append(FormatPair(Sanitize(line.Left), Sanitize(line.Right), dbl ? width / 2 : width));
                        }
                        buf.Append(0x0A);
                        // возврат к обычному состоянию
                        if (dbl) { buf.Append(new byte[] { 0x1D, 0x21, 0x00 }); }
                        if (line.Bold) { buf.Append(new byte[] { 0x1B, 0x45, 0x00 }); }
                        if (center || right) { buf.Append(new byte[] { 0x1B, 0x61, 0x00 }); }
                        break;

                    case "divider":
                        buf.Append(new string('-', width));
                        buf.Append(0x0A);
                        break;

                    case "feed":
                        buf.Append(new byte[] { 0x1B, 0x64, (byte)(line.Lines < 1 ? 1 : line.Lines) });
                        break;

                    case "cut":
                        buf.Append(new byte[] { 0x1D, 0x56, 0x42, 0x00 });
                        break;

                    case "qr":
                        string value = Sanitize(line.Value);
                        int len = value.Length + 3;
                        buf.Append(new byte[] { 0x1B, 0x61, 0x01 }); // по центру
                        buf.Append(new byte[] { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 }); // модель
                        buf.Append(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x06 }); // размер точки
                        buf.Append(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31 }); // коррекция M
                        buf.Append(new byte[] { 0x1D, 0x28, 0x6B, (byte)(len & 0xFF), (byte)(len >> 8), 0x31, 0x50, 0x30 });
                        buf.Append(value);
                        buf.Append(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 }); // печать
                        buf.Append(new byte[] { 0x1B, 0x61, 0x00 });
                        break;

                    default:
                        throw new ArgumentException("Неизвестный тип строки чека: " + line.Type);
                }
            }

            return buf.ToArray();
        }

        // null → "", символы вне 0x20–0x7E → '?'
        public static string Sanitize(string text)
        {
            if (text == null)
            {
                return "";
            }
            char[] chars = new char[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                chars[i] = (text[i] >= ' ' && text[i] <= '~') ? text[i] : '?';
            }
            return new string(chars);
        }

        // Левая часть, пробелы, правая часть — ровно width символов
        public static string FormatPair(string left, string right, int width)
        {
            if (left == null) { left = ""; }
            if (right == null) { right = ""; }
            if (right.Length > width - 1)
            {
                return right.Substring(0, width);
            }
            if (left.Length + 1 + right.Length > width)
            {
                left = left.Substring(0, width - 1 - right.Length);
            }
            return left + new string(' ', width - left.Length - right.Length) + right;
        }
    }
}
