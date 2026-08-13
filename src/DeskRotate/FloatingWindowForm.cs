using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

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

    // Windows/DWM은 다른 창이 topmost를 선점하거나 가상 데스크톱을 전환하는 과정 등에서 이 창의
    // topmost 순위를 조용히 박탈할 수 있다 — WinForms의 TopMost 속성은 생성 시(또는 false→true
    // 전환 시)에만 SetWindowPos를 호출하고 이후 재확인하지 않으므로, 한 번 박탈되면 다른 창 뒤에
    // 숨겨진 채로 복구되지 않는 결함으로 이어진다(FR-004 "숨겨지지 않는다" 위반). 이를 막기 위해
    // 이미 매초 도는 RefreshDisplay에서 SetWindowPos(HWND_TOPMOST)를 직접 재호출해 자가 복구한다.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    private const int ClickDragThresholdPixels = 5;

    /// <summary>드래그 중 창이 화면 작업 영역 테두리에서 이 거리(px) 이내로 들어오면 달라붙는다 (FR-032).</summary>
    private const int EdgeSnapThresholdPixels = 15;

    private static readonly Size MinimalSize = new(100, 70);
    // 상세 보기 라벨 Height/Width는 실제 실행 화면에서 쓰는 글꼴로 TextRenderer.MeasureText를
    // 직접 측정해 정한 값이다 — 이전에는 Height=20/18로 잡혀 있어 텍스트 아래쪽 몇 픽셀이 잘려
    // 보이고, "종료까지 남은 시간"처럼 초 단위 숫자가 커지면 폭도 부족해 오른쪽이 잘리는 문제가
    // 있었다(라벨 잘림 버그). 6자리 초(최대 999999초)까지 잘리지 않도록 여유를 뒀다.
    private static readonly Size DetailedSize = new(280, 312);

    /// <summary>최소 보기 숫자 오른쪽 원형 진행률 그래픽(FR-036)의 지름(px).</summary>
    private const int ProgressRingDiameter = 32;

    /// <summary>숫자와 원형 그래픽 사이 간격(px).</summary>
    private const int ProgressRingGapPixels = 8;

    private readonly int _desktopIndex;
    private ViewMode _viewMode = ViewMode.Minimal;

    /// <summary>가장 최근 RefreshDisplay에서 받은 세션의 "진행률 원 표시" 옵션값(FR-036) — ApplyViewMode의
    /// 가시성 계산에도 쓰이므로 필드로 보관한다. 옵션이 세션 도중 바뀌지 않으므로(생성 시 고정) 값은
    /// 사실상 상수지만, 아직 첫 RefreshDisplay가 오지 않은 생성 시점의 기본값은 FR-036의 기본값(켜짐)과
    /// 맞춰 true로 둔다.
    private bool _showProgressRing = true;

    /// <summary>가장 최근 RefreshDisplay에서 받은 세션의 목표 도달 여부 — 목표에 도달하면(FR-015,
    /// "완료") 더 이상 다음 전환까지 남은 시간이라는 개념이 없으므로 원형 그래픽을 숨긴다(사용자
    /// 피드백: 완료 후에도 마지막 채움 상태로 얼어붙어 있는 것이 오해를 줌).</summary>
    private bool _targetReached;

    private readonly Label _minimalCountdownLabel;
    private readonly ProgressRingControl _progressRingControl;
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

        // Dock.Fill 대신 명시적 위치를 쓴다 — 오른쪽에 원형 진행률 그래픽(FR-036)이 나란히 들어가야
        // 하므로, 숫자 라벨이 최소 보기 폭 전체를 독차지할 수 없다. 실제 위치·폭은 텍스트 길이와
        // "진행률 원 표시" 옵션 여부에 따라 ResizeMinimalWindowToFitText가 매초 다시 계산한다.
        _minimalCountdownLabel = new Label
        {
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(FontFamily.GenericSansSerif, 22F, FontStyle.Bold),
            Height = MinimalSize.Height,
        };

        _progressRingControl = new ProgressRingControl
        {
            Width = ProgressRingDiameter,
            Height = ProgressRingDiameter,
        };

        _nextSwitchLabel = new Label { Left = 10, Top = 36, Width = 260, Height = 26 };
        _remainingToFinishLabel = new Label { Left = 10, Top = 64, Width = 260, Height = 26 };
        _totalPlannedRuntimeLabel = new Label { Left = 10, Top = 92, Width = 260, Height = 26 };
        _cycleProgressLabel = new Label { Left = 10, Top = 120, Width = 260, Height = 26 };
        _countsHeaderLabel = new Label { Left = 10, Top = 148, Width = 260, Height = 26, Text = "데스크톱별 전환 횟수:" };
        _perDesktopCountsList = new ListBox { Left = 10, Top = 176, Width = 260, Height = 126 };

        // 상세 보기 전용 커스텀 닫기 버튼 (FR-029) — 테두리 없는 창이라 기본 X 버튼이 없어,
        // 클릭하면 다른 닫기 경로와 동일하게 OnFormClosing의 종료 확인 절차로 이어진다.
        // 고정 Width(26px)로는 "×" 글자가 실제 실행 화면(DPI 배율에 따라)에서 살짝 잘려 보이는
        // 문제가 보고됐다 — AutoSize로 바꿔 실제 렌더링 시점의 글꼴·DPI 기준으로 크기를 계산한다.
        // 주의: AutoSize=true를 켜는 것만으로는 Width가 즉시 갱신되지 않는다 — WinForms는 컨트롤이
        // 부모에 Add된 뒤에야 실제 크기를 계산해 반영한다. 부모에 붙기 전에 오른쪽 정렬용 Left를
        // 계산해야 하므로, GetPreferredSize를 직접 호출해 그 결과로 Size를 미리 확정한다.
        _closeButton = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Top = 6, Text = "×" };
        _closeButton.Size = _closeButton.GetPreferredSize(Size.Empty);
        _closeButton.Left = DetailedSize.Width - 10 - _closeButton.Width;
        _closeButton.Click += (_, _) => Close();

        // 일시정지/재개 버튼 (FR-035) — 상세 보기를 클릭해야만 보이며, 세션은 하나뿐이므로 눌렀을 때
        // RotationEngine이 모든 데스크톱의 창에 동일하게 반영한다. 고정 Width(90px)로는 "일시정지"
        // 텍스트가 실제 실행 화면에서 잘려 보이는 문제가 보고됐다 — AutoSize로 바꿨다(왼쪽 정렬이라
        // Left는 그대로 두고 Width만 실제 텍스트에 맞춰 계산된다).
        _pauseButton = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Left = 10, Top = 6, Text = "일시정지" };
        _pauseButton.Click += (_, _) => PauseToggleRequested?.Invoke();

        Controls.Add(_minimalCountdownLabel);
        Controls.Add(_progressRingControl);
        Controls.AddRange(new Control[]
        {
            _nextSwitchLabel, _remainingToFinishLabel, _totalPlannedRuntimeLabel, _cycleProgressLabel,
            _countsHeaderLabel, _perDesktopCountsList, _closeButton, _pauseButton,
        });

        // 창 본문(최소 보기 숫자, 상세 보기 라벨들, 폼 배경) 어디를 눌러도 클릭/드래그를 인식한다.
        WireDragAndClickHandlers(this);
        WireDragAndClickHandlers(_minimalCountdownLabel);
        WireDragAndClickHandlers(_progressRingControl);
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

    /// <summary>원형 진행률 그래픽(FR-036)이 지금 보여야 하는지 — 최소 보기이고, 옵션이 켜져 있고,
    /// 아직 목표에 도달하지 않았을 때만 보인다. 목표 도달 후("완료")에는 다음 전환까지 남은 시간이라는
    /// 개념 자체가 없으므로 그래픽을 숨긴다.</summary>
    private bool ShouldShowProgressRing => _viewMode == ViewMode.Minimal && _showProgressRing && !_targetReached;

    private void ApplyViewMode()
    {
        bool detailed = _viewMode == ViewMode.Detailed;

        _minimalCountdownLabel.Visible = !detailed;
        _progressRingControl.Visible = ShouldShowProgressRing;
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
    /// 잘려 보이는 문제가 실제 실행 화면에서 발견됐다. 실제 텍스트 폭(+ 켜져 있으면 원형 진행률
    /// 그래픽 폭, FR-036)을 측정해 필요한 만큼만 창을 넓히고(최소 크기는 MinimalSize 유지), 숫자
    /// 라벨과 원형 그래픽을 하나의 그룹으로 보아 가로 중앙에 배치한다.
    /// </summary>
    private void ResizeMinimalWindowToFitText()
    {
        bool showRing = ShouldShowProgressRing;
        Size measuredText = TextRenderer.MeasureText(_minimalCountdownLabel.Text, _minimalCountdownLabel.Font);
        int labelWidth = measuredText.Width;
        int ringContribution = showRing ? ProgressRingDiameter + ProgressRingGapPixels : 0;
        int contentWidth = labelWidth + ringContribution;
        int desiredWidth = Math.Max(MinimalSize.Width, contentWidth + 24);

        if (ClientSize.Width != desiredWidth || ClientSize.Height != MinimalSize.Height)
        {
            int centerX = Location.X + ClientSize.Width / 2;
            ClientSize = new Size(desiredWidth, MinimalSize.Height);
            Location = new Point(centerX - ClientSize.Width / 2, Location.Y);
        }

        int startX = (ClientSize.Width - contentWidth) / 2;
        _minimalCountdownLabel.SetBounds(startX, 0, labelWidth, MinimalSize.Height);

        if (showRing)
        {
            int ringY = (MinimalSize.Height - ProgressRingDiameter) / 2;
            _progressRingControl.SetBounds(startX + labelWidth + ProgressRingGapPixels, ringY, ProgressRingDiameter, ProgressRingDiameter);
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

    /// <summary>매초 RotationEngine의 타이머 틱에서 호출되어 표시 내용을 최신 상태로 갱신한다 (FR-005, FR-006, FR-014, FR-023, FR-030, FR-031, FR-036).</summary>
    public void RefreshDisplay(RotationSession session)
    {
        ReassertTopMost();

        _minimalCountdownLabel.Text = FormatMinimalText(session);

        _showProgressRing = session.ShowProgressRing;
        _targetReached = session.TargetReached;
        _progressRingControl.Visible = ShouldShowProgressRing;
        _progressRingControl.Ratio = session.NextSwitchProgressRatio;

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
    /// topmost 순위를 매초 다시 강제한다(FR-004) — 창 이동·크기·활성화(포커스)에는 영향을 주지
    /// 않도록 SWP_NOMOVE·SWP_NOSIZE·SWP_NOACTIVATE를 함께 지정한다. 핸들이 아직 없으면(생성 직후
    /// 극초반) 아무 일도 하지 않는다.
    /// </summary>
    private void ReassertTopMost()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
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

    /// <summary>
    /// 최소 보기 숫자 오른쪽에 그리는 원형 진행률 그래픽(FR-036). 테두리(뼈대)는 <see cref="Ratio"/>
    /// 값과 무관하게 항상 동일한 완전한 원으로 그려 형태가 찌그러지지 않도록 보장하고, 내부는
    /// 남은 시간 비율만큼(파이 형태로) 채운다 — 남은 시간이 줄면 채워진 비율만 줄어든다.
    /// </summary>
    private sealed class ProgressRingControl : Control
    {
        private const int OutlineWidth = 3;
        private static readonly Color OutlineColor = Color.DimGray;
        private static readonly Color FillColor = Color.MediumSeaGreen;

        private double _ratio = 1.0;

        /// <summary>다음 자동 전환까지 남은 시간 비율(1.0 = 방금 전환됨 · 0.0 = 곧 전환). 0~1로 클램프된다.</summary>
        public double Ratio
        {
            get => _ratio;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_ratio - clamped) < 0.0005)
                {
                    return;
                }

                _ratio = clamped;
                Invalidate();
            }
        }

        public ProgressRingControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(OutlineWidth / 2, OutlineWidth / 2, Width - OutlineWidth - 1, Height - OutlineWidth - 1);

            if (_ratio > 0.0)
            {
                using var fillBrush = new SolidBrush(FillColor);
                // 12시 방향(-90도)에서 시작해 시계 방향으로 남은 비율만큼만 채운다 — 시간이
                // 줄어들수록 채워진 부채꼴만 작아지고, 테두리는 아래에서 항상 완전한 원으로
                // 별도로 그리므로 형태 자체는 절대 찌그러지지 않는다(FR-036 핵심 요구).
                e.Graphics.FillPie(fillBrush, bounds, -90f, (float)(_ratio * 360.0));
            }

            using var outlinePen = new Pen(OutlineColor, OutlineWidth);
            e.Graphics.DrawEllipse(outlinePen, bounds);
        }
    }
}
