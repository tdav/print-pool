using System;
using System.Collections;
using System.Text;

namespace PrintAgent.Core
{
    // Вёрстка чеков кухни и счёта в массив ReceiptLine. Чистая логика, без обращения к железу
    public static class ReceiptFormatter
    {
        // Постоянные надписи чека — менять язык достаточно здесь
        public const string LabelDate = "Sana";
        public const string LabelCashier = "Kassir";
        public const string LabelBill = "Hisob No";
        public const string LabelTable = "Stol No";
        public const string LabelName = "Nomi";
        public const string LabelQuantity = "Soni";
        public const string LabelSum = "Summa";
        public const string LabelService = "Xizmat";
        public const string LabelTotal = "Jami";

        // Ширины колонок таблицы счёта: количество и сумма; название занимает остаток (width - 19)
        const int QuantityColumn = 5;
        const int SumColumn = 12;

        // Чек для кухни: шапка, позиции крупно, протяжка, отрезка
        public static ReceiptLine[] Kitchen(ReceiptData data, string dateTime, int width)
        {
            ArrayList list = new ArrayList();
            int half = width / 2;

            string tableLine = LabelTable + ": " + data.Table;
            if (data.Hall != null && data.Hall.Length > 0)
            {
                tableLine = tableLine + ", " + data.Hall;
            }
            string[] header = new string[]
            {
                dateTime,
                LabelBill + ": " + data.BillNumber,
                tableLine,
                LabelCashier + ": " + data.Cashier
            };
            for (int h = 0; h < header.Length; h++)
            {
                string[] parts = Wrap(header[h], width);
                for (int p = 0; p < parts.Length; p++)
                {
                    list.Add(new ReceiptLine { Type = "text", Text = parts[p], Align = "center" });
                }
            }
            list.Add(new ReceiptLine { Type = "text", Text = "" });

            for (int i = 0; i < data.Items.Length; i++)
            {
                string name = data.Items[i].Name == null ? "" : data.Items[i].Name;
                string qty = data.Items[i].Quantity == null ? "" : data.Items[i].Quantity;
                int budget = width - qty.Length - 3;
                string first = name;
                string rest = "";
                if (name.Length > budget)
                {
                    first = name.Substring(0, budget);
                    rest = name.Substring(budget);
                }
                string row = first + new string('.', width - first.Length - qty.Length) + qty;
                list.Add(new ReceiptLine { Type = "text", Text = row, Size = 3, Bold = true });
                if (rest.Length > 0)
                {
                    string[] tail = Wrap(rest, width);
                    for (int t = 0; t < tail.Length; t++)
                    {
                        list.Add(new ReceiptLine { Type = "text", Text = tail[t], Size = 3, Bold = true });
                    }
                }
            }

            list.Add(new ReceiptLine { Type = "feed", Lines = 3 });
            list.Add(new ReceiptLine { Type = "cut" });
            return (ReceiptLine[])list.ToArray(typeof(ReceiptLine));
        }

        // Счёт клиенту: название, шапка, таблица, итоги, протяжка, отрезка
        public static ReceiptLine[] Bill(ReceiptData data, string dateTime, int width)
        {
            ArrayList list = new ArrayList();
            int half = width / 2;
            int nameWidth = width - 1 - QuantityColumn - 1 - SumColumn;

            if (data.Title != null && data.Title.Length > 0)
            {
                bool big = data.Title.Length <= half;
                list.Add(new ReceiptLine { Type = "text", Text = data.Title, Align = "center", Size = big ? 2 : 1, Bold = true });
                list.Add(new ReceiptLine { Type = "text", Text = "" });
            }

            string tableLine = LabelTable + ": " + data.Table;
            if (data.Hall != null && data.Hall.Length > 0)
            {
                tableLine = tableLine + ", " + data.Hall;
            }
            // порядок шапки счёта отличается от кухонного: сначала кассир, потом номер счёта
            string[] header = new string[]
            {
                LabelDate + ": " + dateTime,
                LabelCashier + ": " + data.Cashier,
                LabelBill + ": " + data.BillNumber,
                tableLine
            };
            for (int h = 0; h < header.Length; h++)
            {
                string[] parts = Wrap(header[h], width);
                for (int p = 0; p < parts.Length; p++)
                {
                    list.Add(new ReceiptLine { Type = "text", Text = parts[p], Align = "center" });
                }
            }

            list.Add(new ReceiptLine { Type = "divider" });
            list.Add(new ReceiptLine
            {
                Type = "text",
                Text = LabelName.PadRight(nameWidth) + " " + LabelQuantity.PadLeft(QuantityColumn) + " " + LabelSum.PadLeft(SumColumn)
            });
            list.Add(new ReceiptLine { Type = "divider" });

            for (int i = 0; i < data.Items.Length; i++)
            {
                string[] parts = Wrap(data.Items[i].Name == null ? "" : data.Items[i].Name, nameWidth);
                string qty = data.Items[i].Quantity == null ? "" : data.Items[i].Quantity;
                list.Add(new ReceiptLine
                {
                    Type = "text",
                    Text = parts[0].PadRight(nameWidth) + " " + qty.PadLeft(QuantityColumn) + " " + FormatMoney(data.Items[i].Sum).PadLeft(SumColumn)
                });
                for (int p = 1; p < parts.Length; p++)
                {
                    list.Add(new ReceiptLine { Type = "text", Text = parts[p].PadRight(width) });
                }
            }

            list.Add(new ReceiptLine { Type = "divider" });

            // Надпись итога начинается с позиции width/2 - 4, при нехватке места сдвигается влево
            string subtotal = FormatMoney(data.Subtotal);
            int pos = half - 4;
            string label = LabelSum + ":";
            int maxPos = width - subtotal.Length - label.Length - 1;
            if (pos > maxPos) { pos = maxPos; }
            if (pos < 0) { pos = 0; }
            list.Add(new ReceiptLine
            {
                Type = "text",
                Text = new string(' ', pos) + label + new string(' ', width - pos - label.Length - subtotal.Length) + subtotal
            });

            if (data.ServiceSum > 0)
            {
                string service = FormatMoney(data.ServiceSum);
                label = LabelService + ":";
                if (data.ServicePercent > 0)
                {
                    label = label + " " + data.ServicePercent.ToString() + "%";
                }
                pos = half - 4;
                maxPos = width - service.Length - label.Length - 1;
                if (pos > maxPos) { pos = maxPos; }
                if (pos < 0) { pos = 0; }
                list.Add(new ReceiptLine
                {
                    Type = "text",
                    Text = new string(' ', pos) + label + new string(' ', width - pos - label.Length - service.Length) + service
                });
            }

            list.Add(new ReceiptLine { Type = "divider" });

            string total = LabelTotal + ": " + FormatMoney(data.Total);
            if (data.Currency != null && data.Currency.Length > 0)
            {
                total = total + " " + data.Currency;
            }
            list.Add(new ReceiptLine { Type = "text", Text = total, Align = "center", Size = total.Length <= half ? 2 : 1, Bold = true });
            list.Add(new ReceiptLine { Type = "divider" });
            list.Add(new ReceiptLine { Type = "feed", Lines = 3 });
            list.Add(new ReceiptLine { Type = "cut" });
            return (ReceiptLine[])list.ToArray(typeof(ReceiptLine));
        }

        // "dd.MM.yyyy HH:mm"
        public static string FormatDateTime(int year, int month, int day, int hour, int minute)
        {
            return day.ToString().PadLeft(2, '0') + "." + month.ToString().PadLeft(2, '0') + "." + year.ToString().PadLeft(4, '0')
                + " " + hour.ToString().PadLeft(2, '0') + ":" + minute.ToString().PadLeft(2, '0');
        }

        // Пробел между тройками разрядов: 126000 → "126 000"
        public static string FormatMoney(int value)
        {
            string digits = value.ToString();
            int start = digits.Length > 0 && digits[0] == '-' ? 1 : 0;
            StringBuilder sb = new StringBuilder();
            sb.Append(digits.Substring(0, start));
            for (int i = start; i < digits.Length; i++)
            {
                if (i > start && (digits.Length - i) % 3 == 0)
                {
                    sb.Append(' ');
                }
                sb.Append(digits[i]);
            }
            return sb.ToString();
        }

        // Перенос по знакам на строки не длиннее width; пустой текст — одна пустая строка
        public static string[] Wrap(string text, int width)
        {
            if (text == null || text.Length == 0)
            {
                return new string[] { "" };
            }
            int count = (text.Length + width - 1) / width;
            string[] result = new string[count];
            for (int i = 0; i < count; i++)
            {
                int from = i * width;
                int len = text.Length - from < width ? text.Length - from : width;
                result[i] = text.Substring(from, len);
            }
            return result;
        }
    }
}
