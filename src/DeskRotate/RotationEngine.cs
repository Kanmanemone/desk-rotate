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

    /// <summary>
    /// 실행 시점 데스크톱이 실제로 몇 번째인지 공식 API로 알 수 없어(FR-034), 뒤로(Previous)
    /// 이 횟수만큼 무조건 이동시켜 실제 데스크톱 1번에 도달한다. 참조 창을 만들어 "이동했는지"를
    /// 조회로 판정하려는 시도는 실사용 환경에서 신뢰할 수 없는 것으로 확인됐다(때로는 실제 이동이
    /// 있었는데도 계속 "이동 안 함"으로, 때로는 반대로 계속 "이동함"으로 오판해 엉뚱한 데스크톱까지
    /// 점프하는 결함으로 이어졌다). 반면 Ctrl+Win+Left는 이미 첫 번째 데스크톱에 있을 때 눌러도
    /// 완전히 안전한 no-op이라는 것은 Windows 표준 동작으로 보장되므로, 판정에 의존하지 않고
    /// 실제 있을 법한 데스크톱 개수보다 넉넉한 횟수만큼 무조건 이동을 시도하는 쪽이 더 신뢰할 수
    /// 있다 — 이미 1번에 도달한 뒤의 나머지 시도는 그냥 아무 일도 일어나지 않는다.
    /// </summary>
    private const int GuaranteedSeekToFirstAttempts = 60;

    /// <summary>
    /// 새로 만든 플로팅 창을 바로 다음 단계의 이동 판정 기준(참조 창)으로 쓰기 전에 주는 안정화
    /// 시간(ms) — 창을 막 Show()한 직후에는 셸의 가상 데스크톱 추적에 아직 완전히 반영되지 않았을
    /// 가능성을 배제할 수 없다(간헐적으로 재발한 오탐성 데스크톱 추가 생성 버그의 유력한 원인 중
    /// 하나로 보고 추가한 방어 지연). 매 데스크톱 방문마다 한 번씩만 드는 비용이라 전체 초기 설정
    /// 시간에 미치는 영향은 크지 않다.
    /// </summary>
    private const int NewMarkerSettleMilliseconds = 400;

    /// <summary>
    /// <see cref="EnsureAdvancedToNextDesktop"/>에서 "다음으로" 키 입력을 다시 보내며 이동 여부를
    /// 재확인하는 최대 횟수. 실사용 중 SendInput 자체가(다른 프로세스의 순간적인 입력 가로채기 등
    /// 이유로) 아예 씹혀 전혀 반영되지 않는 사례가 실제로 확인됐다 — 그 경우 같은 키 입력의 효과를
    /// 아무리 오래 기다려도 이동은 감지되지 않는다(애초에 입력 자체가 전달되지 않았으므로). 그래서
    /// "한 번 보내고 여러 번 확인"이 아니라, 확인이 실패할 때마다 키 입력 자체를 다시 보낸다.
    /// </summary>
    private const int EnsureAdvancedRetryAttempts = 3;

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
    /// 앱 시작 시 사용자 개입 없이, 실제 데스크톱 1번까지 무조건 이동한 뒤(FR-034), 1번부터
    /// 순회 범위 끝까지 순서대로 방문하며 데스크톱별 플로팅 창을 만든다(FR-020) — 이동 여부
    /// 판정에는 매번 새로 만드는 1회용 참조 창 대신, 이미 신뢰성이 검증된 플로팅 창 자체를
    /// 쓴다(실사용 중 발견: 1회용 참조 창은 만들자마자 조회하면 결과가 신뢰할 수 없었다). 범위
    /// 시작보다 앞선 구간(순회 대상이 아닌 데스크톱)에서 만든 임시 창은 마지막에 정리하고,
    /// 범위 시작으로 복귀한다.
    /// </summary>
    public void PerformInitialSetup()
    {
        SeekToActualFirstDesktopBlindly();

        var visitedInOrder = new Dictionary<int, FloatingWindowForm>();

        FloatingWindowForm previous = CreateWindowOnCurrentDesktop(1);
        visitedInOrder[1] = previous;

        for (int desktopIndex = 2; desktopIndex <= _session.RangeEnd; desktopIndex++)
        {
            EnsureAdvancedToNextDesktop(previous);
            FloatingWindowForm current = CreateWindowOnCurrentDesktop(desktopIndex);
            visitedInOrder[desktopIndex] = current;
            previous = current;
        }

        foreach ((int desktopIndex, FloatingWindowForm window) in visitedInOrder)
        {
            if (desktopIndex >= _session.RangeStart)
            {
                _desktopWindows[desktopIndex] = window;
            }
            else
            {
                // 범위 시작 이전(순회 대상 아님)은 지나가는 길에만 필요했던 임시 창이므로 정리한다.
                window.Dispose();
            }
        }

        ReturnToDesktop(_session.RangeStart);
        _session.CurrentDesktopIndex = _session.RangeStart;
    }

    /// <summary>
    /// 뒤로(Previous) 이동을 정해진 횟수만큼 무조건 시도해 실제 데스크톱 1번에 도달한다.
    /// 자세한 이유는 <see cref="GuaranteedSeekToFirstAttempts"/> 참고.
    /// </summary>
    private void SeekToActualFirstDesktopBlindly()
    {
        for (int i = 0; i < GuaranteedSeekToFirstAttempts; i++)
        {
            _keyboard.SendSwitchKeystroke(SwitchDirection.Previous);
            PumpMessagesFor(InterKeystrokeDelayMilliseconds);
        }
    }

    /// <summary>
    /// 다음 데스크톱으로 전환 키를 보내고, <paramref name="referenceOnCurrentDesktop"/>(전환 전
    /// 위치를 나타내는 창)이 여전히 현재 데스크톱에 있는지로 실제 이동 여부를 확인한다. 이동하지
    /// 않았다면(그 데스크톱이 아직 없다면) 표준 "새 데스크톱 추가" 단축키로 새 데스크톱을 만들어
    /// 그쪽으로 전환한다(FR-033).
    /// </summary>
    private void EnsureAdvancedToNextDesktop(FloatingWindowForm referenceOnCurrentDesktop)
    {
        IntPtr referenceHandle = referenceOnCurrentDesktop.Handle;

        // "다음으로" 키를 한 번만 보내고 그 결과를 여러 번 폴링하는 방식은, 만약 그 한 번의 키
        // 입력 자체가(다른 프로세스가 순간적으로 입력을 가로채는 등 이유로) 전혀 반영되지 않았을
        // 경우 아무리 오래 기다려도 소용이 없다 — 애초에 이동이 시작된 적이 없기 때문이다. 그래서
        // 확인에 실패할 때마다 키 입력 자체를 다시 보낸다. 이동이 확인되는 즉시 반환하므로, 이미
        // 존재하는 데스크톱으로 정상 전환된 경우 추가 키 입력을 보낼 일은 없다(EnsureAdvancedRetry
        // Attempts번 모두 실패해야만 아래에서 새로 생성한다).
        for (int attempt = 1; attempt <= EnsureAdvancedRetryAttempts; attempt++)
        {
            _keyboard.SendSwitchKeystroke(SwitchDirection.Next);
            if (WaitForMovementAway(referenceHandle))
            {
                return;
            }
        }

        // 키 입력을 다시 보내며 여러 차례 재확인해도 참조 창이 계속 현재 데스크톱에 있다 — 전환이
        // 일어나지 않았으므로 그 순번의 데스크톱이 아직 없다는 뜻이다. 새로 만들며 그쪽으로 전환한다.
        //
        // 주의: Win+Ctrl+D는 지금 위치가 어디든 항상 전체 데스크톱 목록의 "맨 끝"에 새 데스크톱을
        // 추가한다 — "바로 다음 자리"에 끼워 넣지 않는다. 그래서 이동 여부 판정을 오판하면(예: 실제로는
        // 이동했는데 아직 안 했다고 잘못 판단) 옆 데스크톱이 아니라 이미 존재하던 다른 데스크톱들 끝
        // (예: 19번)까지 엉뚱하게 새로 만들며 튀어버리는 결함으로 이어진다 — 오판 하나가 곧바로 큰
        // 점프가 되므로 이동 감지 자체를 신뢰할 수 있어야 한다.
        _keyboard.SendCreateDesktopKeystroke();
        PumpMessagesFor(InterKeystrokeDelayMilliseconds);
    }

    /// <summary>
    /// 전환 키 입력 직후 <paramref name="referenceHandle"/>가 현재 데스크톱에서 벗어났는지(=이동
    /// 했는지) 넉넉한 시간 동안 짧은 간격으로 반복 조회한다.
    /// </summary>
    private bool WaitForMovementAway(IntPtr referenceHandle)
    {
        // 2초에서 3초로 늘렸다 — 세 번 연속 확인(EnsureAdvancedToNextDesktop 참고)과 함께,
        // 시스템 부하로 전환 애니메이션이 예상보다 오래 걸리는 경우를 더 잘 견디기 위함이다.
        const int timeoutMilliseconds = 3000;
        const int pollIntervalMilliseconds = 100;

        int elapsed = 0;
        while (elapsed < timeoutMilliseconds)
        {
            PumpMessagesFor(pollIntervalMilliseconds);
            elapsed += pollIntervalMilliseconds;

            if (!_interop.IsWindowOnCurrentVirtualDesktop(referenceHandle))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <paramref name="milliseconds"/> 동안 Windows 메시지를 계속 처리하며 대기한다. `Thread.Sleep`
    /// 대신 이렇게 메시지를 직접 퍼 올려야, 이 시점(아직 `Application.Run()`의 메인 메시지 루프가
    /// 시작되기 전)에 만든 창도 정상적으로 처리된다.
    /// </summary>
    private static void PumpMessagesFor(int milliseconds)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
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
        window.PauseToggleRequested += HandlePauseToggleRequested;
        window.PlaceAtTopCenter();
        window.Show();
        // 이 창이 바로 다음 데스크톱의 이동 판정 기준으로 쓰이기 전에 잠깐 안정화 시간을 준다
        // (NewMarkerSettleMilliseconds 참고).
        PumpMessagesFor(NewMarkerSettleMilliseconds);
        return window;
    }

    /// <summary>
    /// 어느 플로팅 창의 상세 보기에서든 일시정지 버튼을 누르면 호출된다 (FR-035) — 세션은
    /// 하나뿐이므로 즉시 모든 창에 반영해, 다른 데스크톱의 창을 봐도 같은 일시정지 상태로 보인다.
    /// </summary>
    private void HandlePauseToggleRequested()
    {
        _session.TogglePause();
        RefreshAllWindows();
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
                PumpMessagesFor(InterKeystrokeDelayMilliseconds);
            }
        }
    }

    private async Task OnTickAsync()
    {
        _session.Tick();
        RefreshAllWindows();

        // 전환 시퀀스가 이미 진행 중이면(재시도 등으로 300ms 이상 걸리는 중) 다시 겹쳐 시작하지
        // 않는다 — 매초 화면 갱신 자체는 위에서 이미 계속 이루어지므로 창이 멈춰 보이지 않는다.
        // 일시정지 중에는(FR-035) 진행 중이던 시퀀스는 끝까지 마치되 새 시도는 시작하지 않는다.
        if (_switchInProgress || _session.TargetReached || _session.IsPaused || _session.RemainingSecondsToNextSwitch > 0)
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
