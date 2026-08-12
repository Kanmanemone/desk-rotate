namespace DeskRotate;

/// <summary>
/// 시작 입력 폼 (contracts/startup-input-contract.md).
/// 데스크톱 개수·전환 간격(초)·목표 총 전환 횟수를 입력받고, 총 예상 실행 시간을 실시간으로 미리 보여준다.
/// 세 값 모두 NumericUpDown의 Minimum=1로 "1 이상의 정수" 제약(FR-003, FR-011, FR-013)을 강제한다.
/// </summary>
public sealed class StartupInputForm : Form
{
    private readonly NumericUpDown _desktopCountInput;
    private readonly NumericUpDown _intervalSecondsInput;
    private readonly NumericUpDown _targetSwitchCountInput;
    private readonly Label _totalRuntimePreviewLabel;

    /// <summary>사용자가 유효한 값을 제출했을 때 발생한다.</summary>
    public event Action<RotationSession>? Submitted;

    public StartupInputForm()
    {
        // 고정 픽셀 좌표로 배치하므로 WinForms의 DPI 자동 스케일링을 끈다 — 켜져 있으면
        // 컨트롤 좌표만 배율만큼 커지고 ClientSize는 그대로라 하단 컨트롤(시작 버튼)이
        // 보이는 영역 밖으로 밀려나는 문제가 있었다.
        AutoScaleMode = AutoScaleMode.None;

        Text = "desk-rotate 시작 설정";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(340, 280);

        var desktopCountLabel = new Label { Text = "현재 떠 있는 데스크톱 개수", Left = 16, Top = 16, Width = 260 };
        _desktopCountInput = new NumericUpDown { Left = 16, Top = 38, Width = 100, Minimum = 1, Maximum = 100, Value = 2 };

        var intervalLabel = new Label { Text = "전환 간격 (초)", Left = 16, Top = 70, Width = 260 };
        _intervalSecondsInput = new NumericUpDown { Left = 16, Top = 92, Width = 100, Minimum = 1, Maximum = 86400, Value = 1500 };

        var targetCountLabel = new Label { Text = "목표 총 전환 횟수", Left = 16, Top = 124, Width = 260 };
        // Maximum은 전환 간격(최대 86400초)과 곱했을 때 int 오버플로가 나지 않도록 10000으로 제한한다
        // (86400 * 10000 = 8.64억 < int.MaxValue). spec.md/plan.md는 별도 상한을 두지 않기로 했으나,
        // TotalPlannedRuntimeSeconds가 int라서 생기는 순수 기술적 안전장치이며 실사용 범위를 넘어서지 않는다.
        _targetSwitchCountInput = new NumericUpDown { Left = 16, Top = 146, Width = 100, Minimum = 1, Maximum = 10000, Value = 8 };

        _totalRuntimePreviewLabel = new Label { Left = 16, Top = 182, Width = 300, Height = 20 };

        var startButton = new Button { Text = "시작", Left = 220, Top = 220, Width = 100, Height = 32 };
        startButton.Click += (_, _) => OnStart();

        _desktopCountInput.ValueChanged += (_, _) => UpdatePreview();
        _intervalSecondsInput.ValueChanged += (_, _) => UpdatePreview();
        _targetSwitchCountInput.ValueChanged += (_, _) => UpdatePreview();

        Controls.AddRange(new Control[]
        {
            desktopCountLabel, _desktopCountInput,
            intervalLabel, _intervalSecondsInput,
            targetCountLabel, _targetSwitchCountInput,
            _totalRuntimePreviewLabel, startButton,
        });

        UpdatePreview();
    }

    /// <summary>총 예상 실행 시간(전환 간격 × 목표 횟수) 미리보기를 갱신한다 (FR-014, SC-005).</summary>
    private void UpdatePreview()
    {
        int interval = (int)_intervalSecondsInput.Value;
        int target = (int)_targetSwitchCountInput.Value;
        int totalSeconds = interval * target;
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
        var session = new RotationSession(
            totalDesktopCount: (int)_desktopCountInput.Value,
            intervalSeconds: (int)_intervalSecondsInput.Value,
            targetSwitchCount: (int)_targetSwitchCountInput.Value);

        Submitted?.Invoke(session);
    }
}
