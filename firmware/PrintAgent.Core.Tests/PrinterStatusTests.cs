using PrintAgent.Core;
using Xunit;

namespace PrintAgent.Core.Tests
{
    public class PrinterStatusTests
    {
        [Fact]
        public void Queries_AreDleEot()
        {
            Assert.Equal(new byte[] { 0x10, 0x04, 0x02 }, PrinterStatus.QueryOffline);
            Assert.Equal(new byte[] { 0x10, 0x04, 0x04 }, PrinterStatus.QueryPaper);
        }

        [Fact]
        public void Decode_Ready_ReturnsNull()
        {
            Assert.Null(PrinterStatus.Decode(0x12, 0x12));
        }

        [Fact]
        public void Decode_CoverOpen()
        {
            Assert.Equal("COVER_OPEN", PrinterStatus.Decode(0x16, 0x12));
        }

        [Fact]
        public void Decode_NoPaper()
        {
            Assert.Equal("NO_PAPER", PrinterStatus.Decode(0x12, 0x72));
        }

        [Fact]
        public void Decode_CoverOpenWinsOverNoPaper()
        {
            Assert.Equal("COVER_OPEN", PrinterStatus.Decode(0x16, 0x72));
        }

        [Fact]
        public void Decode_NoAnswer_IsNoInformation()
        {
            Assert.Null(PrinterStatus.Decode(-1, -1));
            Assert.Equal("NO_PAPER", PrinterStatus.Decode(-1, 0x72));
            Assert.Equal("COVER_OPEN", PrinterStatus.Decode(0x16, -1));
        }
    }
}
