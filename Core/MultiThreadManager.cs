using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JM.LinqFaster;

namespace ExileCore
{
    [DebuggerDisplay("Name: {Name}, Start: {Start}, End: {End}, Elapsed: {ElapsedMs}, IsRunning: {IsRunning}, Completed: {IsCompleted}, Failed: {IsFailed}")]
    public class Job
    {
        public bool IsCompleted;
        public bool IsFailed;
        public bool IsRunning;

        public Job(string name, Action work)
        {
            Name = name;
            Work = work;
        }

        public JobType Type { get; set; }
        public Action Work { get; set; }
        public string Name { get; set; }
        public double ElapsedMs { get; set; }

        public int Start { get; set; }
        public int End { get; set; }
    }

    public enum JobType
    {
        Core,
        Plugin
    }

    public class MultiThreadManager
    {
        private readonly ConcurrentDictionary<string, Job> Jobs = new ConcurrentDictionary<string, Job>();

        private readonly Queue<Job> JobHistory = new Queue<Job>(4096);

        public void RunJobs()
        {
            var toRemove = new List<string>();

            foreach (var job in Jobs)
            {
                if (job.Value.IsRunning) continue;
                if (job.Value.IsCompleted || job.Value.IsFailed)
                {
                    toRemove.Add(job.Key);
                    if (JobHistory.Count == 4096)
                    {
                        JobHistory.Dequeue();
                    }
                    JobHistory.Enqueue(job.Value);
                    continue;
                }

                job.Value.IsRunning = true;

                Task.Run(() =>
                {
                    try
                    {
                        var jobTimer = new Stopwatch();
                        jobTimer.Start();

                        job.Value.Start = DateTime.Now.Millisecond;

                        job.Value.Work();
                        job.Value.IsCompleted = true;
                        toRemove.Add(job.Key);
                        job.Value.End = DateTime.Now.Millisecond;

                        jobTimer.Stop();
                        job.Value.ElapsedMs = jobTimer.ElapsedMilliseconds;
                        if (job.Value.ElapsedMs > 33 && job.Key != "MainControl")
                        {
                            //
                        }
                    }
                    catch (Exception e)
                    {
                        job.Value.IsFailed = true;
                    }
                    finally
                    {
                        job.Value.IsRunning = false;
                    }
                });
            }

            foreach (var jobName in toRemove)
            {
                Jobs.TryRemove(jobName, out _);
            }
        }

        public bool AddJob(Job job, JobType type)
        {
            if (Jobs.ContainsKey(job.Name)) return false;

            job.Type = type;
            return Jobs.TryAdd(job.Name, job);            
        }

        public bool AddApiJob(Job job)
        {
            return AddJob(job, JobType.Core);
        }

        public bool AddPluginJob(Job job)
        {
            return AddJob(job, JobType.Plugin);
        }
    }
}
