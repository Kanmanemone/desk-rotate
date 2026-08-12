namespace DeskRotate;

/// <summary>
/// 데스크톱별 플로팅 창 (contracts/floating-window-contract.md).
/// 항상 위로 표시되는 일반 창이며, 남은 시간·총 예상 실행 시간·데스크톱별 전환 횟수를 보여주고
/// 동시에 공식 IsWindowOnCurrentVirtualDesktop 조회의 대상(HWND)이 되어 전환 검증에도 쓰인다.
/// </summary>
public sealed class FloatingWindowForm : Form
{
    private readonly int _desktopIndex;
    private readonly Label _nextSwitchLabel;
    private readonly Label _remainingToFinishLabel;
    private readonly Label _totalPlannedRuntimeLabel;
    private readonly ListBox _perDesktopCountsList;

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

        // 고정 픽셀 좌표로 배치하므로 WinForms의 DPI 자동 스케일링을 끈다 (StartupInputForm과 동일한 이유 —
        // 켜져 있으면 하단 컨트롤이 ClientSize 밖으로 밀려나 잘려 보이는 문제가 있었다).
        AutoScaleMode = AutoScaleMode.None;

        Text = $"desk-rotate — 데스크톱 {desktopIndex}";
        // 일반 창 스타일을 사용한다 — ToolWindow 계열(WS_EX_TOOLWINDOW)은 Alt+Tab에서 숨겨지므로 쓰지 않는다.
        // spec.md 결정: 이 창들은 일반 창이며 Alt+Tab·작업표시줄·Task View에 노출될 수 있어야 한다(FR-004, Assumptions).
        FormBorderStyle = FormBorderStyle.FixedSingle;
        TopMost = true;
        ShowInTaskbar = true;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(240, 240);
        StartPosition = FormStartPosition.Manual;

        _nextSwitchLabel = new Label { Left = 10, Top = 10, Width = 220, Height = 20 };
        _remainingToFinishLabel = new Label { Left = 10, Top = 32, Width = 220, Height = 20 };
        _totalPlannedRuntimeLabel = new Label { Left = 10, Top = 54, Width = 220, Height = 20 };
        var countsHeaderLabel = new Label { Left = 10, Top = 78, Width = 220, Height = 18, Text = "데스크톱별 전환 횟수:" };
        _perDesktopCountsList = new ListBox { Left = 10, Top = 98, Width = 220, Height = 130 };

        Controls.AddRange(new Control[]
        {
            _nextSwitchLabel, _remainingToFinishLabel, _totalPlannedRuntimeLabel,
            countsHeaderLabel, _perDesktopCountsList,
        });

        FormClosing += OnFormClosing;
    }

    /// <summary>초기 배치: 화면 상단 중앙(12시 방향) (FR-021). 이후 사용자가 드래그하면 세션 동안 그 위치가 유지된다.</summary>
    public void PlaceAtTopCenter()
    {
        Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int x = workingArea.Left + (workingArea.Width - Width) / 2;
        int y = workingArea.Top;
        Location = new Point(x, y);
    }

    /// <summary>매초 RotationEngine의 타이머 틱에서 호출되어 표시 내용을 최신 상태로 갱신한다 (FR-005, FR-006, FR-014).</summary>
    public void RefreshDisplay(RotationSession session)
    {
        _nextSwitchLabel.Text = session.TargetReached
            ? "전환 완료 — 최종 통계"
            : $"다음 전환까지: {session.RemainingSecondsToNextSwitch}초";

        _remainingToFinishLabel.Text = session.TargetReached
            ? "종료까지 남은 시간: 0초"
            : $"종료까지 남은 시간: {session.RemainingSecondsToFinish}초";

        _totalPlannedRuntimeLabel.Text = $"총 예상 실행 시간: {session.TotalPlannedRuntimeSeconds}초";

        int selectedIndex = _perDesktopCountsList.TopIndex;
        _perDesktopCountsList.BeginUpdate();
        _perDesktopCountsList.Items.Clear();
        foreach (var pair in session.PerDesktopSwitchCounts.OrderBy(kv => kv.Key))
        {
            _perDesktopCountsList.Items.Add($"데스크톱 {pair.Key}: {pair.Value}회");
        }

        _perDesktopCountsList.EndUpdate();
        if (selectedIndex >= 0 && selectedIndex < _perDesktopCountsList.Items.Count)
        {
            _perDesktopCountsList.TopIndex = selectedIndex;
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
