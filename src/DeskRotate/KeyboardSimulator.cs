using System.Runtime.InteropServices;

namespace DeskRotate;

public enum SwitchDirection
{
    Next,
    Previous,
}

/// <summary>
/// SendInput으로 Ctrl+Win+Left/Right를 시뮬레이션해 가상 데스크톱을 전환한다 (research.md §2).
/// keybd_event 등 레거시 API는 사용하지 않는다.
/// </summary>
public sealed class KeyboardSimulator
{
    private const int InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;

    private const ushort VkControl = 0x11;
    private const ushort VkLWin = 0x5B;
    private const ushort VkLeft = 0x25;
    private const ushort VkRight = 0x27;
    private const ushort VkD = 0x44;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    // 네이티브 INPUT 유니온은 세 멤버 중 가장 큰 MOUSEINPUT(64비트에서 32바이트) 크기로 정렬된다.
    // KEYBDINPUT(24바이트)만 담으면 유니온 전체 크기가 실제보다 작게 계산되어, SendInput에 넘기는
    // cbSize가 실제 INPUT 크기와 어긋나 ERROR_INVALID_PARAMETER(87)로 거부당한다 — 실제로 발생했던 크래시.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput mi;

        [FieldOffset(0)]
        public KeyboardInput ki;

        [FieldOffset(0)]
        public HardwareInput hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    /// <summary>
    /// SendInput이 실패할 때 즉시 예외를 던지는 대신 몇 번 재시도하는 횟수 — 다른 프로세스가
    /// 아주 짧게 입력을 가로채거나(예: 순간적인 보안 데스크톱 전환) 시스템이 일시적으로 바쁠 때
    /// SendInput이 실패하는 사례가 실제로 확인됐다(ERROR_ACCESS_DENIED). 이런 찰나의 방해까지
    /// 매번 앱 전체 크래시로 이어지면, 장시간 백그라운드로 켜 두는 이 프로그램의 용도상 세션
    /// 전체가 조용히 죽어버리는 결과가 되므로 짧게 재시도한다.
    /// </summary>
    private const int SendInputRetryAttempts = 3;
    private const int SendInputRetryDelayMilliseconds = 150;

    /// <summary>Ctrl+Win+Left 또는 Ctrl+Win+Right를 한 번 눌렀다 뗀다.</summary>
    public void SendSwitchKeystroke(SwitchDirection direction)
    {
        ushort arrowKey = direction == SwitchDirection.Next ? VkRight : VkLeft;

        var inputs = new[]
        {
            KeyDown(VkControl),
            KeyDown(VkLWin),
            KeyDown(arrowKey),
            KeyUp(arrowKey),
            KeyUp(VkLWin),
            KeyUp(VkControl),
        };

        SendInputWithRetry(inputs);
    }

    /// <summary>
    /// Win+Ctrl+D(Windows 표준 "새 데스크톱 추가" 단축키)를 한 번 눌렀다 뗀다 — 새 가상 데스크톱을
    /// 만들고 그쪽으로 전환한다. 초기 탐색·설정 중 범위의 데스크톱이 아직 존재하지 않을 때만 쓰인다
    /// (FR-033, 비공식 API 없이 표준 사용자 단축키만 사용).
    /// </summary>
    public void SendCreateDesktopKeystroke()
    {
        var inputs = new[]
        {
            KeyDown(VkControl),
            KeyDown(VkLWin),
            KeyDown(VkD),
            KeyUp(VkD),
            KeyUp(VkLWin),
            KeyUp(VkControl),
        };

        SendInputWithRetry(inputs);
    }

    private static void SendInputWithRetry(Input[] inputs)
    {
        int inputSize = Marshal.SizeOf<Input>();
        int lastError = 0;

        for (int attempt = 1; attempt <= SendInputRetryAttempts; attempt++)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, inputSize);
            if (sent == (uint)inputs.Length)
            {
                return;
            }

            lastError = Marshal.GetLastWin32Error();
            if (attempt < SendInputRetryAttempts)
            {
                Thread.Sleep(SendInputRetryDelayMilliseconds);
            }
        }

        throw new InvalidOperationException(
            $"SendInput failed after {SendInputRetryAttempts} attempts (last Win32 error {lastError}).");
    }

    private static Input KeyDown(ushort vk) => new()
    {
        type = InputKeyboard,
        u = new InputUnion { ki = new KeyboardInput { wVk = vk, dwFlags = 0 } },
    };

    private static Input KeyUp(ushort vk) => new()
    {
        type = InputKeyboard,
        u = new InputUnion { ki = new KeyboardInput { wVk = vk, dwFlags = KeyEventFKeyUp } },
    };
}
