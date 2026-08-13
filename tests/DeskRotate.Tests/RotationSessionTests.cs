using DeskRotate;
using Xunit;

namespace DeskRotate.Tests;

public class RotationSessionTests
{
    [Theory]
    [InlineData(1, 5, 2)]
    [InlineData(2, 5, 3)]
    [InlineData(4, 5, 5)]
    public void ComputeNextDesktopIndex_IncrementsWithinRange(int current, int rangeEnd, int expectedNext)
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: rangeEnd, intervalSeconds: 10, targetCycleCount: 3)
        {
            CurrentDesktopIndex = current,
        };

        Assert.Equal(expectedNext, session.ComputeNextDesktopIndex());
    }

    [Fact]
    public void ComputeNextDesktopIndex_WrapsFromRangeEndToRangeStart()
    {
        // FR-002: 핑퐁이 아닌 순환(wrap) — 범위의 마지막 데스크톱에서 범위의 시작으로 되돌아간다.
        var session = new RotationSession(rangeStart: 1, rangeEnd: 5, intervalSeconds: 10, targetCycleCount: 3)
        {
            CurrentDesktopIndex = 5,
        };

        Assert.Equal(1, session.ComputeNextDesktopIndex());
    }

    [Fact]
    public void ComputeNextDesktopIndex_NonUnitRange_WrapsFromEndToStart()
    {
        // FR-003: 데스크톱 개수가 아니라 범위(예: 3~7)로 지정 — 7 다음은 1이 아니라 3으로 순환한다.
        var session = new RotationSession(rangeStart: 3, rangeEnd: 7, intervalSeconds: 10, targetCycleCount: 3)
        {
            CurrentDesktopIndex = 7,
        };

        Assert.Equal(3, session.ComputeNextDesktopIndex());
    }

    [Fact]
    public void ComputeNextDesktopIndex_NonUnitRange_IncrementsWithinRange()
    {
        var session = new RotationSession(rangeStart: 3, rangeEnd: 7, intervalSeconds: 10, targetCycleCount: 3)
        {
            CurrentDesktopIndex = 4,
        };

        Assert.Equal(5, session.ComputeNextDesktopIndex());
    }

    [Fact]
    public void ComputeNextDesktopIndex_SingleDesktopRange_AlwaysReturnsItself()
    {
        var session = new RotationSession(rangeStart: 5, rangeEnd: 5, intervalSeconds: 10, targetCycleCount: 3)
        {
            CurrentDesktopIndex = 5,
        };

        Assert.Equal(5, session.ComputeNextDesktopIndex());
    }

    [Fact]
    public void Constructor_InitializesCurrentDesktopIndexToRangeStart()
    {
        var session = new RotationSession(rangeStart: 3, rangeEnd: 7, intervalSeconds: 10, targetCycleCount: 3);

        Assert.Equal(3, session.CurrentDesktopIndex);
        Assert.Equal(5, session.DesktopCount);
    }

    [Fact]
    public void TargetReached_IsFalseUntilCompletedCountMeetsTarget()
    {
        // DesktopCount=1이므로 목표 사이클 수 2 = 목표 총 전환 횟수 2 (FR-013).
        var session = new RotationSession(rangeStart: 1, rangeEnd: 1, intervalSeconds: 10, targetCycleCount: 2);

        Assert.False(session.TargetReached);

        session.RecordSwitchCompleted(1);
        Assert.False(session.TargetReached);

        session.RecordSwitchCompleted(1);
        Assert.True(session.TargetReached);
    }

    [Fact]
    public void RecordSwitchCompleted_IncrementsCompletedCountAndPerDesktopCount()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 3, intervalSeconds: 10, targetCycleCount: 5);

        session.RecordSwitchCompleted(2);

        Assert.Equal(1, session.CompletedSwitchCount);
        Assert.Equal(2, session.CurrentDesktopIndex);
        Assert.Equal(1, session.PerDesktopSwitchCounts[2]);
        Assert.Equal(0, session.PerDesktopSwitchCounts[1]);
        Assert.Equal(0, session.RetryCount);
        Assert.Equal(10, session.RemainingSecondsToNextSwitch);
    }

    [Fact]
    public void PerDesktopSwitchCounts_AreKeyedByAbsoluteDesktopNumber()
    {
        // FR-003: 범위 3~7이면 표시/집계도 절대 번호(3,4,...,7) 기준이어야 한다(1부터 다시 세지 않음).
        var session = new RotationSession(rangeStart: 3, rangeEnd: 7, intervalSeconds: 10, targetCycleCount: 5);

        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, session.PerDesktopSwitchCounts.Keys.OrderBy(k => k));
        Assert.All(session.PerDesktopSwitchCounts.Values, count => Assert.Equal(0, count));

        session.RecordSwitchCompleted(5);

        Assert.Equal(1, session.PerDesktopSwitchCounts[5]);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    public void Constructor_RejectsNonPositiveRangeStart(int rangeStart, int rangeEnd)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RotationSession(rangeStart, rangeEnd, intervalSeconds: 1, targetCycleCount: 1));
    }

    [Fact]
    public void Constructor_RejectsRangeEndBelowRangeStart()
    {
        // spec.md FR-027 / Edge Cases: 범위 끝이 시작보다 작으면(예: 7~3) 유효하지 않다.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RotationSession(rangeStart: 7, rangeEnd: 3, intervalSeconds: 1, targetCycleCount: 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveInterval(int intervalSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RotationSession(rangeStart: 1, rangeEnd: 3, intervalSeconds, targetCycleCount: 1));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveTargetCycleCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RotationSession(rangeStart: 1, rangeEnd: 1, intervalSeconds: 1, targetCycleCount: 0));
    }

    // --- 목표 사이클 수 → 목표 총 전환 횟수 환산, 현재 사이클 번호 (FR-013, FR-030) ---

    [Fact]
    public void TargetSwitchCount_IsCycleCountTimesDesktopCount()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 3, intervalSeconds: 10, targetCycleCount: 3);

        Assert.Equal(9, session.TargetSwitchCount);
    }

    [Fact]
    public void CurrentCycleNumber_AdvancesEveryFullCycleAndCapsAtTargetCycleCount()
    {
        // 범위 1~3(DesktopCount=3), 목표 사이클 3 → 목표 총 전환 횟수 9.
        var session = new RotationSession(rangeStart: 1, rangeEnd: 3, intervalSeconds: 10, targetCycleCount: 3);

        Assert.Equal(1, session.CurrentCycleNumber);

        session.RecordSwitchCompleted(1);
        session.RecordSwitchCompleted(2);
        session.RecordSwitchCompleted(3);
        Assert.Equal(2, session.CurrentCycleNumber);

        session.RecordSwitchCompleted(1);
        session.RecordSwitchCompleted(2);
        session.RecordSwitchCompleted(3);
        Assert.Equal(3, session.CurrentCycleNumber);

        session.RecordSwitchCompleted(1);
        session.RecordSwitchCompleted(2);
        session.RecordSwitchCompleted(3);
        Assert.True(session.TargetReached);
        Assert.Equal(3, session.CurrentCycleNumber);
    }

    [Fact]
    public void ShowSecondsUnitAndShowCycleNumber_DefaultToOn()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 3, intervalSeconds: 10, targetCycleCount: 1);

        Assert.True(session.ShowSecondsUnit);
        Assert.True(session.ShowCycleNumber);
    }

    [Fact]
    public void ShowSecondsUnitAndShowCycleNumber_CanBeOverridden()
    {
        var session = new RotationSession(
            rangeStart: 1, rangeEnd: 3, intervalSeconds: 10, targetCycleCount: 1,
            showSecondsUnit: false, showCycleNumber: false);

        Assert.False(session.ShowSecondsUnit);
        Assert.False(session.ShowCycleNumber);
    }

    // --- User Story 3: 남은 시간 및 총 예상 실행 시간 계산 (FR-005, FR-014) ---

    [Fact]
    public void TotalPlannedRuntimeSeconds_IsIntervalTimesTargetCount()
    {
        // DesktopCount=3, 목표 사이클 2 → 목표 총 전환 횟수 6 → 600 * 6 = 3600.
        var session = new RotationSession(rangeStart: 1, rangeEnd: 3, intervalSeconds: 600, targetCycleCount: 2);

        Assert.Equal(3600, session.TotalPlannedRuntimeSeconds);
    }

    [Fact]
    public void Tick_DecrementsRemainingCountersBySecond()
    {
        // DesktopCount=2, 목표 사이클 1 → 목표 총 전환 횟수 2 → 총 예상 실행 시간 20초.
        var session = new RotationSession(rangeStart: 1, rangeEnd: 2, intervalSeconds: 10, targetCycleCount: 1);

        session.Tick();

        Assert.Equal(9, session.RemainingSecondsToNextSwitch);
        Assert.Equal(19, session.RemainingSecondsToFinish);
    }

    [Fact]
    public void Tick_DoesNotGoBelowZero()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 1, intervalSeconds: 1, targetCycleCount: 1);

        session.Tick();
        session.Tick();
        session.Tick();

        Assert.Equal(0, session.RemainingSecondsToNextSwitch);
        Assert.Equal(0, session.RemainingSecondsToFinish);
    }

    [Fact]
    public void Tick_StopsCountingDownOnceTargetReached()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 1, intervalSeconds: 10, targetCycleCount: 1);
        session.RecordSwitchCompleted(1);

        Assert.True(session.TargetReached);
        int remainingBefore = session.RemainingSecondsToFinish;

        session.Tick();

        Assert.Equal(remainingBefore, session.RemainingSecondsToFinish);
    }

    [Fact]
    public void RecordSwitchCompleted_ResetsNextSwitchCountdownToInterval()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 3, intervalSeconds: 15, targetCycleCount: 5);
        session.Tick();
        session.Tick();

        session.RecordSwitchCompleted(2);

        Assert.Equal(15, session.RemainingSecondsToNextSwitch);
    }

    // --- 일시정지/재개 (FR-035) ---

    [Fact]
    public void IsPaused_DefaultsToFalse()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 3, intervalSeconds: 10, targetCycleCount: 1);

        Assert.False(session.IsPaused);
    }

    [Fact]
    public void TogglePause_FlipsIsPaused()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 3, intervalSeconds: 10, targetCycleCount: 1);

        session.TogglePause();
        Assert.True(session.IsPaused);

        session.TogglePause();
        Assert.False(session.IsPaused);
    }

    [Fact]
    public void Tick_DoesNotCountDownWhilePaused()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 2, intervalSeconds: 10, targetCycleCount: 1);

        session.TogglePause();
        session.Tick();
        session.Tick();

        Assert.Equal(10, session.RemainingSecondsToNextSwitch);
        Assert.Equal(20, session.RemainingSecondsToFinish);
    }

    [Fact]
    public void Tick_ResumesCountingDownAfterUnpausing()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 2, intervalSeconds: 10, targetCycleCount: 1);

        session.TogglePause();
        session.Tick();
        session.TogglePause();
        session.Tick();

        Assert.Equal(9, session.RemainingSecondsToNextSwitch);
    }

    // --- 원형 진행률 그래픽 (FR-036) ---

    [Fact]
    public void ShowProgressRing_DefaultsToOn()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 3, intervalSeconds: 10, targetCycleCount: 1);

        Assert.True(session.ShowProgressRing);
    }

    [Fact]
    public void ShowProgressRing_CanBeOverridden()
    {
        var session = new RotationSession(
            rangeStart: 1, rangeEnd: 3, intervalSeconds: 10, targetCycleCount: 1,
            showProgressRing: false);

        Assert.False(session.ShowProgressRing);
    }

    [Fact]
    public void NextSwitchProgressRatio_StartsAtFull()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 2, intervalSeconds: 10, targetCycleCount: 1);

        Assert.Equal(1.0, session.NextSwitchProgressRatio);
    }

    [Fact]
    public void NextSwitchProgressRatio_DecreasesLinearlyAsCountdownTicks()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 2, intervalSeconds: 10, targetCycleCount: 1);

        session.Tick();
        session.Tick();

        // RemainingSecondsToNextSwitch = 8/10.
        Assert.Equal(0.8, session.NextSwitchProgressRatio, precision: 5);
    }

    [Fact]
    public void NextSwitchProgressRatio_FreezesWhilePaused()
    {
        var session = new RotationSession(rangeStart: 1, rangeEnd: 2, intervalSeconds: 10, targetCycleCount: 1);
        session.Tick();
        double ratioBeforePause = session.NextSwitchProgressRatio;

        session.TogglePause();
        session.Tick();
        session.Tick();

        Assert.Equal(ratioBeforePause, session.NextSwitchProgressRatio);
    }
}
