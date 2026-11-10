using PrintAgent.Core;
using Xunit;

namespace PrintAgent.Core.Tests
{
    public class JobJournalTests
    {
        [Fact]
        public void OnBoot_Null_ReturnsNull()
        {
            Assert.Null(JobJournal.OnBoot(null));
        }

        [Fact]
        public void OnBoot_Finished_ReturnsSameResult()
        {
            JournalEntry saved = new JournalEntry();
            saved.JobId = "job-1";
            saved.State = JobJournal.Finished;
            saved.Success = true;
            saved.Error = null;

            JournalEntry result = JobJournal.OnBoot(saved);

            Assert.Same(saved, result);
        }

        [Fact]
        public void OnBoot_Started_ReturnsRebootFailure()
        {
            JournalEntry saved = new JournalEntry();
            saved.JobId = "job-2";
            saved.State = JobJournal.Started;
            saved.Success = true;
            saved.Error = null;

            JournalEntry result = JobJournal.OnBoot(saved);

            Assert.NotNull(result);
            Assert.NotSame(saved, result);
            Assert.Equal("job-2", result.JobId);
            Assert.Equal(JobJournal.Finished, result.State);
            Assert.False(result.Success);
            Assert.Equal("REBOOT_DURING_PRINT", result.Error);
            // Исходная запись не изменяется
            Assert.Equal(JobJournal.Started, saved.State);
            Assert.True(saved.Success);
            Assert.Null(saved.Error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void OnBoot_EmptyJobId_ReturnsNull(string jobId)
        {
            JournalEntry saved = new JournalEntry();
            saved.JobId = jobId;
            saved.State = JobJournal.Started;

            Assert.Null(JobJournal.OnBoot(saved));
        }

        [Theory]
        [InlineData("unknown")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("FINISHED")]
        [InlineData("Started ")]
        public void OnBoot_UnknownState_ReturnsRebootFailure(string state)
        {
            JournalEntry saved = new JournalEntry();
            saved.JobId = "job-3";
            saved.State = state;
            saved.Success = true;
            saved.Error = "X";

            JournalEntry result = JobJournal.OnBoot(saved);

            Assert.NotNull(result);
            Assert.NotSame(saved, result);
            Assert.Equal("job-3", result.JobId);
            Assert.Equal(JobJournal.Finished, result.State);
            Assert.False(result.Success);
            Assert.Equal("REBOOT_DURING_PRINT", result.Error);
            Assert.Equal(state, saved.State);
            Assert.True(saved.Success);
            Assert.Equal("X", saved.Error);
        }

        [Fact]
        public void OnBoot_FinishedWithError_ReturnsUnchanged()
        {
            JournalEntry saved = new JournalEntry();
            saved.JobId = "job-4";
            saved.State = JobJournal.Finished;
            saved.Success = false;
            saved.Error = "PRINTER_OFFLINE";

            JournalEntry result = JobJournal.OnBoot(saved);

            Assert.Same(saved, result);
            Assert.Equal("job-4", result.JobId);
            Assert.False(result.Success);
            Assert.Equal("PRINTER_OFFLINE", result.Error);
        }

        [Theory]
        [InlineData(204)]
        [InlineData(404)]
        [InlineData(409)]
        public void IsDelivered_204_404_409_True(int status)
        {
            Assert.True(JobJournal.IsDelivered(status));
        }

        [Theory]
        [InlineData(200)]
        [InlineData(400)]
        [InlineData(500)]
        [InlineData(0)]
        [InlineData(203)]
        [InlineData(205)]
        [InlineData(410)]
        public void IsDelivered_OtherStatuses_False(int status)
        {
            Assert.False(JobJournal.IsDelivered(status));
        }
    }
}
