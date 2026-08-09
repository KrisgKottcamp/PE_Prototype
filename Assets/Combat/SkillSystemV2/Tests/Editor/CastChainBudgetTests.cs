using NUnit.Framework;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class CastChainBudgetTests
    {
        [Test]
        public void RejectsDepthBeyondConfiguredLimit()
        {
            var budget = new CastChainBudget(
                maxDepth: 2,
                maxActivations: 10);

            Assert.That(budget.TryConsumeActivation(0), Is.True);
            Assert.That(budget.TryConsumeActivation(2), Is.True);
            Assert.That(budget.TryConsumeActivation(3), Is.False);
            Assert.That(budget.ActivationCount, Is.EqualTo(2));
        }

        [Test]
        public void RejectsActivationsBeyondRootBudget()
        {
            var budget = new CastChainBudget(
                maxDepth: 5,
                maxActivations: 2);

            Assert.That(budget.TryConsumeActivation(0), Is.True);
            Assert.That(budget.TryConsumeActivation(1), Is.True);
            Assert.That(budget.TryConsumeActivation(1), Is.False);
            Assert.That(budget.ActivationCount, Is.EqualTo(2));
        }
    }
}
