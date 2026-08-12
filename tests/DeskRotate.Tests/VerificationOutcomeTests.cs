using DeskRotate;
using Xunit;

namespace DeskRotate.Tests;

public class VerificationOutcomeTests
{
    [Fact]
    public void Matched_IsTrueWhenIntendedEqualsActual()
    {
        var outcome = new VerificationOutcome(IntendedDesktopIndex: 3, ActualDesktopIndex: 3);

        Assert.True(outcome.Matched);
    }

    [Fact]
    public void Matched_IsFalseWhenIntendedDiffersFromActual()
    {
        var outcome = new VerificationOutcome(IntendedDesktopIndex: 3, ActualDesktopIndex: 2);

        Assert.False(outcome.Matched);
    }

    [Fact]
    public void RetryPolicy_CompletesWhenOutcomeMatched()
    {
        var outcome = new VerificationOutcome(2, 2);

        RetryDecision decision = RetryPolicy.Decide(outcome, retryCountSoFar: 0);

        Assert.Equal(RetryDecision.Complete, decision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RetryPolicy_RetriesWhenMismatchedAndUnderLimit(int retryCountSoFar)
    {
        var outcome = new VerificationOutcome(2, 3);

        RetryDecision decision = RetryPolicy.Decide(outcome, retryCountSoFar);

        Assert.Equal(RetryDecision.Retry, decision);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void RetryPolicy_SelfCorrectsWhenMismatchedAndLimitExhausted(int retryCountSoFar)
    {
        // FR-019: 재시도 한도(MaxRetryAttempts=3) 소진 시 무한 재시도하지 않고 자가 보정한다.
        var outcome = new VerificationOutcome(2, 3);

        RetryDecision decision = RetryPolicy.Decide(outcome, retryCountSoFar);

        Assert.Equal(RetryDecision.SelfCorrect, decision);
    }

    [Fact]
    public void RetryPolicy_NeverRetriesIndefinitely()
    {
        var outcome = new VerificationOutcome(1, 2);

        for (int retryCount = 0; retryCount <= RetryPolicy.MaxRetryAttempts + 5; retryCount++)
        {
            RetryDecision decision = RetryPolicy.Decide(outcome, retryCount);
            if (retryCount >= RetryPolicy.MaxRetryAttempts)
            {
                Assert.Equal(RetryDecision.SelfCorrect, decision);
            }
        }
    }
}
