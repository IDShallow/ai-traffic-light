using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeTrafficLight
{
    internal static class Win32
    {
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int WM_DPICHANGED = 0x02E0;
        public const int HTCAPTION = 2;
        public const int SW_RESTORE = 9;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_MENU = 0x12;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        /// <summary>系统（主屏）DPI。Win10 1607+；DPI 感知由 app.manifest 声明为 Per-Monitor V2。</summary>
        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        public static int DpiOfWParam(IntPtr wParam)
        {
            return (int)(((long)wParam >> 16) & 0xFFFF);
        }

        public static Win32.RECT RectOfLParam(IntPtr lParam)
        {
            return (Win32.RECT)Marshal.PtrToStructure(lParam, typeof(Win32.RECT));
        }

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        /// <summary>把窗口带到前台。裸调 SetForegroundWindow 会被前台锁拒绝，先合成一次 ALT。</summary>
        public static void ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
            }
            catch { }
        }

        /// <summary>沿父进程链找第一个有主窗口的进程（VSCode 里 claude.exe 的父链最终指向 Code.exe）。</summary>
        public static IntPtr FindOwnerWindow(int pid)
        {
            int cur = pid;
            for (int depth = 0; depth < 8 && cur > 0; depth++)
            {
                try
                {
                    Process p = Process.GetProcessById(cur);
                    if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
                }
                catch { return IntPtr.Zero; }

                int parent;
                if (!TryGetParentPid(cur, out parent)) return IntPtr.Zero;
                if (parent == cur) return IntPtr.Zero;
                cur = parent;
            }
            return IntPtr.Zero;
        }

        public static bool ActivateOwnerWindow(int pid)
        {
            IntPtr hwnd = FindOwnerWindow(pid);
            if (hwnd == IntPtr.Zero) return false;
            ForceForeground(hwnd);
            return true;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint TH32CS_SNAPPROCESS = 0x00000002;

        public static bool TryGetParentPid(int pid, out int parentPid)
        {
            parentPid = 0;
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == new IntPtr(-1)) return false;
            try
            {
                PROCESSENTRY32 e = new PROCESSENTRY32();
                e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                if (!Process32FirstW(snap, ref e)) return false;
                do
                {
                    if ((int)e.th32ProcessID == pid)
                    {
                        parentPid = (int)e.th32ParentProcessID;
                        return true;
                    }
                }
                while (Process32NextW(snap, ref e));
                return false;
            }
            finally
            {
                CloseHandle(snap);
            }
        }
    }
}
