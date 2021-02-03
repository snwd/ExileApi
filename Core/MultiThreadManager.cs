using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JM.LinqFaster;

namespace ExileCore
{
    [DebuggerDisplay("Name: {Name}, Elapsed: {ElapsedMs}, Completed: {IsCompleted}, Failed: {IsFailed}")]
    public class Job
    {
        public volatile bool IsCompleted;
        public volatile bool IsFailed;
        public volatile bool IsQueued;

        public Job(string name, Action work)
        {
            Name = name;
            Work = work;
        }

        public Action Work { get; set; }
        public string Name { get; set; }
        public double ElapsedMs { get; set; }
    }

    public class MultiThreadManager
    {
        private readonly ConcurrentQueue<Job> Jobs = new ConcurrentQueue<Job>();

        public void RunJobs()
        {
            while (!Jobs.IsEmpty)
            {
                Job job;
                var success = Jobs.TryDequeue(out job);
                if (!success) continue;

                Task.Run(() =>
                {
                    var jobTimer = new Stopwatch();
                    jobTimer.Start();

                    var doWorkTask = Task.Run(job.Work);
                    doWorkTask.Wait();

                    jobTimer.Stop();
                    job.ElapsedMs = jobTimer.ElapsedMilliseconds;
                });
            }
        }

        public Job AddJob(Job job)
        {
            Jobs.Enqueue(job);
            job.IsQueued = true;
            return job;
        }

        public Job AddJob(Action action, string name)
        {
            var newJob = new Job(name, action);

            return AddJob(newJob);
        }
    }
}
