namespace DeskRotate;

/// <summary>
/// 앱 실행 중 유지되는 유일한 상태 객체 (data-model.md RotationSession).
/// 세션 범위에서만 유지되며 영속화되지 않는다 (spec.md FR-007).
/// </summary>
public sealed class RotationSession
{
    /// <summary>순회 범위의 시작 데스크톱 번호(1-based, 포함).</summary>
    public int RangeStart { get; }

    /// <summary>순회 범위의 끝 데스크톱 번호(1-based, 포함).</summary>
    public int RangeEnd { get; }

    /// <summary>순회 대상 데스크톱 개수 (파생값, 순환 계산에만 내부적으로 사용).</summary>
    public int DesktopCount => RangeEnd - RangeStart + 1;

    public int IntervalSeconds { get; }

    /// <summary>사용자가 입력한 목표 사이클 수 — 범위 안 데스크톱을 한 번씩 모두 순회하는 것이 1사이클 (FR-013).</summary>
    public int TargetCycleCount { get; }

    /// <summary>목표 총 전환 횟수 — TargetCycleCount * DesktopCount로 환산되며, 기존 회전·통계 로직은 이 값을 그대로 사용한다 (FR-013).</summary>
    public int TargetSwitchCount { get; }

    /// <summary>전환 간격 × 목표 총 전환 횟수 (FR-014).</summary>
    public int TotalPlannedRuntimeSeconds => IntervalSeconds * TargetSwitchCount;

    /// <summary>최소 보기 숫자 뒤에 "초"를 붙일지 여부 (FR-031).</summary>
    public bool ShowSecondsUnit { get; }

    /// <summary>최소 보기 앞에 "[N번째] "를 붙일지 여부 (FR-031).</summary>
    public bool ShowCycleNumber { get; }

    /// <summary>현재 진행 중인 사이클 번호(1-based), 목표 사이클 수를 넘지 않도록 캡핑된다 (FR-030).</summary>
    public int CurrentCycleNumber => Math.Min((CompletedSwitchCount / DesktopCount) + 1, TargetCycleCount);

    /// <summary>검증으로 확인된 현재 데스크톱의 절대 번호 (RangeStart..RangeEnd).</summary>
    public int CurrentDesktopIndex { get; set; }

    /// <summary>검증까지 완료된 누적 전환 횟수.</summary>
    public int CompletedSwitchCount { get; private set; }

    /// <summary>목표 총 전환 횟수에 도달했는지 여부 (FR-015).</summary>
    public bool TargetReached => CompletedSwitchCount >= TargetSwitchCount;

    /// <summary>다음 자동 전환까지 남은 시간(초). 목표 도달 시 의미 없음.</summary>
    public int RemainingSecondsToNextSwitch { get; private set; }

    /// <summary>프로그램 종료까지 남은 전체 시간(초).</summary>
    public int RemainingSecondsToFinish { get; private set; }

    /// <summary>가장 최근 전환 시도의 검증 결과 (아직 없으면 null).</summary>
    public VerificationOutcome? LastVerification { get; set; }

    /// <summary>진행 중인 전환 시도의 재시도 횟수.</summary>
    public int RetryCount { get; set; }

    /// <summary>절대 데스크톱 번호(RangeStart..RangeEnd)별 검증된 누적 전환 횟수 (FR-006, data-model.md PerDesktopSwitchCount).</summary>
    public IReadOnlyDictionary<int, int> PerDesktopSwitchCounts => _perDesktopSwitchCounts;

    private readonly Dictionary<int, int> _perDesktopSwitchCounts;

    public RotationSession(
        int rangeStart,
        int rangeEnd,
        int intervalSeconds,
        int targetCycleCount,
        bool showSecondsUnit = true,
        bool showCycleNumber = true)
    {
        if (rangeStart < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rangeStart), "1 이상이어야 합니다.");
        }

        if (rangeEnd < rangeStart)
        {
            throw new ArgumentOutOfRangeException(nameof(rangeEnd), "시작 번호 이상이어야 합니다.");
        }

        if (intervalSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "1 이상이어야 합니다.");
        }

        if (targetCycleCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetCycleCount), "1 이상이어야 합니다.");
        }

        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
        IntervalSeconds = intervalSeconds;
        TargetCycleCount = targetCycleCount;
        TargetSwitchCount = targetCycleCount * DesktopCount;
        ShowSecondsUnit = showSecondsUnit;
        ShowCycleNumber = showCycleNumber;

        CurrentDesktopIndex = rangeStart;
        RemainingSecondsToNextSwitch = intervalSeconds;
        RemainingSecondsToFinish = TotalPlannedRuntimeSeconds;

        _perDesktopSwitchCounts = new Dictionary<int, int>();
        for (int i = rangeStart; i <= rangeEnd; i++)
        {
            _perDesktopSwitchCounts[i] = 0;
        }
    }

    /// <summary>
    /// 현재 데스크톱 번호를 기준으로, 범위 안에서 균일 순환(wrap) 방식에 따른 다음 목표 데스크톱의
    /// 절대 번호를 계산한다 (FR-002 — 핑퐁 방식이 아닌 순환 방식, 범위 끝에서 범위 시작으로 되돌아감).
    /// </summary>
    public int ComputeNextDesktopIndex()
    {
        return CurrentDesktopIndex >= RangeEnd ? RangeStart : CurrentDesktopIndex + 1;
    }

    /// <summary>매초 호출되어 남은 시간 카운트다운을 진행한다 (목표 도달 후에는 더 감소하지 않음).</summary>
    public void Tick()
    {
        if (TargetReached)
        {
            return;
        }

        if (RemainingSecondsToNextSwitch > 0)
        {
            RemainingSecondsToNextSwitch--;
        }

        if (RemainingSecondsToFinish > 0)
        {
            RemainingSecondsToFinish--;
        }
    }

    /// <summary>
    /// 전환이 검증까지 완료됐을 때(정상 일치 또는 FR-019 자가 보정) 호출 —
    /// 데스크톱별 카운트와 누적 횟수를 늘리고 다음 간격 카운트다운을 리셋한다.
    /// </summary>
    public void RecordSwitchCompleted(int verifiedDesktopIndex)
    {
        CurrentDesktopIndex = verifiedDesktopIndex;
        CompletedSwitchCount++;
        RetryCount = 0;
        RemainingSecondsToNextSwitch = IntervalSeconds;

        _perDesktopSwitchCounts[verifiedDesktopIndex] = _perDesktopSwitchCounts.GetValueOrDefault(verifiedDesktopIndex) + 1;
    }
}
