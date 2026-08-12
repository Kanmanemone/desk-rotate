namespace DeskRotate;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 폼 크기 계산 전에 DPI 인식 모드를 지정해야 한다 — 지정하지 않으면 OS가 창을 통째로
        // 비트맵 확대하거나(흐릿해짐), WinForms가 컨트롤 좌표만 스케일링해서 폼 하단 컨트롤이
        // ClientSize 밖으로 밀려날 수 있다(실제 발생했던 버그: 시작 버튼이 잘려서 안 보임).
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        RotationSession? session = RunStartupInput();
        if (session is null)
        {
            // 사용자가 값을 제출하지 않고 시작 입력 창을 닫음 — 세션을 시작하지 않고 종료.
            return;
        }

        var engine = new RotationEngine(session, new VirtualDesktopInterop(), new KeyboardSimulator());
        engine.ExitRequested += Application.Exit;

        engine.PerformInitialSetup();
        engine.Start();

        // 데스크톱별 플로팅 창이 이제 애플리케이션의 생명 주기를 담당한다 —
        // 어느 창에서든 종료가 확정되면 engine.ExitRequested가 Application.Exit()를 호출한다 (FR-009).
        Application.Run();
    }

    private static RotationSession? RunStartupInput()
    {
        RotationSession? session = null;

        using var startupForm = new StartupInputForm();
        startupForm.Submitted += s =>
        {
            session = s;
            startupForm.Close();
        };

        Application.Run(startupForm);
        return session;
    }
}
