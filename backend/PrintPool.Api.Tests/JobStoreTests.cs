using Microsoft.Extensions.Time.Testing;

namespace PrintPool.Api.Tests;

public class JobStoreTests
{
    private static readonly Guid D1 = Guid.NewGuid();
    private static readonly Guid D2 = Guid.NewGuid();
    private static readonly ReceiptLineDto[] Lines = [new("text", "hi", null, null, null, null, null, null, null)];

    [Fact]
    public void Add_ReturnsQueuedJobWithNewId()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var job = store.Add(D1, Lines);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.NotEqual(Guid.Empty, job.JobId);
    }

    [Fact]
    public void TakeNext_ReturnsJobsInAddOrder()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        var b = store.Add(D1, Lines);
        Assert.Equal(a.JobId, store.TakeNext(D1)!.JobId);
        store.Complete(a.JobId, D1, true, null);
        Assert.Equal(b.JobId, store.TakeNext(D1)!.JobId);
    }

    [Fact]
    public void TakeNext_RepeatsDispatchedJobUntilCompleted()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        var b = store.Add(D1, Lines);
        Assert.Equal(a.JobId, store.TakeNext(D1)!.JobId);
        Assert.Equal(a.JobId, store.TakeNext(D1)!.JobId);
        Assert.Equal(JobStatus.Queued, store.Find(b.JobId)!.Status);
    }

    [Fact]
    public void TakeNext_OtherDevice_ReturnsNull()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        store.Add(D1, Lines);
        Assert.Null(store.TakeNext(D2));
    }

    [Fact]
    public void Complete_Success_SetsPrinted()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        store.TakeNext(D1);
        Assert.Equal(ResultOutcome.Accepted, store.Complete(a.JobId, D1, true, null));
        Assert.Equal(JobStatus.Printed, store.Find(a.JobId)!.Status);
    }

    [Fact]
    public void Complete_Failure_KeepsErrorAndNextJobStillServed()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        var b = store.Add(D1, Lines);
        store.TakeNext(D1);
        Assert.Equal(ResultOutcome.Accepted, store.Complete(a.JobId, D1, false, "NO_PAPER"));
        var found = store.Find(a.JobId)!;
        Assert.Equal(JobStatus.Failed, found.Status);
        Assert.Equal("NO_PAPER", found.Error);
        Assert.Equal(b.JobId, store.TakeNext(D1)!.JobId);
    }

    [Fact]
    public void Complete_WrongDevice_Rejected()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        store.TakeNext(D1);
        Assert.Equal(ResultOutcome.WrongDevice, store.Complete(a.JobId, D2, true, null));
        Assert.Equal(JobStatus.Dispatched, store.Find(a.JobId)!.Status);
    }

    [Fact]
    public void Complete_QueuedJob_Rejected()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        Assert.Equal(ResultOutcome.WrongState, store.Complete(a.JobId, D1, true, null));
        Assert.Equal(JobStatus.Queued, store.Find(a.JobId)!.Status);
    }

    [Fact]
    public void Complete_UnknownJob_NotFound()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        Assert.Equal(ResultOutcome.JobNotFound, store.Complete(Guid.NewGuid(), D1, true, null));
    }

    [Fact]
    public void Dispatched_After60s_FailsWithDeviceTimeout()
    {
        var time = new FakeTimeProvider();
        var store = new JobStore(time, TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        store.TakeNext(D1);
        time.Advance(TimeSpan.FromSeconds(61));
        var found = store.Find(a.JobId)!;
        Assert.Equal(JobStatus.Failed, found.Status);
        Assert.Equal("DEVICE_TIMEOUT", found.Error);
    }

    [Fact]
    public void Complete_AfterTimeout_SameDevice_Overrides()
    {
        var time = new FakeTimeProvider();
        var store = new JobStore(time, TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        store.TakeNext(D1);
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(ResultOutcome.Accepted, store.Complete(a.JobId, D1, true, null));
        Assert.Equal(JobStatus.Printed, store.Find(a.JobId)!.Status);
    }

    [Fact]
    public void Complete_RepeatedSameResult_Accepted()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        store.TakeNext(D1);
        store.Complete(a.JobId, D1, true, null);
        var before = store.Find(a.JobId);
        Assert.Equal(ResultOutcome.Accepted, store.Complete(a.JobId, D1, true, null));
        Assert.Equal(before, store.Find(a.JobId));
    }

    [Fact]
    public void Complete_AfterPrinted_DifferentResult_Rejected()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        store.TakeNext(D1);
        store.Complete(a.JobId, D1, true, null);
        Assert.Equal(ResultOutcome.WrongState, store.Complete(a.JobId, D1, false, "NO_PAPER"));
        Assert.Equal(JobStatus.Printed, store.Find(a.JobId)!.Status);
    }

    [Fact]
    public void Complete_AfterExplicitFailure_DifferentResult_Rejected()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        store.TakeNext(D1);
        store.Complete(a.JobId, D1, false, "NO_PAPER");
        Assert.Equal(ResultOutcome.WrongState, store.Complete(a.JobId, D1, true, null));
        Assert.Equal(JobStatus.Failed, store.Find(a.JobId)!.Status);
    }

    [Fact]
    public void Complete_RepeatedFailure_AcceptedWithoutChange()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        store.TakeNext(D1);
        store.Complete(a.JobId, D1, false, "NO_PAPER");
        var before = store.Find(a.JobId);
        Assert.Equal(ResultOutcome.Accepted, store.Complete(a.JobId, D1, false, "NO_PAPER"));
        Assert.Equal(before, store.Find(a.JobId));
    }

    [Fact]
    public void TakeNext_AfterTimeoutOfA_ServesB()
    {
        var time = new FakeTimeProvider();
        var store = new JobStore(time, TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        var b = store.Add(D1, Lines);
        store.TakeNext(D1);
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(b.JobId, store.TakeNext(D1)!.JobId);
        Assert.Equal(JobStatus.Failed, store.Find(a.JobId)!.Status);
    }

    [Fact]
    public void Complete_AfterTimeout_OtherDevice_WrongDeviceThenRightDeviceAccepted()
    {
        var time = new FakeTimeProvider();
        var store = new JobStore(time, TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        store.TakeNext(D1);
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(ResultOutcome.WrongDevice, store.Complete(a.JobId, D2, true, null));
        Assert.Equal(ResultOutcome.Accepted, store.Complete(a.JobId, D1, true, null));
        Assert.Equal(JobStatus.Printed, store.Find(a.JobId)!.Status);
    }

    [Fact]
    public void Timeout_ExactlyAtLimit_StillDispatched_After61sFailed()
    {
        var time = new FakeTimeProvider();
        var store = new JobStore(time, TimeSpan.FromSeconds(60));
        var a = store.Add(D1, Lines);
        store.TakeNext(D1);
        time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(JobStatus.Dispatched, store.Find(a.JobId)!.Status);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(JobStatus.Failed, store.Find(a.JobId)!.Status);
    }

    [Fact]
    public void Add_StoresOwnCopyOfLines()
    {
        var store = new JobStore(new FakeTimeProvider(), TimeSpan.FromSeconds(60));
        var lines = new[] { new ReceiptLineDto("text", "hi", null, null, null, null, null, null, null) };
        var job = store.Add(D1, lines);
        lines[0] = new ReceiptLineDto("cut", null, null, null, null, null, null, null, null);
        Assert.Equal("text", store.Find(job.JobId)!.Lines[0].Type);
    }
}
