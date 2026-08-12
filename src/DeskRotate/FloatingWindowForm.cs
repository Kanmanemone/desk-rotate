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

    private static readonly Size MinimalSize = new(100, 70);
    private static readonly Size DetailedSize = new(240, 240);

    private readonly int _desktopIndex;
    private ViewMode _viewMode = ViewMode.Minimal;

    private readonly Label _minimalCountdownLabel;
    private readonly Label _nextSwitchLabel;
    private readonly Label _remainingToFinishLabel;
    private readonly Label _totalPlannedRuntimeLabel;
    private readonly Label _countsHeaderLabel;
    private readonly ListBox _perDesktopCountsList;

    private bool _mouseDown;
    private bool _dragged;
    private Point _dragStartMouseScreenPoint;
    private Point _dragStartWindowLocation;

    /// <summary>사용자가 종료 확인 다이얼로그에서 "예"를 선택했을 때 발생한다 (FR-008, FR-009).</summary>
    public event Action? ExitConfirmed;

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

        _nextSwitchLabel = new Label { Left = 10, Top = 10, Width = 220, Height = 20 };
        _remainingToFinishLabel = new Label { Left = 10, Top = 32, Width = 220, Height = 20 };
        _totalPlannedRuntimeLabel = new Label { Left = 10, Top = 54, Width = 220, Height = 20 };
        _countsHeaderLabel = new Label { Left = 10, Top = 78, Width = 220, Height = 18, Text = "데스크톱별 전환 횟수:" };
        _perDesktopCountsList = new ListBox { Left = 10, Top = 98, Width = 220, Height = 130 };

        Controls.Add(_minimalCountdownLabel);
        Controls.AddRange(new Control[]
        {
            _nextSwitchLabel, _remainingToFinishLabel, _totalPlannedRuntimeLabel,
            _countsHeaderLabel, _perDesktopCountsList,
        });

        // 창 본문(최소 보기 숫자, 상세 보기 라벨들, 폼 배경) 어디를 눌러도 클릭/드래그를 인식한다.
        WireDragAndClickHandlers(this);
        WireDragAndClickHandlers(_minimalCountdownLabel);
        WireDragAndClickHandlers(_nextSwitchLabel);
        WireDragAndClickHandlers(_remainingToFinishLabel);
        WireDragAndClickHandlers(_totalPlannedRuntimeLabel);
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
            Location = new Point(_dragStartWindowLocation.X + dx, _dragStartWindowLocation.Y + dy);
        }
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
        _countsHeaderLabel.Visible = detailed;
        _perDesktopCountsList.Visible = detailed;

        ClientSize = detailed ? DetailedSize : MinimalSize;
    }

    /// <summary>초기 배치: 화면 상단 중앙(12시 방향) (FR-021). 이후 사용자가 드래그하면 세션 동안 그 위치가 유지된다.</summary>
    public void PlaceAtTopCenter()
    {
        Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int x = workingArea.Left + (workingArea.Width - Width) / 2;
        int y = workingArea.Top;
        Location = new Point(x, y);
    }

    /// <summary>매초 RotationEngine의 타이머 틱에서 호출되어 표시 내용을 최신 상태로 갱신한다 (FR-005, FR-006, FR-014, FR-023).</summary>
    public void RefreshDisplay(RotationSession session)
    {
        _minimalCountdownLabel.Text = session.TargetReached
            ? "완료"
            : session.RemainingSecondsToNextSwitch.ToString();

        _nextSwitchLabel.Text = session.TargetReached
            ? "전환 완료 — 최종 통계"
            : $"다음 전환까지: {session.RemainingSecondsToNextSwitch}초";

        _remainingToFinishLabel.Text = session.TargetReached
            ? "종료까지 남은 시간: 0초"
            : $"종료까지 남은 시간: {session.RemainingSecondsToFinish}초";

        _totalPlannedRuntimeLabel.Text = $"총 예상 실행 시간: {session.TotalPlannedRuntimeSeconds}초";

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
