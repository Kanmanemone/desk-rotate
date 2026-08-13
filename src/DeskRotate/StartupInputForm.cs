namespace DeskRotate;

/// <summary>
/// 시작 입력 폼 (contracts/startup-input-contract.md).
/// 순회할 데스크톱 범위(시작~끝, 1-based)·전환 간격(초)·목표 사이클 수(범위 안 데스크톱을 한 번씩
/// 모두 순회하는 것이 1사이클)와 표시 옵션(초 단위 표시, 사이클 번호 표시)을 입력받고,
/// 총 예상 실행 시간을 실시간으로 미리 보여준다.
/// 기본값은 범위 1~3, 간격 300초(5분), 목표 사이클 4, 초 단위 표시 켜짐, 사이클 번호 표시 켜짐이다 (FR-026, FR-013, FR-031).
/// </summary>
public sealed class StartupInputForm : Form
{
    private readonly NumericUpDown _rangeStartInput;
    private readonly NumericUpDown _rangeEndInput;
    private readonly NumericUpDown _intervalSecondsInput;
    private readonly NumericUpDown _targetCycleCountInput;
    private readonly CheckBox _showSecondsUnitCheckBox;
    private readonly CheckBox _showCycleNumberCheckBox;
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
        ClientSize = new Size(360, 400);

        // 라벨/체크박스 Height는 임의값이 아니라, 이 폼에서 실제로 쓰는 글꼴(맑은 고딕 9pt)로
        // TextRenderer.MeasureText를 직접 측정해 나온 실측값(25px, 체크박스는 글리프 포함 29px)에
        // 여유를 더한 것이다 — 이전에 Height=20/18로 잡았던 라벨들은 실제 실행 화면에서 텍스트
        // 아래쪽 몇 픽셀이 잘려 보이는 문제가 있었다(라벨 높이 잘림 버그).
        const int LabelHeight = 26;
        // 체크박스는 텍스트 외에 체크 글리프 영역까지 포함해 라벨보다 조금 더 넉넉한 높이가 필요하다
        // (실측 PreferredSize 기준 29px에 가까움).
        const int CheckBoxHeight = 28;

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

        _showSecondsUnitCheckBox = new CheckBox { Text = "초 단위로 표시 (예: 137초)", Left = 16, Top = 195, Width = 328, Height = CheckBoxHeight, Checked = true };
        _showCycleNumberCheckBox = new CheckBox { Text = "사이클 번호 표시 (예: [2번째]137초)", Left = 16, Top = 225, Width = 328, Height = CheckBoxHeight, Checked = true };

        _totalRuntimePreviewLabel = new Label { Left = 16, Top = 259, Width = 320, Height = LabelHeight };
        _errorLabel = new Label { Left = 16, Top = 289, Width = 320, Height = 40, ForeColor = Color.Firebrick };

        var startButton = new Button { Text = "시작", Left = 240, Top = 340, Width = 100, Height = 32 };
        startButton.Click += (_, _) => OnStart();

        _rangeStartInput.ValueChanged += (_, _) => UpdatePreview();
        _rangeEndInput.ValueChanged += (_, _) => UpdatePreview();
        _intervalSecondsInput.ValueChanged += (_, _) => UpdatePreview();
        _targetCycleCountInput.ValueChanged += (_, _) => UpdatePreview();

        Controls.AddRange(new Control[]
        {
            rangeLabel, _rangeStartInput, rangeTildeLabel, _rangeEndInput,
            intervalLabel, _intervalSecondsInput,
            targetCycleLabel, _targetCycleCountInput,
            _showSecondsUnitCheckBox, _showCycleNumberCheckBox,
            _totalRuntimePreviewLabel, _errorLabel, startButton,
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
            showCycleNumber: _showCycleNumberCheckBox.Checked);

        Submitted?.Invoke(session);
    }
}
