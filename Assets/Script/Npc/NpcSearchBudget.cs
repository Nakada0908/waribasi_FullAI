using System;
using System.Diagnostics;

namespace Waribasi.Npc
{
    public sealed class NpcSearchOptions
    {
        public int HypothesisCount { get; set; } = 8;
        public int DrawSamples { get; set; } = 2;
        public int RootWidth { get; set; } = 6;
        public int ReplyWidth { get; set; } = 3;
        public int MaxApplications { get; set; } = 40000;
        public int MaxWorkItems { get; set; } = 200000;
        public double MaxMilliseconds { get; set; } = 600;
        public double SliceMilliseconds { get; set; } = 3;
        public double NearScore { get; set; } = 12;

        internal NpcSearchOptions Copy()
        {
            if (HypothesisCount < 1 || HypothesisCount > 64 || DrawSamples < 1 || DrawSamples > 8 ||
                RootWidth < 1 || RootWidth > 88 || ReplyWidth < 1 || ReplyWidth > 88 ||
                MaxApplications < 1 || MaxWorkItems < 1 || MaxMilliseconds < 0 ||
                SliceMilliseconds <= 0 || NearScore < 0 || double.IsNaN(MaxMilliseconds) ||
                double.IsInfinity(MaxMilliseconds) || double.IsNaN(SliceMilliseconds) ||
                double.IsInfinity(SliceMilliseconds) || double.IsNaN(NearScore) || double.IsInfinity(NearScore))
            {
                throw new ArgumentOutOfRangeException(nameof(NpcSearchOptions));
            }
            return (NpcSearchOptions)MemberwiseClone();
        }
    }

    public enum NpcSearchStop
    {
        Running,
        Completed,
        ApplicationLimit,
        WorkLimit,
        TimeLimit,
        Cancelled,
        InvalidHypothesis
    }

    public sealed class NpcSearchStatistics
    {
        public int Applications { get; internal set; }
        public int WorkItems { get; internal set; }
        public int Candidates { get; internal set; }
        public int Hypotheses { get; internal set; }
        public int Scenarios { get; internal set; }
        public int CompletedDeepScenarios { get; internal set; }
        public int CompletedDepth { get; internal set; }
        public double CpuMilliseconds { get; internal set; }
        public double WallMilliseconds { get; internal set; }
        public double MaximumSliceMilliseconds { get; internal set; }
        public bool Fallback { get; internal set; } = true;
        public NpcSearchStop Stop { get; internal set; }
        public string Diagnostic { get; internal set; }
    }

    internal sealed class NpcSearchBudget
    {
        private readonly NpcSearchOptions options;
        private readonly Stopwatch wall = new Stopwatch();
        internal NpcSearchStatistics Statistics { get; } = new NpcSearchStatistics();
        internal bool Stopped => Statistics.Stop != NpcSearchStop.Running;

        internal NpcSearchBudget(NpcSearchOptions options)
        {
            this.options = options;
        }

        internal bool Check()
        {
            if (!wall.IsRunning && !Stopped)
            {
                wall.Start();
            }
            if (!Stopped && options.MaxMilliseconds > 0 && wall.Elapsed.TotalMilliseconds >= options.MaxMilliseconds)
            {
                Stop(NpcSearchStop.TimeLimit);
            }
            return !Stopped;
        }

        internal bool Spend(bool application = false)
        {
            if (!Check())
            {
                return false;
            }
            if (Statistics.WorkItems >= options.MaxWorkItems)
            {
                Stop(NpcSearchStop.WorkLimit);
                return false;
            }
            if (application && Statistics.Applications >= options.MaxApplications)
            {
                Stop(NpcSearchStop.ApplicationLimit);
                return false;
            }
            Statistics.WorkItems++;
            if (application)
            {
                Statistics.Applications++;
            }
            return true;
        }

        internal void Stop(NpcSearchStop reason)
        {
            if (!Stopped)
            {
                Statistics.Stop = reason;
                wall.Stop();
            }
            Statistics.WallMilliseconds = wall.Elapsed.TotalMilliseconds;
        }
    }
}
