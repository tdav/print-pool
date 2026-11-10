using System;
using PrintAgent.Core;
using Xunit;

namespace PrintAgent.Core.Tests
{
    public class EscPosBuilderTests
    {
        private static ReceiptLine L(string type)
        {
            ReceiptLine l = new ReceiptLine();
            l.Type = type;
            return l;
        }

        // Индекс первого вхождения последовательности или -1
        private static int IndexOf(byte[] data, byte[] part)
        {
            for (int i = 0; i + part.Length <= data.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < part.Length; j++)
                {
                    if (data[i + j] != part[j]) { ok = false; break; }
                }
                if (ok) { return i; }
            }
            return -1;
        }

        [Fact]
        public void Build_StartsWithReset()
        {
            byte[] r = EscPosBuilder.Build(new ReceiptLine[0]);
            Assert.Equal(new byte[] { 0x1B, 0x40 }, r);
        }

        [Fact]
        public void Text_CenterBoldDouble_EmitsCommandsThenResets()
        {
            ReceiptLine l = L("text");
            l.Text = "AB";
            l.Align = "center";
            l.Bold = true;
            l.Size = 2;
            byte[] r = EscPosBuilder.Build(new ReceiptLine[] { l });
            byte[] expected = new byte[]
            {
                0x1B, 0x40,
                0x1B, 0x61, 0x01, 0x1B, 0x45, 0x01, 0x1D, 0x21, 0x11,
                0x41, 0x42, 0x0A,
                0x1D, 0x21, 0x00, 0x1B, 0x45, 0x00, 0x1B, 0x61, 0x00
            };
            Assert.Equal(expected, r);
        }

        [Fact]
        public void Text_RightAlign_ResetsAlignment()
        {
            ReceiptLine l = L("text");
            l.Text = "A";
            l.Align = "right";
            byte[] r = EscPosBuilder.Build(new ReceiptLine[] { l });
            Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x61, 0x02, 0x41, 0x0A, 0x1B, 0x61, 0x00 }, r);
        }

        [Fact]
        public void Divider_Is48Dashes()
        {
            byte[] r = EscPosBuilder.Build(new ReceiptLine[] { L("divider") });
            Assert.Equal(2 + 48 + 1, r.Length);
            for (int i = 2; i < 50; i++) { Assert.Equal(0x2D, r[i]); }
            Assert.Equal(0x0A, r[50]);
        }

        [Fact]
        public void Pair_PadsToWidth()
        {
            string s = EscPosBuilder.FormatPair("TOTAL", "45 000", 48);
            Assert.Equal(48, s.Length);
            Assert.StartsWith("TOTAL", s);
            Assert.EndsWith("45 000", s);
        }

        [Fact]
        public void Pair_LongLeft_TruncatedKeepingOneSpace()
        {
            string s = EscPosBuilder.FormatPair(new string('x', 60), "9.99", 48);
            Assert.Equal(48, s.Length);
            Assert.EndsWith("x 9.99", s);
        }

        [Fact]
        public void Pair_LongRight_LeftDisappears()
        {
            string s = EscPosBuilder.FormatPair("L", new string('r', 60), 48);
            Assert.Equal(new string('r', 48), s);
        }

        [Fact]
        public void Pair_Size2_UsesWidth24()
        {
            ReceiptLine l = L("pair");
            l.Left = "A";
            l.Right = "1";
            l.Size = 2;
            byte[] r = EscPosBuilder.Build(new ReceiptLine[] { l });
            // сброс(2) + размер(3) + 24 символа + LF + возврат размера(3)
            Assert.Equal(2 + 3 + 24 + 1 + 3, r.Length);
            Assert.Equal(0x0A, r[2 + 3 + 24]);
        }

        [Fact]
        public void Sanitize_NonAscii_ReplacedWithQuestionMark()
        {
            Assert.Equal("???", EscPosBuilder.Sanitize("Чай"));
            Assert.Equal("", EscPosBuilder.Sanitize(null));
            Assert.Equal("a b~", EscPosBuilder.Sanitize("a b~"));
        }

        [Fact]
        public void Text_NonAscii_IsSanitizedInOutput()
        {
            ReceiptLine l = L("text");
            l.Text = "Чай";
            byte[] r = EscPosBuilder.Build(new ReceiptLine[] { l });
            Assert.Equal(new byte[] { 0x1B, 0x40, 0x3F, 0x3F, 0x3F, 0x0A }, r);
        }

        [Fact]
        public void Qr_StoresDataWithCorrectLength()
        {
            ReceiptLine l = L("qr");
            l.Value = "https://example.com";
            Assert.Equal(19, l.Value.Length);
            byte[] r = EscPosBuilder.Build(new ReceiptLine[] { l });
            int i = IndexOf(r, new byte[] { 0x1D, 0x28, 0x6B, 22, 0, 0x31, 0x50, 0x30 });
            Assert.True(i >= 0);
            Assert.Equal((byte)'h', r[i + 8]);
            // модель, размер, коррекция, печать присутствуют, по центру и возврат влево
            Assert.True(IndexOf(r, new byte[] { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 }) >= 0);
            Assert.True(IndexOf(r, new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x06 }) >= 0);
            Assert.True(IndexOf(r, new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31 }) >= 0);
            Assert.True(IndexOf(r, new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 }) >= 0);
            Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x61, 0x01 }, new byte[] { r[0], r[1], r[2], r[3], r[4] });
            Assert.Equal(new byte[] { 0x1B, 0x61, 0x00 }, new byte[] { r[r.Length - 3], r[r.Length - 2], r[r.Length - 1] });
        }

        [Fact]
        public void Feed_EmitsEscD()
        {
            ReceiptLine l = L("feed");
            l.Lines = 3;
            byte[] r = EscPosBuilder.Build(new ReceiptLine[] { l });
            Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x64, 0x03 }, r);
        }

        [Fact]
        public void Feed_ZeroLines_TreatedAsOne()
        {
            byte[] r = EscPosBuilder.Build(new ReceiptLine[] { L("feed") });
            Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x64, 0x01 }, r);
        }

        [Fact]
        public void Cut_EmitsPartialCut()
        {
            byte[] r = EscPosBuilder.Build(new ReceiptLine[] { L("cut") });
            Assert.Equal(new byte[] { 0x1B, 0x40, 0x1D, 0x56, 0x42, 0x00 }, r);
        }

        [Fact]
        public void Build_UnknownType_Throws()
        {
            Assert.Throws<ArgumentException>(() => EscPosBuilder.Build(new ReceiptLine[] { L("bogus") }));
        }

        [Fact]
        public void Build_SpecExample_EndsWithCut()
        {
            ReceiptLine head = L("text"); head.Text = "SHOP"; head.Align = "center"; head.Bold = true; head.Size = 2;
            ReceiptLine pair = L("pair"); pair.Left = "TOTAL"; pair.Right = "45 000";
            ReceiptLine qr = L("qr"); qr.Value = "order-123";
            ReceiptLine feed = L("feed"); feed.Lines = 3;
            byte[] r = EscPosBuilder.Build(new ReceiptLine[] { head, L("divider"), pair, qr, feed, L("cut") });
            int n = r.Length;
            Assert.Equal(new byte[] { 0x1D, 0x56, 0x42, 0x00 }, new byte[] { r[n - 4], r[n - 3], r[n - 2], r[n - 1] });
        }
    }
}
