using System;
using System.Collections.Generic;
using System.Threading;

namespace LobotJR
{

    public class FutureTaskRegistry
    {

        private readonly IList<FutureTask> futureTasks;
        public IReadOnlyList<FutureTask> FutureTasks { get; }
        private readonly IList<FutureTask> toRemove;

        private ManualResetEvent wait;

        public FutureTaskRegistry()
        {
            this.futureTasks = new List<FutureTask>();
            this.toRemove = new List<FutureTask>();
            this.wait = new ManualResetEvent(false);
        }

        public void Run()
        {
            TimeSpan min = TimeSpan.FromMinutes(10);
            while (true)
            {
                wait.Reset();
                foreach (var ft in futureTasks)
                {
                    if (!ft.TryFire())
                        min = min.CompareTo(ft.GetDelay()) == -1 ? min : ft.GetDelay();

                }
                Cleanup();
                wait.WaitOne(min, true);
                //Thread.Sleep((int)min.TotalMilliseconds);
            }
        }

        public void register(FutureTask ft)
        {
            futureTasks.Add(ft);
            wait.Set();
        }
        public void enqueue(FutureTask ft)
        {
            lock (toRemove)
            {
                toRemove.Add(ft);
            }
            //futureTasks.Remove(ft);
        }

        private void Cleanup()
        {
            lock (toRemove)
            {
                foreach (var ft in toRemove)
                    futureTasks.Remove(ft);
            }
        }
    }

    public interface FutureTask
    {
        void Fire();
        bool TryFire();
        TimeSpan GetDelay();
        bool Cancel(bool whilePerforming);
        bool IsDone();
    }

    public class SingularScheduledTask : FutureTask
    {
        private readonly FutureTaskRegistry registry;
        private readonly Action action;
        private readonly TimeSpan delay;
        private readonly DateTime created;
        private bool completed;

        public SingularScheduledTask(FutureTaskRegistry registry, TimeSpan delay, Action action)
        {
            this.registry = registry;
            this.delay = delay;
            this.action = action;
            this.created = DateTime.Now;
            this.registry.register(this);
        }

        public TimeSpan GetDelay()
        {
            return delay.Subtract(DateTime.Now.Subtract(created));
        }

        public bool Cancel(bool b)
        {
            completed = true;
            registry.enqueue(this);
            return true;
        }

        public bool IsDone()
        {
            return completed;
        }

        public bool TryFire()
        {
            if (delay.CompareTo(DateTime.Now.Subtract(created)) == -1 && !completed)
            {
                Fire();
                return true;
            }
            return false;
        }

        public void Fire()
        {
            action?.Invoke();
            completed = true;
            registry.enqueue(this);
        }
    }

    public class ReoccurringScheduledTask : FutureTask
    {
        private readonly FutureTaskRegistry registry;
        private readonly Action action;
        private readonly TimeSpan delay;
        private DateTime lastFire;
        private bool completed;

        public ReoccurringScheduledTask(FutureTaskRegistry registry,
            TimeSpan delay, Action action)
        {
            this.registry = registry;
            this.delay = delay;
            this.action = action;
            this.lastFire = DateTime.Now;
            this.registry.register(this);
        }

        public TimeSpan GetDelay()
        {
            return delay.Subtract(DateTime.Now.Subtract(lastFire));
        }

        public bool Cancel(bool b)
        {
            completed = true;
            registry.enqueue(this);
            return true;
        }

        public bool IsDone()
        {
            return completed;
        }

        public bool TryFire()
        {
            if (delay.CompareTo(DateTime.Now.Subtract(lastFire)) == -1 && !completed)
            {
                Fire();
                return true;
            }
            return false;
        }

        public void Fire()
        {
            action?.Invoke();
            lastFire = DateTime.Now;
        }
    }
}