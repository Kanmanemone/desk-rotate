namespace DeskRotate;

/// <summary>
/// 시작 입력 폼 (contracts/startup-input-contract.md).
/// 순회할 데스크톱 범위(시작~끝, 1-based)·전환 간격(초)·목표 사이클 수(범위 안 데스크톱을 한 번씩
/// 모두 순회하는 것이 1사이클)와 표시 옵션(초 단위 표시, 사이클 번호 표시, 진행률 원 표시)을 입력받고,
/// 총 예상 실행 시간을 실시간으로 미리 보여준다.
/// 기본값은 범위 1~3, 간격 300초(5분), 목표 사이클 4, 초 단위 표시 켜짐, 사이클 번호 표시 켜짐,
/// 진행률 원 표시 켜짐이다 (FR-026, FR-013, FR-031, FR-036).
/// </summary>
public sealed class StartupInputForm : Form
{
    private readonly NumericUpDown _rangeStartInput;
    private readonly NumericUpDown _rangeEndInput;
    private readonly NumericUpDown _intervalSecondsInput;
    private readonly NumericUpDown _targetCycleCountInput;
    private readonly CheckBox _showSecondsUnitCheckBox;
    private readonly CheckBox _showCycleNumberCheckBox;
    private readonly CheckBox _showProgressRingCheckBox;
    private readonly Label _totalRuntimePreviewLabel;
    private readonly Label _errorLabel;

    /// <summary>사용자가 유효한 값을 제출했을 때 발생한다.</summary>
    public event Action<RotationSession>? Submitted;

    public StartupInputForm()
    {
        Text = "desk-rotate 시작 설정";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.None;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        // 라벨 Height는 임의값이 아니라, 이 폼에서 실제로 쓰는 글꼴(맑은 고딕 9pt)로
        // TextRenderer.MeasureText를 직접 측정해 나온 실측값(25px)에 여유를 더한 것이다 — 이전에
        // Height=20/18로 잡았던 라벨들은 실제 실행 화면에서 텍스트 아래쪽 몇 픽셀이 잘려 보이는
        // 문제가 있었다(라벨 높이 잘림 버그).
        const int LabelHeight = 26;

        // === 입력부: 회전 설정값 입력 ===
        var rangeLabel = new Label { Text = "순회할 데스크톱 범위 (시작 ~ 끝)", Left = 16, Top = 16, Width = 320, Height = LabelHeight };
        _rangeStartInput = new NumericUpDown { Left = 16, Top = 44, Width = 90, Minimum = 1, Maximum = 100, Value = 1 };
        var rangeTildeLabel = new Label { Text = "~", Left = 112, Top = 46, Width = 16, Height = LabelHeight };
        _rangeEndInput = new NumericUpDown { Left = 132, Top = 44, Width = 90, Minimum = 1, Maximum = 100, Value = 3 };

        var intervalLabel = new Label { Text = "전환 간격 (초)", Left = 16, Top = 75, Width = 260, Height = LabelHeight };
        _intervalSecondsInput = new NumericUpDown { Left = 16, Top = 103, Width = 100, Minimum = 1, Maximum = 86400, Value = 300 };

        // 긴 설명을 한 줄에 다 담으려다 자동 줄바꿈이 기대만큼 동작하지 않아 잘려 보이던 문제가
        // 실제 실행 화면에서 발견됐다 — 한 줄에 확실히 들어가는 짧은 문구로 대체했다.
        var targetCycleLabel = new Label { Text = "목표 사이클 수 (1바퀴 = 1사이클)", Left = 16, Top = 134, Width = 320, Height = LabelHeight };
        // Maximum은 전환 간격(최대 86400초)·데스크톱 개수(최대 100)와 곱한 총 전환 횟수가
        // int 오버플로가 나지 않도록 100으로 제한한다(86400 * 100 * 100 = 8.64억 < int.MaxValue).
        // spec.md/plan.md는 별도 상한을 두지 않기로 했으나, TotalPlannedRuntimeSeconds가 int라서
        // 생기는 순수 기술적 안전장치이며 실사용 범위를 넘어서지 않는다.
        _targetCycleCountInput = new NumericUpDown { Left = 16, Top = 162, Width = 100, Minimum = 1, Maximum = 100, Value = 4 };

        // 체크박스는 고정 Width 대신 AutoSize를 쓴다 — 고정 폭(328px)은 이 세션의 측정 환경에서는
        // 여유가 있어 보였지만, 실제 사용자 화면(다른 DPI 배율)에서는 "진행률 원 표시" 체크박스의
        // 텍스트 끝부분이 잘려 보이는 문제가 보고됐다. AutoSize는 그 컨트롤이 실제로 그려질 때의
        // 글꼴·DPI를 그대로 반영해 크기를 계산하므로 환경에 상관없이 잘리지 않는다.
        _showSecondsUnitCheckBox = new CheckBox { Text = "초 단위로 표시 (예: 137초)", Left = 16, Top = 195, AutoSize = true, Checked = true };
        _showCycleNumberCheckBox = new CheckBox { Text = "사이클 번호 표시 (예: [2번째]137초)", Left = 16, Top = 225, AutoSize = true, Checked = true };
        _showProgressRingCheckBox = new CheckBox { Text = "진행률 원 표시 (숫자 옆 원형 그래픽)", Left = 16, Top = 255, AutoSize = true, Checked = true };

        _errorLabel = new Label { Left = 16, Top = 289, Width = 328, Height = 40, ForeColor = Color.Firebrick };

        // === 구분선 (입력부 / 확인부) — 거창한 그룹박스 대신 얇은 수평선 하나로만 구분한다 ===
        var divider = new Panel { Left = 16, Top = 339, Width = 328, Height = 1, BackColor = SystemColors.ControlDark };

        // === 확인부: 예상 실행 시간 · 시작 전 대기 안내 · 시작 버튼 ===
        _totalRuntimePreviewLabel = new Label { Left = 16, Top = 354, Width = 328, Height = LabelHeight };

        // RotationEngine.GuaranteedFirstDesktopSeekSeconds는 상황과 무관하게 항상 정확히 걸리는
        // 값이다(FR-034, "실제 데스크톱 1번을 찾는" 맹목적 뒤로가기 단계). 그 이후 범위 안 데스크톱을
        // 방문·생성하며 준비하는 시간(FR-033)은 이미 존재하는 데스크톱 수에 따라 달라져 정확한
        // 초로 예측할 수 없으므로, 그 부분은 "조금 더 걸릴 수 있다"로만 안내한다.
        string startupDelayText = $"시작하면 첫 데스크톱을 찾느라 최소 {RotationEngine.GuaranteedFirstDesktopSeekSeconds}초간 화면이 여러 번 바뀌고, 이후 범위를 준비하는 동안 조금 더 걸릴 수 있습니다.";
        // 2줄로 줄바꿈되는 긴 문구라 고정 Height를 임의로 잡으면(처음에 40으로 잡았다가 실제
        // 렌더링에서 마지막 줄이 잘리는 문제가 실측으로 발견됐다) 잘릴 위험이 있다 — 다른 라벨/
        // 체크박스 크기 계산과 같은 원칙으로, 실제 폭(328px) 기준 줄바꿈된 높이를 직접 측정해서 쓴다.
        Size startupDelayTextSize = TextRenderer.MeasureText(
            startupDelayText, Font, new Size(328, int.MaxValue), TextFormatFlags.WordBreak);

        var startupDelayLabel = new Label
        {
            Text = startupDelayText,
            Left = 16,
            Top = 384,
            Width = 328,
            Height = startupDelayTextSize.Height + 6,
            ForeColor = Color.DimGray,
        };

        int startButtonTop = startupDelayLabel.Bottom + 10;
        var startButton = new Button { Text = "시작", Left = 240, Top = startButtonTop, Width = 100, Height = 32 };
        startButton.Click += (_, _) => OnStart();

        ClientSize = new Size(360, startButton.Bottom + 16);

        _rangeStartInput.ValueChanged += (_, _) => UpdatePreview();
        _rangeEndInput.ValueChanged += (_, _) => UpdatePreview();
        _intervalSecondsInput.ValueChanged += (_, _) => UpdatePreview();
        _targetCycleCountInput.ValueChanged += (_, _) => UpdatePreview();

        Controls.AddRange(new Control[]
        {
            rangeLabel, _rangeStartInput, rangeTildeLabel, _rangeEndInput,
            intervalLabel, _intervalSecondsInput,
            targetCycleLabel, _targetCycleCountInput,
            _showSecondsUnitCheckBox, _showCycleNumberCheckBox, _showProgressRingCheckBox,
            _errorLabel,
            divider,
            _totalRuntimePreviewLabel, startupDelayLabel, startButton,
        });

        UpdatePreview();
    }

    /// <summary>총 예상 실행 시간(전환 간격 × 목표 사이클 수 × 데스크톱 개수) 미리보기를 갱신한다 (FR-013, FR-014, SC-005).</summary>
    private void UpdatePreview()
    {
        int interval = (int)_intervalSecondsInput.Value;
        int targetCycleCount = (int)_targetCycleCountInput.Value;
        int desktopCount = Math.Max(1, (int)_rangeEndInput.Value - (int)_rangeStartInput.Value + 1);
        int totalSeconds = interval * targetCycleCount * desktopCount;
        _totalRuntimePreviewLabel.Text = $"총 예상 실행 시간: {FormatDuration(totalSeconds)}";
    }

    private static string FormatDuration(int totalSeconds)
    {
        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}시간 {span.Minutes}분 {span.Seconds}초"
            : $"{span.Minutes}분 {span.Seconds}초";
    }

    private void OnStart()
    {
        int rangeStart = (int)_rangeStartInput.Value;
        int rangeEnd = (int)_rangeEndInput.Value;

        if (rangeEnd < rangeStart)
        {
            _errorLabel.Text = "끝 번호는 시작 번호 이상이어야 합니다.";
            return;
        }

        _errorLabel.Text = string.Empty;

        var session = new RotationSession(
            rangeStart: rangeStart,
            rangeEnd: rangeEnd,
            intervalSeconds: (int)_intervalSecondsInput.Value,
            targetCycleCount: (int)_targetCycleCountInput.Value,
            showSecondsUnit: _showSecondsUnitCheckBox.Checked,
            showCycleNumber: _showCycleNumberCheckBox.Checked,
            showProgressRing: _showProgressRingCheckBox.Checked);

        Submitted?.Invoke(session);
    }
}
