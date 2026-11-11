using System.Text;
using PrintAgent.Core;
using Xunit;

namespace PrintAgent.Core.Tests
{
    public class ReceiptFormatterTests
    {
        private static ReceiptItem Item(string name, string qty, int sum)
        {
            ReceiptItem i = new ReceiptItem();
            i.Name = name;
            i.Quantity = qty;
            i.Sum = sum;
            return i;
        }

        private static ReceiptData KitchenData()
        {
            ReceiptData d = new ReceiptData();
            d.BillNumber = "A20";
            d.Table = "12";
            d.Hall = "Siniy zal";
            d.Cashier = "Vlad";
            d.Items = new ReceiptItem[] { Item("SARBAST NEFILTR RAZLIV", "1", 0), Item("MNOGO MYASA 40SM", "2", 0) };
            return d;
        }

        private static ReceiptData BillData()
        {
            ReceiptData d = new ReceiptData();
            d.Title = "BEREZKA";
            d.BillNumber = "A20";
            d.Table = "12";
            d.Hall = "Siniy zal";
            d.Cashier = "Vlad";
            d.Items = new ReceiptItem[]
            {
                Item("MNOGO MYASA 40SM", "1", 80000),
                Item("SARBAST NEFILTR RAZLIV", "9", 126000),
                Item("KARTOFEL PO-DEREVENSKI S SOUSOM", "2", 30000)
            };
            d.Subtotal = 321000;
            d.ServicePercent = 10;
            d.ServiceSum = 32100;
            d.Total = 353100;
            d.Currency = "sum";
            return d;
        }

        private static string S(int n)
        {
            return new string(' ', n);
        }

        // Проверка всех полей строки text
        private static void AssertText(ReceiptLine l, string text, string align, int size, bool bold)
        {
            Assert.Equal("text", l.Type);
            Assert.Equal(text, l.Text);
            Assert.Equal(align, l.Align);
            Assert.Equal(size, l.Size);
            Assert.Equal(bold, l.Bold);
        }

        [Fact]
        public void Kitchen_Width48_MatchesLayout()
        {
            ReceiptLine[] r = ReceiptFormatter.Kitchen(KitchenData(), "11.11.2026 22:53", 48);

            Assert.Equal(10, r.Length);
            AssertText(r[0], "11.11.2026 22:53", "center", 0, false);
            AssertText(r[1], "Hisob No: A20", "center", 0, false);
            AssertText(r[2], "Stol No: 12, Siniy zal", "center", 0, false);
            AssertText(r[3], "Kassir: Vlad", "center", 0, false);
            AssertText(r[4], "", null, 0, false);
            AssertText(r[5], "SARBAST NEFILTR RAZL...1", null, 2, true);
            AssertText(r[6], "IV", null, 2, true);
            AssertText(r[7], "MNOGO MYASA 40SM.......2", null, 2, true);
            Assert.Equal("feed", r[8].Type);
            Assert.Equal(3, r[8].Lines);
            Assert.Equal("cut", r[9].Type);
        }

        [Fact]
        public void Kitchen_EmptyHall_NoComma()
        {
            ReceiptData d = KitchenData();
            d.Hall = "";
            ReceiptLine[] r = ReceiptFormatter.Kitchen(d, "11.11.2026 22:53", 48);
            Assert.Equal("Stol No: 12", r[2].Text);
        }

        [Fact]
        public void Kitchen_FractionalQuantity_WrapsNameTail()
        {
            ReceiptData d = KitchenData();
            d.Items = new ReceiptItem[] { Item("SHASHLIK MOL GOSHTI", "0,35", 0) };
            ReceiptLine[] r = ReceiptFormatter.Kitchen(d, "11.11.2026 22:53", 48);
            AssertText(r[5], "SHASHLIK MOL GOSH...0,35", null, 2, true);
            AssertText(r[6], "TI", null, 2, true);
        }

        [Fact]
        public void Kitchen_LongHallAtWidth32_WrapsHeaderCentered()
        {
            ReceiptData d = KitchenData();
            d.Hall = "ABCDEFGHIJ0123456789ABCDEFGHIJ0123456789";
            ReceiptLine[] r = ReceiptFormatter.Kitchen(d, "11.11.2026 22:53", 32);

            string full = "Stol No: 12, " + d.Hall;
            StringBuilder joined = new StringBuilder();
            int i = 2;
            while (joined.Length < full.Length)
            {
                Assert.True(r[i].Text.Length <= 32);
                Assert.Equal("center", r[i].Align);
                joined.Append(r[i].Text);
                i++;
            }
            Assert.Equal(full, joined.ToString());
            Assert.Equal("Kassir: Vlad", r[i].Text);
        }

        [Fact]
        public void Bill_Width48_MatchesLayout()
        {
            ReceiptLine[] r = ReceiptFormatter.Bill(BillData(), "11.11.2026 23:08", 48);

            int i = 0;
            AssertText(r[i++], "BEREZKA", "center", 2, true);
            AssertText(r[i++], "", null, 0, false);
            AssertText(r[i++], "Sana: 11.11.2026 23:08", "center", 0, false);
            AssertText(r[i++], "Kassir: Vlad", "center", 0, false);
            AssertText(r[i++], "Hisob No: A20", "center", 0, false);
            AssertText(r[i++], "Stol No: 12, Siniy zal", "center", 0, false);
            Assert.Equal("divider", r[i++].Type);
            AssertText(r[i++], "Nomi" + S(27) + "Soni" + S(8) + "Summa", null, 0, false);
            Assert.Equal("divider", r[i++].Type);
            AssertText(r[i++], "MNOGO MYASA 40SM" + S(18) + "1" + S(7) + "80 000", null, 0, false);
            AssertText(r[i++], "SARBAST NEFILTR RAZLIV" + S(12) + "9" + S(6) + "126 000", null, 0, false);
            AssertText(r[i++], "KARTOFEL PO-DEREVENSKI S SOUS" + S(5) + "2" + S(7) + "30 000", null, 0, false);
            AssertText(r[i++], "OM" + S(46), null, 0, false);
            Assert.Equal("divider", r[i++].Type);
            AssertText(r[i++], S(20) + "Summa:" + S(15) + "321 000", null, 0, false);
            AssertText(r[i++], S(20) + "Xizmat: 10%" + S(11) + "32 100", null, 0, false);
            Assert.Equal("divider", r[i++].Type);
            AssertText(r[i++], "Jami: 353 100 sum", "center", 2, true);
            Assert.Equal("divider", r[i++].Type);
            Assert.Equal("feed", r[i].Type);
            Assert.Equal(3, r[i++].Lines);
            Assert.Equal("cut", r[i++].Type);
            Assert.Equal(r.Length, i);
        }

        [Fact]
        public void Bill_TableRowsAreExactlyWidth()
        {
            ReceiptLine[] r = ReceiptFormatter.Bill(BillData(), "11.11.2026 23:08", 48);
            Assert.Equal(48, r[7].Text.Length);
            for (int i = 9; i <= 12; i++)
            {
                Assert.Equal(48, r[i].Text.Length);
            }
            Assert.Equal(48, r[14].Text.Length);
            Assert.Equal(48, r[15].Text.Length);
        }

        [Fact]
        public void Bill_ZeroServiceSum_NoServiceLine()
        {
            ReceiptData d = BillData();
            d.ServiceSum = 0;
            ReceiptLine[] r = ReceiptFormatter.Bill(d, "11.11.2026 23:08", 48);
            for (int i = 0; i < r.Length; i++)
            {
                Assert.DoesNotContain("Xizmat", r[i].Text ?? "");
            }
        }

        [Fact]
        public void Bill_ZeroPercent_ServiceWithoutPercent()
        {
            ReceiptData d = BillData();
            d.ServicePercent = 0;
            ReceiptLine[] r = ReceiptFormatter.Bill(d, "11.11.2026 23:08", 48);
            Assert.Equal(S(20) + "Xizmat:" + S(15) + "32 100", r[15].Text);
        }

        [Fact]
        public void Bill_EmptyTitle_StartsWithDate()
        {
            ReceiptData d = BillData();
            d.Title = "";
            ReceiptLine[] r = ReceiptFormatter.Bill(d, "11.11.2026 23:08", 48);
            AssertText(r[0], "Sana: 11.11.2026 23:08", "center", 0, false);
        }

        [Fact]
        public void Bill_LongTitle_NormalSizeBold()
        {
            ReceiptData d = BillData();
            d.Title = "ABCDEFGHIJKLMNOPQRSTUVWXY"; // 25 > 24, но title до 24 по контракту: берём 25 как границу
            ReceiptLine[] r = ReceiptFormatter.Bill(d, "11.11.2026 23:08", 48);
            AssertText(r[0], d.Title, "center", 1, true);

            d.Title = "ABCDEFGHIJKLMNOPQRSTUVWX"; // ровно 24 — крупно
            r = ReceiptFormatter.Bill(d, "11.11.2026 23:08", 48);
            AssertText(r[0], d.Title, "center", 2, true);
        }

        [Fact]
        public void Bill_LongTotal_NormalSizeBold()
        {
            ReceiptData d = BillData();
            d.Total = 999999999;
            d.Currency = "ABCDEFGHIJ"; // "Jami: 999 999 999 ABCDEFGHIJ" = 29 > 24
            ReceiptLine[] r = ReceiptFormatter.Bill(d, "11.11.2026 23:08", 48);
            AssertText(r[17], "Jami: 999 999 999 ABCDEFGHIJ", "center", 1, true);
        }

        [Fact]
        public void Bill_EmptyCurrency_NoTrailingSpace()
        {
            ReceiptData d = BillData();
            d.Currency = "";
            ReceiptLine[] r = ReceiptFormatter.Bill(d, "11.11.2026 23:08", 48);
            AssertText(r[17], "Jami: 353 100", "center", 2, true);
        }

        [Fact]
        public void Bill_Width32_ColumnsAndShiftedLabel()
        {
            ReceiptData d = BillData();
            d.ServicePercent = 100;
            d.ServiceSum = 999999999;
            ReceiptLine[] r = ReceiptFormatter.Bill(d, "11.11.2026 23:08", 32);

            // название 13 знаков: 32 - 19
            Assert.Equal("Nomi" + S(11) + "Soni" + S(8) + "Summa", r[7].Text);
            Assert.Equal("MNOGO MYASA 4" + S(5) + "1" + S(7) + "80 000", r[9].Text);
            Assert.Equal("0SM" + S(29), r[10].Text);
            for (int i = 0; i < r.Length; i++)
            {
                if (r[i].Type == "text" && r[i].Align == null && r[i].Text.Length > 0)
                {
                    Assert.Equal(32, r[i].Text.Length);
                }
            }
            // Summa: позиция 12; Xizmat: 100% (12 знаков) + сумма 11 знаков — сдвиг до позиции 8
            Assert.Equal(S(12) + "Summa:" + S(7) + "321 000", r[17].Text);
            Assert.Equal(S(8) + "Xizmat: 100%" + " " + "999 999 999", r[18].Text);
        }

        [Fact]
        public void Bill_OddWidth33_NothingOverflows()
        {
            ReceiptLine[] r = ReceiptFormatter.Bill(BillData(), "11.11.2026 23:08", 33);
            for (int i = 0; i < r.Length; i++)
            {
                if (r[i].Type != "text") { continue; }
                if (r[i].Size == 2)
                {
                    Assert.True(r[i].Text.Length <= 16);
                }
                else if (r[i].Align == null && r[i].Text.Length > 0)
                {
                    Assert.Equal(33, r[i].Text.Length);
                }
                else
                {
                    Assert.True(r[i].Text.Length <= 33);
                }
            }
        }

        [Fact]
        public void Kitchen_OddWidth33_BigLinesAre16()
        {
            ReceiptData d = KitchenData();
            ReceiptLine[] r = ReceiptFormatter.Kitchen(d, "11.11.2026 22:53", 33);
            int big = 0;
            for (int i = 0; i < r.Length; i++)
            {
                if (r[i].Size == 2)
                {
                    big++;
                    Assert.True(r[i].Text.Length <= 16);
                }
            }
            Assert.True(big >= 2);
            Assert.Equal(16, r[5].Text.Length);
        }

        [Theory]
        [InlineData(0, "0")]
        [InlineData(999, "999")]
        [InlineData(1000, "1 000")]
        [InlineData(126000, "126 000")]
        [InlineData(999999999, "999 999 999")]
        public void FormatMoney_GroupsByThree(int value, string expected)
        {
            Assert.Equal(expected, ReceiptFormatter.FormatMoney(value));
        }

        [Fact]
        public void FormatDateTime_PadsWithZeros()
        {
            Assert.Equal("05.01.2026 09:07", ReceiptFormatter.FormatDateTime(2026, 1, 5, 9, 7));
            Assert.Equal("11.11.2026 22:53", ReceiptFormatter.FormatDateTime(2026, 11, 11, 22, 53));
        }

        [Fact]
        public void Wrap_SplitsByCharacters()
        {
            Assert.Equal(new string[] { "ABC", "DEF", "G" }, ReceiptFormatter.Wrap("ABCDEFG", 3));
            Assert.Equal(new string[] { "ABC", "DEF" }, ReceiptFormatter.Wrap("ABCDEF", 3));
            Assert.Equal(new string[] { "" }, ReceiptFormatter.Wrap("", 3));
        }
    }
}
