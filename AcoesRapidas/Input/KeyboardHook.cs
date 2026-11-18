using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AcoesRapidas.Input;

public readonly record struct RawKeyEvent(int VirtualKey, int ScanCode, uint Flags, bool IsDown, bool IsInjected);

/// <summary>
/// Hook global de teclado em uma thread própria, para a interface nunca atrasar a digitação.
/// Os eventos são disparados nessa thread.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly bool _passive;
    private readonly CopilotKeyDetector _detector = new();
    private readonly MenuKeyFilter _menuKeys = new();
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private volatile bool _menuOpen;

    /// <param name="passive">Quando verdadeiro, apenas observa as teclas e nunca bloqueia nada.</param>
    public KeyboardHook(bool passive = false)
    {
        _passive = passive;
        _callback = OnKeyboardEvent;
    }

    public event Action? CopilotKeyPressed;

    public event Action<MenuCommand>? MenuCommandReceived;

    public event Action<RawKeyEvent>? RawKeyReceived;

    public bool MenuOpen
    {
        get => _menuOpen;
        set => _menuOpen = value;
    }

    public void Start()
    {
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();

        _thread = new Thread(() =>
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            _hook = NativeMethods.SetWindowsHookExW(
                NativeMethods.WH_KEYBOARD_LL, _callback, NativeMethods.GetModuleHandleW(null), 0);

            if (_hook == 0)
            {
                failure = new Win32Exception(Marshal.GetLastWin32Error());
                ready.Set();
                return;
            }

            ready.Set();
            NativeMethods.RunMessageLoop();
            NativeMethods.UnhookWindowsHookEx(_hook);
        })
        {
            IsBackground = true,
            Name = "Hook de teclado",
        };

        _thread.Start();
        ready.Wait();

        if (failure is not null)
        {
            throw new InvalidOperationException("Não foi possível monitorar o teclado.", failure);
        }
    }

    public void Dispose()
    {
        if (_thread is { IsAlive: true })
        {
            NativeMethods.PostThreadMessageW(_threadId, NativeMethods.WM_QUIT, 0, 0);
            _thread.Join(TimeSpan.FromSeconds(1));
        }
    }

    private nint OnKeyboardEvent(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && HandleKey(wParam, lParam))
        {
            return 1;
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool HandleKey(nint wParam, nint lParam)
    {
        var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        var isDown = wParam is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
        var isInjected = (data.flags & NativeMethods.LLKHF_INJECTED) != 0;
        var vk = (int)data.vkCode;

        RawKeyReceived?.Invoke(new RawKeyEvent(vk, (int)data.scanCode, data.flags, isDown, isInjected));

        if (_passive || (isInjected && !CopilotKeyDetector.IsCompanionKey(vk)))
        {
            return false;
        }

        var detection = _detector.Process(vk, isDown, data.time);
        if (detection.MaskWinKey)
        {
            NativeMethods.SendNeutralKey();
        }

        if (detection.Triggered)
        {
            CopilotKeyPressed?.Invoke();
        }

        if (detection.Suppress)
        {
            return true;
        }

        var menuKey = _menuKeys.Process(vk, isDown, _menuOpen);
        if (menuKey.Command is { } command)
        {
            MenuCommandReceived?.Invoke(command);
        }

        return menuKey.Suppress;
    }
}
