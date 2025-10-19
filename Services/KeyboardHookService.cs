using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenShotManager.Services.Interfaces;

namespace ScreenShotManager.Services;

/// <summary>
/// Global keyboard hook service to intercept Print Screen key
/// </summary>
public class KeyboardHookService : IKeyboardHookService
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int VkSnapshot = 0x2C;

    private IntPtr hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? proc;
    private bool isPaused;

    public event EventHandler? PrintScreenPressed;
    
    public bool IsPaused
    {
        get => isPaused;
        set => isPaused = value;
    }

    public void Start()
    {
        proc = HookCallback;
        hookId = SetHook(proc);
    }

    public void Stop()
    {
        Dispose();
    }

    private IntPtr SetHook(LowLevelKeyboardProc keyUse)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule != null)
        {
            return SetWindowsHookEx(WhKeyboardLl, keyUse,
                GetModuleHandle(curModule.ModuleName), 0);
        }
        return IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WmKeydown)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            
            if (vkCode == VkSnapshot)
            {
                if (!isPaused)
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                        new Action(() => PrintScreenPressed?.Invoke(this, EventArgs.Empty))
                    );
                    return 1;
                }
            }
        }
        return CallNextHookEx(hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hookId);
            hookId = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    #region Win32 API

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook,
        LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
        IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    #endregion
}

