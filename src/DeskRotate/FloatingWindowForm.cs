namespace DeskRotate;

/// <summary>
/// 데스크톱별 플로팅 창 (contracts/floating-window-contract.md).
/// 테두리 없는(borderless) 항상-위 창이며, 기본 상태(최소 보기)에서는 다음 전환까지 남은 시간
/// 숫자만 표시한다. 클릭하면 상세 보기(총 예상 실행 시간·종료까지 남은 시간·데스크톱별 전환 횟수)로
/// 전환된다. 동시에 공식 IsWindowOnCurrentVirtualDesktop 조회의 대상(HWND)이 되어 전환 검증에도 쓰인다.
/// </summary>
public sealed class FloatingWindowForm : Form
{
    private enum ViewMode
    {
        Minimal,
        Detailed,
    }

    private const int ClickDragThresholdPixels = 5;

    /// <summary>드래그 중 창이 화면 작업 영역 테두리에서 이 거리(px) 이내로 들어오면 달라붙는다 (FR-032).</summary>
    private const int EdgeSnapThresholdPixels = 15;

    private static readonly Size MinimalSize = new(100, 70);
    // 상세 보기 라벨 Height/Width는 실제 실행 화면에서 쓰는 글꼴로 TextRenderer.MeasureText를
    // 직접 측정해 정한 값이다 — 이전에는 Height=20/18로 잡혀 있어 텍스트 아래쪽 몇 픽셀이 잘려
    // 보이고, "종료까지 남은 시간"처럼 초 단위 숫자가 커지면 폭도 부족해 오른쪽이 잘리는 문제가
    // 있었다(라벨 잘림 버그). 6자리 초(최대 999999초)까지 잘리지 않도록 여유를 뒀다.
    private static readonly Size DetailedSize = new(280, 312);

    private readonly int _desktopIndex;
    private ViewMode _viewMode = ViewMode.Minimal;

    private readonly Label _minimalCountdownLabel;
    private readonly Label _nextSwitchLabel;
    private readonly Label _remainingToFinishLabel;
    private readonly Label _totalPlannedRuntimeLabel;
    private readonly Label _cycleProgressLabel;
    private readonly Label _countsHeaderLabel;
    private readonly ListBox _perDesktopCountsList;
    private readonly Button _closeButton;
    private readonly Button _pauseButton;

    private bool _mouseDown;
    private bool _dragged;
    private Point _dragStartMouseScreenPoint;
    private Point _dragStartWindowLocation;

    /// <summary>사용자가 종료 확인 다이얼로그에서 "예"를 선택했을 때 발생한다 (FR-008, FR-009).</summary>
    public event Action? ExitConfirmed;

    /// <summary>사용자가 상세 보기의 일시정지/재개 버튼을 눌렀을 때 발생한다 (FR-035).</summary>
    public event Action? PauseToggleRequested;

    /// <summary>
    /// true면 닫힐 때 확인 다이얼로그를 건너뛴다 — 다른 창에서 이미 종료가 확정되어
    /// 나머지 창들을 함께 정리할 때만 엔진이 이 값을 true로 설정한다 (FR-009).
    /// </summary>
    public bool SuppressCloseConfirmation { get; set; }

    public int DesktopIndex => _desktopIndex;

    public FloatingWindowForm(int desktopIndex)
    {
        _desktopIndex = desktopIndex;

        // 고정 픽셀 좌표로 배치하므로 WinForms의 DPI 자동 스케일링을 끈다 (StartupInputForm과 동일한 이유).
        AutoScaleMode = AutoScaleMode.None;

        Text = $"desk-rotate — 데스크톱 {desktopIndex}";
        // 테두리 없는 창 — 제목 표시줄도 없으므로 기본 X 버튼이 없다. 닫기는 Alt+F4나 엔진의
        // 전체 종료 경로로만 트리거되며, 어느 쪽이든 OnFormClosing에서 확인 절차를 거친다.
        // WS_EX_TOOLWINDOW를 유발하는 ToolWindow 계열 스타일과 달리 FormBorderStyle.None은
        // Alt+Tab·작업표시줄 노출에 영향을 주지 않는다 (FR-004).
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = MinimalSize;

        _minimalCountdownLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(FontFamily.GenericSansSerif, 22F, FontStyle.Bold),
        };

        _nextSwitchLabel = new Label { Left = 10, Top = 36, Width = 260, Height = 26 };
        _remainingToFinishLabel = new Label { Left = 10, Top = 64, Width = 260, Height = 26 };
        _totalPlannedRuntimeLabel = new Label { Left = 10, Top = 92, Width = 260, Height = 26 };
        _cycleProgressLabel = new Label { Left = 10, Top = 120, Width = 260, Height = 26 };
        _countsHeaderLabel = new Label { Left = 10, Top = 148, Width = 260, Height = 26, Text = "데스크톱별 전환 횟수:" };
        _perDesktopCountsList = new ListBox { Left = 10, Top = 176, Width = 260, Height = 126 };

        // 상세 보기 전용 커스텀 닫기 버튼 (FR-029) — 테두리 없는 창이라 기본 X 버튼이 없어,
        // 클릭하면 다른 닫기 경로와 동일하게 OnFormClosing의 종료 확인 절차로 이어진다.
        _closeButton = new Button { Left = 244, Top = 6, Width = 26, Height = 26, Text = "×" };
        _closeButton.Click += (_, _) => Close();

        // 일시정지/재개 버튼 (FR-035) — 상세 보기를 클릭해야만 보이며, 세션은 하나뿐이므로 눌렀을 때
        // RotationEngine이 모든 데스크톱의 창에 동일하게 반영한다.
        _pauseButton = new Button { Left = 10, Top = 6, Width = 90, Height = 26, Text = "일시정지" };
        _pauseButton.Click += (_, _) => PauseToggleRequested?.Invoke();

        Controls.Add(_minimalCountdownLabel);
        Controls.AddRange(new Control[]
        {
            _nextSwitchLabel, _remainingToFinishLabel, _totalPlannedRuntimeLabel, _cycleProgressLabel,
            _countsHeaderLabel, _perDesktopCountsList, _closeButton, _pauseButton,
        });

        // 창 본문(최소 보기 숫자, 상세 보기 라벨들, 폼 배경) 어디를 눌러도 클릭/드래그를 인식한다.
        WireDragAndClickHandlers(this);
        WireDragAndClickHandlers(_minimalCountdownLabel);
        WireDragAndClickHandlers(_nextSwitchLabel);
        WireDragAndClickHandlers(_remainingToFinishLabel);
        WireDragAndClickHandlers(_totalPlannedRuntimeLabel);
        WireDragAndClickHandlers(_cycleProgressLabel);
        WireDragAndClickHandlers(_countsHeaderLabel);

        ApplyViewMode();

        FormClosing += OnFormClosing;
    }

    private void WireDragAndClickHandlers(Control control)
    {
        control.MouseDown += OnMouseDown;
        control.MouseMove += OnMouseMove;
        control.MouseUp += OnMouseUp;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _mouseDown = true;
        _dragged = false;
        _dragStartMouseScreenPoint = Cursor.Position;
        _dragStartWindowLocation = Location;
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_mouseDown)
        {
            return;
        }

        Point current = Cursor.Position;
        int dx = current.X - _dragStartMouseScreenPoint.X;
        int dy = current.Y - _dragStartMouseScreenPoint.Y;

        // FR-025: 일정 거리 이상 움직여야 드래그로 간주한다 — 그 전까지는 클릭 취소 가능성을 열어 둔다.
        if (!_dragged && (Math.Abs(dx) > ClickDragThresholdPixels || Math.Abs(dy) > ClickDragThresholdPixels))
        {
            _dragged = true;
        }

        if (_dragged)
        {
            Location = SnapToEdgesIfNear(new Point(_dragStartWindowLocation.X + dx, _dragStartWindowLocation.Y + dy));
        }
    }

    /// <summary>
    /// 창이 소속 화면의 작업 영역 테두리에서 <see cref="EdgeSnapThresholdPixels"/> 이내로 들어오면
    /// 해당 축을 테두리에 맞춰 고정한다(FR-032). 벗어나면 그대로 자유롭게 이동한다 — 과도한 스냅을
    /// 피하기 위해 임계 거리를 "거의 닿을 수준"으로 작게 잡는다.
    /// </summary>
    private Point SnapToEdgesIfNear(Point candidate)
    {
        Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        int x = candidate.X;
        int y = candidate.Y;

        if (Math.Abs(x - workingArea.Left) <= EdgeSnapThresholdPixels)
        {
            x = workingArea.Left;
        }
        else if (Math.Abs((x + Width) - workingArea.Right) <= EdgeSnapThresholdPixels)
        {
            x = workingArea.Right - Width;
        }

        if (Math.Abs(y - workingArea.Top) <= EdgeSnapThresholdPixels)
        {
            y = workingArea.Top;
        }
        else if (Math.Abs((y + Height) - workingArea.Bottom) <= EdgeSnapThresholdPixels)
        {
            y = workingArea.Bottom - Height;
        }

        return new Point(x, y);
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_mouseDown && !_dragged)
        {
            // 이동 없이 누르고 뗐다 — 드래그가 아니라 클릭이므로 보기를 전환한다 (FR-024, FR-025).
            ToggleViewMode();
        }

        _mouseDown = false;
        _dragged = false;
    }

    private void ToggleViewMode()
    {
        _viewMode = _viewMode == ViewMode.Minimal ? ViewMode.Detailed : ViewMode.Minimal;
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        bool detailed = _viewMode == ViewMode.Detailed;

        _minimalCountdownLabel.Visible = !detailed;
        _nextSwitchLabel.Visible = detailed;
        _remainingToFinishLabel.Visible = detailed;
        _totalPlannedRuntimeLabel.Visible = detailed;
        _cycleProgressLabel.Visible = detailed;
        _countsHeaderLabel.Visible = detailed;
        _perDesktopCountsList.Visible = detailed;
        _closeButton.Visible = detailed;
        _pauseButton.Visible = detailed;

        ClientSize = detailed ? DetailedSize : MinimalSize;
    }

    /// <summary>
    /// 최소 보기 텍스트가 사이클 번호 접두어(FR-031)로 길어질 수 있어, 고정 크기로는 큰 글씨가
    /// 잘려 보이는 문제가 실제 실행 화면에서 발견됐다. 실제 텍스트 폭을 측정해 필요한 만큼만
    /// 창을 넓히고(최소 크기는 MinimalSize 유지), 가로 중심은 유지한 채 좌우로 균등하게 늘린다.
    /// </summary>
    private void ResizeMinimalWindowToFitText()
    {
        Size measured = TextRenderer.MeasureText(_minimalCountdownLabel.Text, _minimalCountdownLabel.Font);
        int desiredWidth = Math.Max(MinimalSize.Width, measured.Width + 24);

        if (ClientSize.Width != desiredWidth || ClientSize.Height != MinimalSize.Height)
        {
            int centerX = Location.X + ClientSize.Width / 2;
            ClientSize = new Size(desiredWidth, MinimalSize.Height);
            Location = new Point(centerX - ClientSize.Width / 2, Location.Y);
        }
    }

    /// <summary>초기 배치: 화면 상단 중앙(12시 방향) (FR-021). 이후 사용자가 드래그하면 세션 동안 그 위치가 유지된다.</summary>
    public void PlaceAtTopCenter()
    {
        Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int x = workingArea.Left + (workingArea.Width - Width) / 2;
        int y = workingArea.Top;
        Location = new Point(x, y);
    }

    /// <summary>매초 RotationEngine의 타이머 틱에서 호출되어 표시 내용을 최신 상태로 갱신한다 (FR-005, FR-006, FR-014, FR-023, FR-030, FR-031).</summary>
    public void RefreshDisplay(RotationSession session)
    {
        _minimalCountdownLabel.Text = FormatMinimalText(session);

        if (_viewMode == ViewMode.Minimal)
        {
            ResizeMinimalWindowToFitText();
        }

        _nextSwitchLabel.Text = session.TargetReached
            ? "전환 완료 — 최종 통계"
            : session.IsPaused
                ? "일시정지 중"
                : $"다음 전환까지: {session.RemainingSecondsToNextSwitch}초";

        _remainingToFinishLabel.Text = session.TargetReached
            ? "종료까지 남은 시간: 0초"
            : $"종료까지 남은 시간: {session.RemainingSecondsToFinish}초";

        _totalPlannedRuntimeLabel.Text = $"총 예상 실행 시간: {session.TotalPlannedRuntimeSeconds}초";
        _cycleProgressLabel.Text = $"사이클: {session.CurrentCycleNumber} / {session.TargetCycleCount}";

        // 목표 도달 후에는 일시정지 개념이 의미 없으므로 버튼을 비활성화한다 — 켜둬도 TogglePause
        // 자체는 안전하지만(다음 전환이 없으므로 아무 효과 없음), 눌러도 아무 일도 없어 보이는
        // 혼란을 막기 위해 아예 못 누르게 막는다.
        _pauseButton.Text = session.IsPaused ? "재개" : "일시정지";
        _pauseButton.Enabled = !session.TargetReached;

        int topIndex = _perDesktopCountsList.TopIndex;
        _perDesktopCountsList.BeginUpdate();
        _perDesktopCountsList.Items.Clear();
        foreach (var pair in session.PerDesktopSwitchCounts.OrderBy(kv => kv.Key))
        {
            _perDesktopCountsList.Items.Add($"데스크톱 {pair.Key}: {pair.Value}회");
        }

        _perDesktopCountsList.EndUpdate();
        if (topIndex >= 0 && topIndex < _perDesktopCountsList.Items.Count)
        {
            _perDesktopCountsList.TopIndex = topIndex;
        }
    }

    /// <summary>
    /// 최소 보기 숫자를 표시 옵션에 따라 조합한다 (FR-031) — 사이클 번호 표시가 켜지면 앞에
    /// "[N번째] "를, 초 단위 표시가 켜지면 뒤에 "초"를 붙인다(예: "[2번째] 137초").
    /// </summary>
    private static string FormatMinimalText(RotationSession session)
    {
        if (session.TargetReached)
        {
            return "완료";
        }

        if (session.IsPaused)
        {
            return "일시정지";
        }

        string number = session.RemainingSecondsToNextSwitch.ToString();
        string withUnit = session.ShowSecondsUnit ? number + "초" : number;
        return session.ShowCycleNumber ? $"[{session.CurrentCycleNumber}번째] {withUnit}" : withUnit;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (SuppressCloseConfirmation)
        {
            return;
        }

        DialogResult result = MessageBox.Show(
            this,
            "정말 종료할까요?",
            "desk-rotate 종료",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        ExitConfirmed?.Invoke();
    }
}
