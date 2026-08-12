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

    /// <summary>
    /// 전환 시퀀스(키 입력·검증·재시도)가 진행 중인 동안 true — 같은 시퀀스가 겹쳐 실행되는 것을
    /// 막는다. 이 값이 true인 동안에도 매초 틱은 계속 돌며 화면을 갱신한다(더 이상 멈추지 않음).
    /// </summary>
    private bool _switchInProgress;

    /// <summary>사용자가 어느 플로팅 창에서든 종료를 확정했을 때 발생한다 (FR-009).</summary>
    public event Action? ExitRequested;

    public RotationEngine(RotationSession session, VirtualDesktopInterop interop, KeyboardSimulator keyboard)
    {
        _session = session;
        _interop = interop;
        _keyboard = keyboard;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += async (_, _) => await OnTickAsync();
    }

    /// <summary>
    /// 앱 시작 시 사용자 개입 없이, 실행 시점의 데스크톱이 실제로 몇 번째인지 스스로 판별한 뒤
    /// (FR-034) 순회 범위의 시작까지 자동으로 이동하고(초기 탐색, FR-022), 범위 안 각 데스크톱을
    /// 한 번씩 순회하며 플로팅 창을 생성·배치하고(FR-020), 범위 시작으로 복귀한다.
    /// 매 전환 단계마다 실제로 다른 데스크톱으로 이동했는지 공식 API로 확인하고, 그 데스크톱이
    /// 아직 없어 이동하지 않았다면 새 데스크톱을 생성해 채운다(FR-033) — 그렇지 않으면 서로 다른
    /// 범위 번호의 창들이 같은 실제 데스크톱 위에 겹쳐 생성되어 이후 전환 검증이 항상 "일치"로
    /// 오판하는 결함으로 이어진다.
    /// </summary>
    public void PerformInitialSetup()
    {
        SeekToActualFirstDesktop();

        int seekSteps = _session.RangeStart - 1;
        if (seekSteps > 0)
        {
            // 아직 범위 시작에 도달하기 전(=순회 대상이 아닌 데스크톱들을 지나가는 구간)이므로,
            // 단계마다 "지금 어디에 있는지"를 나타내는 1회용 참조 창을 새로 만들어 다음 단계의
            // 이동 여부 확인에 쓰고 바로 버린다.
            Form reference = CreateDesktopProbe();
            for (int i = 0; i < seekSteps; i++)
            {
                EnsureAdvancedToNextDesktop(reference);
                reference.Dispose();
                reference = CreateDesktopProbe();
            }

            reference.Dispose();
        }

        _desktopWindows[_session.RangeStart] = CreateWindowOnCurrentDesktop(_session.RangeStart);

        for (int desktopIndex = _session.RangeStart + 1; desktopIndex <= _session.RangeEnd; desktopIndex++)
        {
            EnsureAdvancedToNextDesktop(_desktopWindows[desktopIndex - 1]);
            _desktopWindows[desktopIndex] = CreateWindowOnCurrentDesktop(desktopIndex);
        }

        ReturnToDesktop(_session.RangeStart);
        _session.CurrentDesktopIndex = _session.RangeStart;
    }

    /// <summary>
    /// 공식 API는 "지금 몇 번째 데스크톱에 있는지" 알려주지 않으므로, 실행 시점을 무조건 절대
    /// 1번으로 가정하면 이미 다른 데스크톱들 사이(예: 4개 중 2번째)에서 실행했을 때 범위 시작까지의
    /// 이동 칸 수 계산이 틀어져 FR-033의 판단 기준 자체가 잘못되고, 그 결과 불필요한 데스크톱을
    /// 대량으로 새로 만들어버리는 심각한 결함으로 이어진다(실사용 중 발견). 이를 막기 위해 뒤로
    /// (Previous) 계속 이동을 시도하며 매번 실제 이동 여부를 확인하고, 더 이상 이동하지 않는
    /// 지점(=실제 데스크톱 1번)에 도달할 때까지 반복한다(FR-034).
    /// </summary>
    private void SeekToActualFirstDesktop()
    {
        Form reference = CreateDesktopProbe();
        while (true)
        {
            _keyboard.SendSwitchKeystroke(SwitchDirection.Previous);
            Thread.Sleep(InterKeystrokeDelayMilliseconds);

            bool moved = !_interop.IsWindowOnCurrentVirtualDesktop(reference.Handle);
            reference.Dispose();

            if (!moved)
            {
                break;
            }

            reference = CreateDesktopProbe();
        }
    }

    /// <summary>
    /// 다음 데스크톱으로 전환 키를 보내고, <paramref name="referenceOnCurrentDesktop"/>(전환 전
    /// 위치를 나타내는 창)이 여전히 현재 데스크톱에 있는지로 실제 이동 여부를 확인한다. 이동하지
    /// 않았다면(그 데스크톱이 아직 없다면) 표준 "새 데스크톱 추가" 단축키로 새 데스크톱을 만들어
    /// 그쪽으로 전환한다(FR-033).
    /// </summary>
    private void EnsureAdvancedToNextDesktop(FloatingWindowForm referenceOnCurrentDesktop)
        => EnsureAdvancedToNextDesktop(referenceOnCurrentDesktop.Handle);

    private void EnsureAdvancedToNextDesktop(Form referenceOnCurrentDesktop)
        => EnsureAdvancedToNextDesktop(referenceOnCurrentDesktop.Handle);

    private void EnsureAdvancedToNextDesktop(IntPtr referenceHandleOnCurrentDesktop)
    {
        _keyboard.SendSwitchKeystroke(SwitchDirection.Next);
        Thread.Sleep(InterKeystrokeDelayMilliseconds);

        if (_interop.IsWindowOnCurrentVirtualDesktop(referenceHandleOnCurrentDesktop))
        {
            // 참조 창이 여전히 현재 데스크톱에 있다 — 전환이 일어나지 않았으므로 그 순번의
            // 데스크톱이 아직 없다는 뜻이다. 새로 만들며 그쪽으로 전환한다.
            _keyboard.SendCreateDesktopKeystroke();
            Thread.Sleep(InterKeystrokeDelayMilliseconds);
        }
    }

    /// <summary>
    /// 절대 위치 판별(FR-034)과 초기 탐색 중(아직 범위 안 데스크톱이 아닌 구간)에만 쓰는, 화면에
    /// 보이지 않는 1회용 참조 창 — 사용자에게 노출되는 실제 마커 창(FloatingWindowForm)과 달리
    /// 검증 전용이며 쓰고 나면 곧바로 버려진다.
    /// </summary>
    private static Form CreateDesktopProbe()
    {
        var probe = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1),
            Opacity = 0,
        };
        probe.Show();
        return probe;
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

    private async Task OnTickAsync()
    {
        _session.Tick();
        RefreshAllWindows();

        // 전환 시퀀스가 이미 진행 중이면(재시도 등으로 300ms 이상 걸리는 중) 다시 겹쳐 시작하지
        // 않는다 — 매초 화면 갱신 자체는 위에서 이미 계속 이루어지므로 창이 멈춰 보이지 않는다.
        if (_switchInProgress || _session.TargetReached || _session.RemainingSecondsToNextSwitch > 0)
        {
            return;
        }

        _switchInProgress = true;
        try
        {
            await AttemptSwitchAsync();
        }
        finally
        {
            _switchInProgress = false;
        }

        RefreshAllWindows();
    }

    private void RefreshAllWindows()
    {
        foreach (FloatingWindowForm window in _desktopWindows.Values)
        {
            window.RefreshDisplay(_session);
        }
    }

    /// <summary>
    /// 범위 안 다음 데스크톱으로 전환을 시도한다 (FR-001, FR-002). 범위 끝에서 시작으로 되돌아가는
    /// 경우에만 여러 번의 키 입력이 필요하며, 그 사이와 검증 직전에 지연을 둔다 (FR-016). UI 스레드를
    /// 막지 않도록 Thread.Sleep 대신 Task.Delay를 사용한다 — 블로킹 방식은 재시도가 걸리는 동안 다른
    /// 창의 화면 갱신까지 멈춰 "타이머가 멈춘 것처럼 보이는" 문제를 일으켰다.
    /// </summary>
    private async Task AttemptSwitchAsync()
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
                await Task.Delay(InterKeystrokeDelayMilliseconds);
            }
        }
        else
        {
            _keyboard.SendSwitchKeystroke(SwitchDirection.Next);
            await Task.Delay(InterKeystrokeDelayMilliseconds);
        }

        // 마지막 키 입력 후 위에서 이미 지연을 두었으므로, 전환 애니메이션이 끝난 뒤에 검증한다.
        // 지연 없이 바로 검증하면 애니메이션이 아직 끝나지 않아 "불일치"로 오판해 불필요한
        // 재시도 키 입력을 추가로 보내는 버그가 있었다(예: 범위 1~3에서 3→1로 2번만 이동하면
        // 되는데 검증 오판으로 3번째 키 입력이 나가던 문제).
        await VerifyAndSettleAsync(intendedTarget, direction);
    }

    /// <summary>
    /// 전환 시도 직후 공식 API로 검증하고(FR-017), 어긋나면 재시도하며(FR-018),
    /// 재시도 한도를 소진하면 실제 위치로 자가 보정한다(FR-019).
    /// </summary>
    private async Task VerifyAndSettleAsync(int intendedTarget, SwitchDirection retryDirection)
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
                    _keyboard.SendSwitchKeystroke(retryDirection);
                    await Task.Delay(InterKeystrokeDelayMilliseconds);
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
