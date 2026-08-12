using System.Runtime.InteropServices;

namespace DeskRotate;

/// <summary>
/// Windows의 공식 문서화된 IVirtualDesktopManager 인터페이스만 감싼다.
/// IVirtualDesktopManagerInternal 등 비공식·비문서화 인터페이스는 절대 참조하지 않는다
/// (spec.md Clarifications, research.md §1).
/// </summary>
[ComImport]
[Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
internal class VirtualDesktopManagerComObject
{
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
internal interface IVirtualDesktopManager
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out bool onCurrentDesktop);

    [PreserveSig]
    int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

    [PreserveSig]
    int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
}

/// <summary>
/// 공식 IVirtualDesktopManager 조회 API 래퍼. 포커스를 빼앗지 않는 순수 읽기/이동 동작만 제공한다.
/// </summary>
public sealed class VirtualDesktopInterop
{
    private readonly IVirtualDesktopManager _manager;

    public VirtualDesktopInterop()
    {
        _manager = (IVirtualDesktopManager)new VirtualDesktopManagerComObject();
    }

    /// <summary>
    /// 지정한 창이 지금 활성화된(사용자에게 보이는) 가상 데스크톱에 있는지 조회한다.
    /// 조회 실패 시(창이 아직 데스크톱에 배치되지 않은 경우 등) false를 반환한다.
    /// </summary>
    public bool IsWindowOnCurrentVirtualDesktop(IntPtr windowHandle)
    {
        int hr = _manager.IsWindowOnCurrentVirtualDesktop(windowHandle, out bool onCurrentDesktop);
        return hr == 0 && onCurrentDesktop;
    }

    /// <summary>지정한 창이 속한 가상 데스크톱의 식별자를 조회한다.</summary>
    public Guid GetWindowDesktopId(IntPtr windowHandle)
    {
        int hr = _manager.GetWindowDesktopId(windowHandle, out Guid desktopId);
        if (hr != 0)
        {
            throw new InvalidOperationException($"GetWindowDesktopId failed with HRESULT 0x{hr:X8}.");
        }

        return desktopId;
    }

    /// <summary>지정한 창을 특정 가상 데스크톱으로 옮긴다.</summary>
    public void MoveWindowToDesktop(IntPtr windowHandle, Guid desktopId)
    {
        int hr = _manager.MoveWindowToDesktop(windowHandle, ref desktopId);
        if (hr != 0)
        {
            throw new InvalidOperationException($"MoveWindowToDesktop failed with HRESULT 0x{hr:X8}.");
        }
    }
}
