namespace PrintPool.Api;

/// <summary>
/// Хранилище заданий печати в памяти, очередь на каждое устройство.
/// Таймаут выданных заданий проверяется лениво в каждом публичном методе.
/// </summary>
public sealed class JobStore(TimeProvider timeProvider, TimeSpan dispatchTimeout)
{
    // Единая блокировка на все операции.
    private readonly object sync = new();

    // Задания в порядке добавления.
    private readonly List<PrintJob> jobs = [];

    // Задания, завершённые по DEVICE_TIMEOUT: поздний итог от устройства ещё можно принять.
    private readonly HashSet<Guid> timedOut = [];

    public PrintJob Add(Guid deviceId, ReceiptLineDto[] lines)
    {
        lock (this.sync)
        {
            var now = timeProvider.GetUtcNow();
            for (var i = 0; i < this.jobs.Count; i++)
            {
                var j = this.jobs[i];
                if (j.Status == JobStatus.Dispatched && now - j.DispatchedAt!.Value > dispatchTimeout)
                {
                    this.jobs[i] = j with { Status = JobStatus.Failed, Error = "DEVICE_TIMEOUT", FinishedAt = now };
                    this.timedOut.Add(j.JobId);
                }
            }

            var job = new PrintJob(Guid.NewGuid(), deviceId, lines, JobStatus.Queued, null, now, null, null);
            this.jobs.Add(job);
            return job;
        }
    }

    public PrintJob? TakeNext(Guid deviceId)
    {
        lock (this.sync)
        {
            var now = timeProvider.GetUtcNow();
            for (var i = 0; i < this.jobs.Count; i++)
            {
                var j = this.jobs[i];
                if (j.Status == JobStatus.Dispatched && now - j.DispatchedAt!.Value > dispatchTimeout)
                {
                    this.jobs[i] = j with { Status = JobStatus.Failed, Error = "DEVICE_TIMEOUT", FinishedAt = now };
                    this.timedOut.Add(j.JobId);
                }
            }

            for (var i = 0; i < this.jobs.Count; i++)
            {
                var j = this.jobs[i];
                if (j.DeviceId != deviceId)
                {
                    continue;
                }

                // Уже выданное задание отдаётся повторно, пока не придёт итог.
                if (j.Status == JobStatus.Dispatched)
                {
                    return j;
                }

                if (j.Status == JobStatus.Queued)
                {
                    var dispatched = j with { Status = JobStatus.Dispatched, DispatchedAt = now };
                    this.jobs[i] = dispatched;
                    return dispatched;
                }
            }

            return null;
        }
    }

    public ResultOutcome Complete(Guid jobId, Guid deviceId, bool success, string? error)
    {
        lock (this.sync)
        {
            var now = timeProvider.GetUtcNow();
            for (var i = 0; i < this.jobs.Count; i++)
            {
                var j = this.jobs[i];
                if (j.Status == JobStatus.Dispatched && now - j.DispatchedAt!.Value > dispatchTimeout)
                {
                    this.jobs[i] = j with { Status = JobStatus.Failed, Error = "DEVICE_TIMEOUT", FinishedAt = now };
                    this.timedOut.Add(j.JobId);
                }
            }

            var index = this.jobs.FindIndex(x => x.JobId == jobId);
            if (index < 0)
            {
                return ResultOutcome.JobNotFound;
            }

            var job = this.jobs[index];
            if (job.DeviceId != deviceId)
            {
                return ResultOutcome.WrongDevice;
            }

            if (job.Status == JobStatus.Queued)
            {
                return ResultOutcome.WrongState;
            }

            // Итог принимается для выданного задания и для задания, закрытого по таймауту.
            if (job.Status == JobStatus.Dispatched || this.timedOut.Remove(jobId))
            {
                this.jobs[index] = job with
                {
                    Status = success ? JobStatus.Printed : JobStatus.Failed,
                    Error = success ? null : error,
                    FinishedAt = now,
                };
                return ResultOutcome.Accepted;
            }

            // Повтор того же итога: принимается, состояние не меняется.
            return (job.Status == JobStatus.Printed) == success ? ResultOutcome.Accepted : ResultOutcome.WrongState;
        }
    }

    public PrintJob? Find(Guid jobId)
    {
        lock (this.sync)
        {
            var now = timeProvider.GetUtcNow();
            for (var i = 0; i < this.jobs.Count; i++)
            {
                var j = this.jobs[i];
                if (j.Status == JobStatus.Dispatched && now - j.DispatchedAt!.Value > dispatchTimeout)
                {
                    this.jobs[i] = j with { Status = JobStatus.Failed, Error = "DEVICE_TIMEOUT", FinishedAt = now };
                    this.timedOut.Add(j.JobId);
                }
            }

            return this.jobs.Find(x => x.JobId == jobId);
        }
    }
}
