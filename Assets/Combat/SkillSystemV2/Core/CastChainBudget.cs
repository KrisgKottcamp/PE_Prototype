using System.Threading;

namespace ProjectEri.SkillSystemV2
{
    public sealed class CastChainBudget
    {
        private static long nextRootCastId;

        private readonly object gate = new object();
        private int activationCount;

        public long RootCastId { get; }
        public int MaxDepth { get; }
        public int MaxActivations { get; }

        public int ActivationCount
        {
            get
            {
                lock (gate)
                    return activationCount;
            }
        }

        public CastChainBudget(int maxDepth, int maxActivations)
            : this(
                Interlocked.Increment(ref nextRootCastId),
                maxDepth,
                maxActivations)
        {
        }

        internal CastChainBudget(
            long rootCastId,
            int maxDepth,
            int maxActivations)
        {
            RootCastId = rootCastId;
            MaxDepth = maxDepth < 0 ? 0 : maxDepth;
            MaxActivations = maxActivations < 1 ? 1 : maxActivations;
        }

        public bool CanActivate(int chainDepth)
        {
            lock (gate)
            {
                return chainDepth >= 0 &&
                       chainDepth <= MaxDepth &&
                       activationCount < MaxActivations;
            }
        }

        public bool TryConsumeActivation(int chainDepth)
        {
            lock (gate)
            {
                if (chainDepth < 0 ||
                    chainDepth > MaxDepth ||
                    activationCount >= MaxActivations)
                {
                    return false;
                }

                activationCount++;
                return true;
            }
        }
    }
}
