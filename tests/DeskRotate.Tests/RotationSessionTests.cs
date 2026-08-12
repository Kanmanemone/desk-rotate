using DeskRotate;
using Xunit;

namespace DeskRotate.Tests;

public class RotationSessionTests
{
    [Theory]
    [InlineData(1, 5, 2)]
    [InlineData(2, 5, 3)]
    [InlineData(4, 5, 5)]
    public void ComputeNextDesktopIndex_IncrementsWithinRange(int current, int total, int expectedNext)
    {
        var session = new RotationSession(totalDesktopCount: total, intervalSeconds: 10, targetSwitchCount: 3)
        {
            CurrentDesktopIndex = current,
        };

        Assert.Equal(expectedNext, session.ComputeNextDesktopIndex());
    }

    [Fact]
    public void ComputeNextDesktopIndex_WrapsFromLastToFirst()
    {
        // FR-002: 핑퐁이 아닌 순환(wrap) — 마지막 데스크톱에서 첫 번째로 되돌아간다.
        var session = new RotationSession(totalDesktopCount: 5, intervalSeconds: 10, targetSwitchCount: 3)
        {
            CurrentDesktopIndex = 5,
        };

        Assert.Equal(1, session.ComputeNextDesktopIndex());
    }

    [Fact]
    public void ComputeNextDesktopIndex_SingleDesktop_AlwaysReturnsItself()
    {
        var session = new RotationSession(totalDesktopCount: 1, intervalSeconds: 10, targetSwitchCount: 3)
        {
            CurrentDesktopIndex = 1,
        };

        Assert.Equal(1, session.ComputeNextDesktopIndex());
    }

    [Fact]
    public void TargetReached_IsFalseUntilCompletedCountMeetsTarget()
    {
        var session = new RotationSession(totalDesktopCount: 3, intervalSeconds: 10, targetSwitchCount: 2);

        Assert.False(session.TargetReached);

        session.RecordSwitchCompleted(2);
        Assert.False(session.TargetReached);

        session.RecordSwitchCompleted(3);
        Assert.True(session.TargetReached);
    }

    [Fact]
    public void RecordSwitchCompleted_IncrementsCompletedCountAndPerDesktopCount()
    {
        var session = new RotationSession(totalDesktopCount: 3, intervalSeconds: 10, targetSwitchCount: 5);

        session.RecordSwitchCompleted(2);

        Assert.Equal(1, session.CompletedSwitchCount);
        Assert.Equal(2, session.CurrentDesktopIndex);
        Assert.Equal(1, session.PerDesktopSwitchCounts[2]);
        Assert.Equal(0, session.PerDesktopSwitchCounts[1]);
        Assert.Equal(0, session.RetryCount);
        Assert.Equal(10, session.RemainingSecondsToNextSwitch);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void Constructor_RejectsNonPositiveInputs(int totalDesktopCount, int intervalSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RotationSession(totalDesktopCount, intervalSeconds, targetSwitchCount: 1));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveTargetSwitchCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RotationSession(totalDesktopCount: 1, intervalSeconds: 1, targetSwitchCount: 0));
    }

    // --- User Story 3: 남은 시간 및 총 예상 실행 시간 계산 (FR-005, FR-014) ---

    [Fact]
    public void TotalPlannedRuntimeSeconds_IsIntervalTimesTargetCount()
    {
        var session = new RotationSession(totalDesktopCount: 3, intervalSeconds: 600, targetSwitchCount: 6);

        Assert.Equal(3600, session.TotalPlannedRuntimeSeconds);
    }

    [Fact]
    public void Tick_DecrementsRemainingCountersBySecond()
    {
        var session = new RotationSession(totalDesktopCount: 3, intervalSeconds: 10, targetSwitchCount: 2);

        session.Tick();

        Assert.Equal(9, session.RemainingSecondsToNextSwitch);
        Assert.Equal(19, session.RemainingSecondsToFinish);
    }

    [Fact]
    public void Tick_DoesNotGoBelowZero()
    {
        var session = new RotationSession(totalDesktopCount: 3, intervalSeconds: 1, targetSwitchCount: 1);

        session.Tick();
        session.Tick();
        session.Tick();

        Assert.Equal(0, session.RemainingSecondsToNextSwitch);
        Assert.Equal(0, session.RemainingSecondsToFinish);
    }

    [Fact]
    public void Tick_StopsCountingDownOnceTargetReached()
    {
        var session = new RotationSession(totalDesktopCount: 3, intervalSeconds: 10, targetSwitchCount: 1);
        session.RecordSwitchCompleted(2);

        Assert.True(session.TargetReached);
        int remainingBefore = session.RemainingSecondsToFinish;

        session.Tick();

        Assert.Equal(remainingBefore, session.RemainingSecondsToFinish);
    }

    [Fact]
    public void RecordSwitchCompleted_ResetsNextSwitchCountdownToInterval()
    {
        var session = new RotationSession(totalDesktopCount: 3, intervalSeconds: 15, targetSwitchCount: 5);
        session.Tick();
        session.Tick();

        session.RecordSwitchCompleted(2);

        Assert.Equal(15, session.RemainingSecondsToNextSwitch);
    }
}
