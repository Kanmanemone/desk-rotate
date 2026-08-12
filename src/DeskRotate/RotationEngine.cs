namespace DeskRotate;

/// <summary>
/// 타이머 기반으로 전환 시도·검증·재시도·자가 보정을 오케스트레이션한다.
/// 초기 탐색(FR-022)과 초기 설정(FR-020)으로 순회 범위 안 데스크톱별 플로팅 창을 만들고,
/// 그 창들을 검증 앵커이자 표시 UI로 함께 사용한다.
/// </summary>
public sealed class RotationEngine
{
    /// <summary>연속 키 입력 사이 지연(ms) — 전환 애니메이션 완료 대기 및 재시도 간격 (research.md §5, FR-016).</summary>
    private const int InterKeystrokeDelayMilliseconds = 300;

    private readonly RotationSession _session;
    private readonly VirtualDesktopInterop _interop;
    private readonly KeyboardSimulator _keyboard;
    private readonly Dictionary<int, FloatingWindowForm> _desktopWindows = new();
    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>사용자가 어느 플로팅 창에서든 종료를 확정했을 때 발생한다 (FR-009).</summary>
    public event Action? ExitRequested;

    public RotationEngine(RotationSession session, VirtualDesktopInterop interop, KeyboardSimulator keyboard)
    {
        _session = session;
        _interop = interop;
        _keyboard = keyboard;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>
    /// 앱 시작 시 사용자 개입 없이, 실행 시점의 데스크톱을 절대 1번으로 가정하고 순회 범위의
    /// 시작까지 자동으로 이동한 뒤(초기 탐색, FR-022), 범위 안 각 데스크톱을 한 번씩 순회하며
    /// 플로팅 창을 생성·배치하고(FR-020), 범위 시작으로 복귀한다.
    /// </summary>
    public void PerformInitialSetup()
    {
        int seekSteps = _session.RangeStart - 1;
        for (int i = 0; i < seekSteps; i++)
        {
            _keyboard.SendSwitchKeystroke(SwitchDirection.Next);
            if (i < seekSteps - 1)
            {
                Thread.Sleep(InterKeystrokeDelayMilliseconds);
            }
        }

        _desktopWindows[_session.RangeStart] = CreateWindowOnCurrentDesktop(_session.RangeStart);

        for (int desktopIndex = _session.RangeStart + 1; desktopIndex <= _session.RangeEnd; desktopIndex++)
        {
            _keyboard.SendSwitchKeystroke(SwitchDirection.Next);
            Thread.Sleep(InterKeystrokeDelayMilliseconds);
            _desktopWindows[desktopIndex] = CreateWindowOnCurrentDesktop(desktopIndex);
        }

        ReturnToDesktop(_session.RangeStart);
        _session.CurrentDesktopIndex = _session.RangeStart;
    }

    /// <summary>초기 설정 이후 회전 타이머를 시작한다.</summary>
    public void Start()
    {
        foreach (FloatingWindowForm window in _desktopWindows.Values)
        {
            window.RefreshDisplay(_session);
        }

        _timer.Start();
    }

    private FloatingWindowForm CreateWindowOnCurrentDesktop(int desktopIndex)
    {
        var window = new FloatingWindowForm(desktopIndex);
        window.ExitConfirmed += HandleExitConfirmed;
        window.PlaceAtTopCenter();
        window.Show();
        return window;
    }

    /// <summary>현재 데스크톱(호출 시점엔 항상 RangeEnd)에서 targetIndex까지 되돌아간다.</summary>
    private void ReturnToDesktop(int targetIndex)
    {
        int stepsBack = _session.RangeEnd - targetIndex;
        for (int i = 0; i < stepsBack; i++)
        {
            _keyboard.SendSwitchKeystroke(SwitchDirection.Previous);
            if (i < stepsBack - 1)
            {
                Thread.Sleep(InterKeystrokeDelayMilliseconds);
            }
        }
    }

    private void OnTick()
    {
        _session.Tick();

        foreach (FloatingWindowForm window in _desktopWindows.Values)
        {
            window.RefreshDisplay(_session);
        }

        if (!_session.TargetReached && _session.RemainingSecondsToNextSwitch <= 0)
        {
            AttemptSwitch();
        }
    }

    /// <summary>
    /// 범위 안 다음 데스크톱으로 전환을 시도한다 (FR-001, FR-002). 범위 끝에서 시작으로 되돌아가는
    /// 경우에만 여러 번의 키 입력이 필요하며, 그 사이에 지연을 둔다 (FR-016).
    /// </summary>
    private void AttemptSwitch()
    {
        int intendedTarget = _session.ComputeNextDesktopIndex();
        bool isWrap = _session.CurrentDesktopIndex == _session.RangeEnd
            && intendedTarget == _session.RangeStart
            && _session.DesktopCount > 1;

        SwitchDirection direction = isWrap ? SwitchDirection.Previous : SwitchDirection.Next;

        if (isWrap)
        {
            int stepsBack = _session.DesktopCount - 1;
            for (int i = 0; i < stepsBack; i++)
            {
                _keyboard.SendSwitchKeystroke(SwitchDirection.Previous);
                if (i < stepsBack - 1)
                {
                    Thread.Sleep(InterKeystrokeDelayMilliseconds);
                }
            }
        }
        else
        {
            _keyboard.SendSwitchKeystroke(SwitchDirection.Next);
        }

        VerifyAndSettle(intendedTarget, direction);
    }

    /// <summary>
    /// 전환 시도 직후 공식 API로 검증하고(FR-017), 어긋나면 재시도하며(FR-018),
    /// 재시도 한도를 소진하면 실제 위치로 자가 보정한다(FR-019).
    /// </summary>
    private void VerifyAndSettle(int intendedTarget, SwitchDirection retryDirection)
    {
        while (true)
        {
            VerificationOutcome outcome = Verify(intendedTarget);
            _session.LastVerification = outcome;

            switch (RetryPolicy.Decide(outcome, _session.RetryCount))
            {
                case RetryDecision.Complete:
                    _session.RecordSwitchCompleted(intendedTarget);
                    return;

                case RetryDecision.SelfCorrect:
                    SelfCorrect(outcome.ActualDesktopIndex);
                    return;

                case RetryDecision.Retry:
                default:
                    _session.RetryCount++;
                    Thread.Sleep(InterKeystrokeDelayMilliseconds);
                    _keyboard.SendSwitchKeystroke(retryDirection);
                    break;
            }
        }
    }

    private VerificationOutcome Verify(int intendedTarget)
    {
        if (_desktopWindows.TryGetValue(intendedTarget, out FloatingWindowForm? intendedWindow)
            && _interop.IsWindowOnCurrentVirtualDesktop(intendedWindow.Handle))
        {
            return new VerificationOutcome(intendedTarget, intendedTarget);
        }

        int actualIndex = FindActualDesktopIndex() ?? intendedTarget;
        return new VerificationOutcome(intendedTarget, actualIndex);
    }

    private int? FindActualDesktopIndex()
    {
        foreach ((int desktopIndex, FloatingWindowForm window) in _desktopWindows)
        {
            if (_interop.IsWindowOnCurrentVirtualDesktop(window.Handle))
            {
                return desktopIndex;
            }
        }

        return null;
    }

    /// <summary>재시도 한도 소진 시 실제로 검증된 위치를 새 기준으로 채택한다 (FR-019, 무한 재시도 금지).</summary>
    private void SelfCorrect(int actualDesktopIndex)
    {
        _session.RecordSwitchCompleted(actualDesktopIndex);
    }

    private void HandleExitConfirmed()
    {
        _timer.Stop();

        foreach (FloatingWindowForm window in _desktopWindows.Values)
        {
            window.SuppressCloseConfirmation = true;
        }

        foreach (FloatingWindowForm window in _desktopWindows.Values)
        {
            if (!window.IsDisposed)
            {
                window.Close();
            }
        }

        ExitRequested?.Invoke();
    }
}
