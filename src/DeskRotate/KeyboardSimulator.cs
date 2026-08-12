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

        int inputSize = Marshal.SizeOf<Input>();
        uint sent = SendInput((uint)inputs.Length, inputs, inputSize);
        if (sent != (uint)inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput sent {sent} of {inputs.Length} events (Win32 error {Marshal.GetLastWin32Error()}).");
        }
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

        int inputSize = Marshal.SizeOf<Input>();
        uint sent = SendInput((uint)inputs.Length, inputs, inputSize);
        if (sent != (uint)inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput sent {sent} of {inputs.Length} events (Win32 error {Marshal.GetLastWin32Error()}).");
        }
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
