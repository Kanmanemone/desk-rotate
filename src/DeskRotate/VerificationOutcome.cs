namespace DeskRotate;

/// <summary>
/// 전환 시도 하나에 대한 검증 결과 값 객체 (data-model.md VerificationOutcome).
/// FR-017(검증)의 결과이며, FR-018(재시도)·FR-019(자가 보정) 판단의 입력이 된다.
/// </summary>
public readonly record struct VerificationOutcome(int IntendedDesktopIndex, int ActualDesktopIndex)
{
    public bool Matched => IntendedDesktopIndex == ActualDesktopIndex;
}

/// <summary>검증 결과와 재시도 횟수만으로 다음 행동을 판단하는 순수 로직 (FR-017~FR-019).</summary>
public enum RetryDecision
{
    /// <summary>검증 일치 — 전환 완료로 기록.</summary>
    Complete,

    /// <summary>불일치이고 재시도 한도 이내 — 키 입력을 재시도.</summary>
    Retry,

    /// <summary>불일치이고 재시도 한도 소진 — 실제 위치로 상태를 자가 보정 (무한 재시도 금지).</summary>
    SelfCorrect,
}

public static class RetryPolicy
{
    /// <summary>재시도 한도 (research.md §5 결정값).</summary>
    public const int MaxRetryAttempts = 3;

    public static RetryDecision Decide(VerificationOutcome outcome, int retryCountSoFar)
    {
        if (outcome.Matched)
        {
            return RetryDecision.Complete;
        }

        return retryCountSoFar >= MaxRetryAttempts ? RetryDecision.SelfCorrect : RetryDecision.Retry;
    }
}
