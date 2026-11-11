using PrintAgent.Core;
using Xunit;

namespace PrintAgent.Core.Tests
{
    public class AgentConfigTests
    {
        // Корректная конфигурация, от которой отталкиваются тесты правил
        private static AgentConfig Valid()
        {
            AgentConfig c = AgentConfig.CreateDefault("11111111-1111-1111-1111-111111111111");
            c.WifiSsid = "Shop";
            c.WifiPassword = "password1";
            c.BackendUrl = "http://192.168.1.10:5080";
            c.PrinterIp = "192.168.1.50";
            return c;
        }

        [Fact]
        public void CreateDefault_SetsPort9100AndPoll5()
        {
            AgentConfig c = AgentConfig.CreateDefault("id-1");
            Assert.Equal("id-1", c.DeviceId);
            Assert.Equal(9100, c.PrinterPort);
            Assert.Equal(5, c.PollSeconds);
        }

        [Fact]
        public void CreateDefault_LineWidth48AndUtcOffset5()
        {
            AgentConfig c = AgentConfig.CreateDefault("id-1");
            Assert.Equal(48, c.LineWidth);
            Assert.Equal(5, c.UtcOffsetHours);
        }

        [Theory]
        [InlineData(31)]
        [InlineData(65)]
        public void Validate_BadLineWidth_ReturnsError(int width)
        {
            AgentConfig c = Valid();
            c.LineWidth = width;
            Assert.Contains("LineWidth", c.Validate());
        }

        [Theory]
        [InlineData(32)]
        [InlineData(64)]
        public void Validate_BoundaryLineWidth_Ok(int width)
        {
            AgentConfig c = Valid();
            c.LineWidth = width;
            Assert.Null(c.Validate());
        }

        [Theory]
        [InlineData(-13)]
        [InlineData(15)]
        public void Validate_BadUtcOffset_ReturnsError(int hours)
        {
            AgentConfig c = Valid();
            c.UtcOffsetHours = hours;
            Assert.Contains("UtcOffsetHours", c.Validate());
        }

        [Theory]
        [InlineData(-12)]
        [InlineData(14)]
        public void Validate_BoundaryUtcOffset_Ok(int hours)
        {
            AgentConfig c = Valid();
            c.UtcOffsetHours = hours;
            Assert.Null(c.Validate());
        }

        [Fact]
        public void Validate_Valid_ReturnsNull()
        {
            Assert.Null(Valid().Validate());
        }

        [Theory]
        [InlineData("256.1.1.1")]
        [InlineData("1.2.3")]
        [InlineData("1.2.3.4.5")]
        [InlineData("a.b.c.d")]
        [InlineData("1..2.3")]
        [InlineData("1.2.3.")]
        [InlineData("1.2.3.4.")]
        [InlineData(".1.2.3")]
        [InlineData("0001.1.1.1")]
        [InlineData("")]
        public void Validate_BadPrinterIp_ReturnsError(string ip)
        {
            AgentConfig c = Valid();
            c.PrinterIp = ip;
            Assert.NotNull(c.Validate());
        }

        [Theory]
        [InlineData("0.0.0.0")]
        [InlineData("255.255.255.255")]
        [InlineData("001.2.3.4")]
        public void Validate_BoundaryPrinterIp_Ok(string ip)
        {
            AgentConfig c = Valid();
            c.PrinterIp = ip;
            Assert.Null(c.Validate());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(65536)]
        public void Validate_BadPort_ReturnsError(int port)
        {
            AgentConfig c = Valid();
            c.PrinterPort = port;
            Assert.NotNull(c.Validate());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(65535)]
        public void Validate_BoundaryPort_Ok(int port)
        {
            AgentConfig c = Valid();
            c.PrinterPort = port;
            Assert.Null(c.Validate());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3601)]
        public void Validate_BadPoll_ReturnsError(int poll)
        {
            AgentConfig c = Valid();
            c.PollSeconds = poll;
            Assert.NotNull(c.Validate());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3600)]
        public void Validate_BoundaryPoll_Ok(int poll)
        {
            AgentConfig c = Valid();
            c.PollSeconds = poll;
            Assert.Null(c.Validate());
        }

        [Theory]
        [InlineData("https://host:5080")]
        [InlineData("http://host:5080/")]
        [InlineData("host:5080")]
        [InlineData("http://")]
        [InlineData("­http://host:5080")]
        [InlineData("")]
        public void Validate_BadBackendUrl_ReturnsError(string url)
        {
            AgentConfig c = Valid();
            c.BackendUrl = url;
            Assert.NotNull(c.Validate());
        }

        [Fact]
        public void Validate_EmptySsid_ReturnsError()
        {
            AgentConfig c = Valid();
            c.WifiSsid = "";
            Assert.NotNull(c.Validate());
        }

        [Fact]
        public void Validate_Ssid33Chars_ReturnsError()
        {
            AgentConfig c = Valid();
            c.WifiSsid = new string('a', 33);
            Assert.NotNull(c.Validate());
        }

        [Fact]
        public void Validate_Ssid32Chars_Ok()
        {
            AgentConfig c = Valid();
            c.WifiSsid = new string('a', 32);
            Assert.Null(c.Validate());
        }

        [Theory]
        [InlineData(7)]
        [InlineData(64)]
        public void Validate_BadPasswordLength_ReturnsError(int len)
        {
            AgentConfig c = Valid();
            c.WifiPassword = new string('p', len);
            Assert.NotNull(c.Validate());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(8)]
        [InlineData(63)]
        public void Validate_GoodPasswordLength_Ok(int len)
        {
            AgentConfig c = Valid();
            c.WifiPassword = new string('p', len);
            Assert.Null(c.Validate());
        }

        [Fact]
        public void Validate_ErrorNamesField()
        {
            AgentConfig c = Valid();
            c.PrinterIp = "256.1.1.1";
            Assert.Contains("PrinterIp", c.Validate());
        }

        [Fact]
        public void IsComplete_Full_True()
        {
            Assert.True(Valid().IsComplete());
        }

        [Fact]
        public void IsComplete_EmptySsid_False()
        {
            AgentConfig c = Valid();
            c.WifiSsid = "";
            Assert.False(c.IsComplete());
        }

        [Fact]
        public void IsComplete_EmptyBackendOrPrinter_False()
        {
            AgentConfig c = Valid();
            c.BackendUrl = "";
            Assert.False(c.IsComplete());
            c = Valid();
            c.PrinterIp = "";
            Assert.False(c.IsComplete());
        }

        [Fact]
        public void Validate_Ssid1Char_Ok()
        {
            AgentConfig c = Valid();
            c.WifiSsid = "a";
            Assert.Null(c.Validate());
        }

        [Fact]
        public void Validate_NullStringFields_ReturnError()
        {
            AgentConfig c = Valid();
            c.PrinterIp = null;
            Assert.NotNull(c.Validate());
            c = Valid();
            c.BackendUrl = null;
            Assert.NotNull(c.Validate());
            c = Valid();
            c.WifiSsid = null;
            Assert.NotNull(c.Validate());
            c = Valid();
            c.WifiPassword = null;
            Assert.NotNull(c.Validate());
        }

        [Fact]
        public void IsComplete_NullFields_False()
        {
            AgentConfig c = new AgentConfig();
            Assert.False(c.IsComplete());
        }
    }
}
