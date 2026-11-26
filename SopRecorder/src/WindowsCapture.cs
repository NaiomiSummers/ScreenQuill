using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
namespace SopRecorder
{
    internal static class Native
    {
        private static readonly int RecorderPid = Process.GetCurrentProcess().Id;
        internal delegate bool EnumCallback(IntPtr hwnd, IntPtr param);
        internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; internal Rectangle Bounds { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } } }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseData { internal Point Point; internal uint Mouse, Flags, Time; internal UIntPtr Extra; }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] internal static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
        [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo { internal int Size, Flags; internal IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret; internal Rect CaretRect; }
        [DllImport("user32.dll")] internal static extern bool GetCaretPos(out Point point);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint GetFinalPathNameByHandle(IntPtr file, StringBuilder path, uint count, uint flags);
        internal static string Title(IntPtr hwnd) { StringBuilder b = new StringBuilder(1024); GetWindowText(hwnd, b, b.Capacity); return b.ToString(); }
        internal static bool Own(IntPtr hwnd) { uint pid; GetWindowThreadProcessId(hwnd, out pid); return pid == RecorderPid; }
    }
    internal sealed class WindowChoice
    {
        internal IntPtr Handle; internal uint Pid; internal long ProcessStart; internal string Title, App;
        public override string ToString() { return Title + " (" + App + ")"; }
        internal bool Available()
        {
            uint current;
            if (!Native.IsWindow(Handle) || !Native.IsWindowVisible(Handle) || Native.IsIconic(Handle) || Native.IsHungAppWindow(Handle)) return false;
            Native.GetWindowThreadProcessId(Handle, out current); if (current != Pid) return false;
            try { using (Process p = Process.GetProcessById((int)Pid)) return p.StartTime.ToUniversalTime().Ticks == ProcessStart; } catch { return false; }
        }
        internal static List<WindowChoice> List()
        {
            List<WindowChoice> result = new List<WindowChoice>();
            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
                if (Native.Own(hwnd) || !Native.IsWindowVisible(hwnd)) return true;
                string title = Native.Title(hwnd); if (string.IsNullOrWhiteSpace(title)) return true;
                int cloaked; if (Native.DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                try { using (Process p = Process.GetProcessById((int)pid)) result.Add(new WindowChoice { Handle = hwnd, Pid = pid, ProcessStart = p.StartTime.ToUniversalTime().Ticks, Title = title, App = p.ProcessName }); } catch { }
                return true;
            }, IntPtr.Zero);
            return result.OrderBy(x => x.Title).ToList();
        }
    }
    internal sealed class CaptureTarget
    {
        internal string DisplayName; internal Rectangle Bounds; internal WindowChoice Window;
        internal bool IsDisplay { get { return Window == null; } }
        internal string Label { get { return IsDisplay ? DisplayName + " " + Bounds.Width + " × " + Bounds.Height : Window.ToString(); } }
        internal bool Available()
        { return IsDisplay ? Screen.AllScreens.Any(s => s.DeviceName == DisplayName && s.Bounds == Bounds) : Window.Available(); }
        internal static CaptureTarget FromScreen(Screen s) { return new CaptureTarget { DisplayName = s.DeviceName, Bounds = s.Bounds }; }
        internal static CaptureTarget Default(string last)
        { return FromScreen(Screen.AllScreens.FirstOrDefault(s => s.DeviceName == last) ?? Screen.PrimaryScreen); }
        internal bool PointInScope(Native.Point point)
        {
            IntPtr root = Native.GetAncestor(Native.WindowFromPoint(point), 2);
            if (Native.Own(root)) return false;
            return IsDisplay ? Bounds.Contains(point.X, point.Y) : root == Window.Handle;
        }
        internal bool KeyboardInScope()
        {
            IntPtr foreground = Native.GetForegroundWindow(); if (foreground == IntPtr.Zero || Native.Own(foreground)) return false;
            if (!IsDisplay) return Native.GetAncestor(foreground, 2) == Window.Handle;
            // Prefer the actual focused control/caret's display. Fall back to the foreground window's center.
            Native.GuiInfo info = new Native.GuiInfo { Size = Marshal.SizeOf(typeof(Native.GuiInfo)) };
            if (Native.GetGUIThreadInfo(0, ref info) && info.Caret != IntPtr.Zero)
            {
                Native.Point point = new Native.Point { X = info.CaretRect.Left, Y = info.CaretRect.Top };
                if (Native.ClientToScreen(info.Caret, ref point)) return Bounds.Contains(point.X, point.Y);
            }
            Native.Rect rect; if (!Native.GetWindowRect(foreground, out rect)) return false;
            return Bounds.Contains((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        }
    }
    internal interface IFrameSource { Step Take(CaptureTarget target, string directory, CancellationToken token); }
    internal sealed class WindowsCapture : IFrameSource
    {
        private static long sequence;
        public Step Take(CaptureTarget target, string directory, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); if (!target.Available()) throw new InvalidOperationException("Capture target is unavailable. Choose an available display or window, then Resume.");
            string file = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
            try
            {
                Step step;
                if (target.IsDisplay)
                {
                    Rectangle r = target.Bounds;
                    if ((long)r.Width * r.Height > 64000000) throw new InvalidOperationException("This display is too large for this version.");
                    using (Bitmap bitmap = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb))
                    {
                        using (Graphics g = Graphics.FromImage(bitmap)) g.CopyFromScreen(r.Location, System.Drawing.Point.Empty, r.Size, CopyPixelOperation.SourceCopy);
                        // Timestamp immediately after pixel acquisition, before encoding/writing.
                        step = new Step { Captured = DateTimeOffset.Now, Mono = new SystemClock().Now, Sequence = Interlocked.Increment(ref sequence) };
                        token.ThrowIfCancellationRequested(); bitmap.Save(file, ImageFormat.Png);
                    }
                }
                else
                {
                    WindowChoice w = target.Window;
                    ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath, string.Format("--capture-worker {0} {1} {2} \"{3}\"", w.Handle.ToInt64(), w.Pid, w.ProcessStart, file)) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                    using (Process worker = Process.Start(info))
                    {
                        Stopwatch timeout = Stopwatch.StartNew();
                        while (!worker.WaitForExit(40))
                        {
                            if (token.IsCancellationRequested || timeout.ElapsedMilliseconds > 6000) { worker.Kill(); worker.WaitForExit(1000); token.ThrowIfCancellationRequested(); throw new InvalidOperationException("The window capture timed out."); }
                        }
                        if (worker.ExitCode != 0) throw new InvalidOperationException("The selected window could not be captured.");
                    }
                    using (BinaryReader reader = new BinaryReader(File.OpenRead(file + ".stamp")))
                        step = new Step { Captured = new DateTimeOffset(reader.ReadInt64(), TimeSpan.FromMinutes(reader.ReadInt32())), Mono = reader.ReadDouble(), Sequence = Interlocked.Increment(ref sequence) };
                }
                token.ThrowIfCancellationRequested(); step.File = file; step.Source = target.IsDisplay ? "Selected display " + target.DisplayName : "Selected application window (" + target.Window.App + ")"; return step;
            }
            catch { Files.Delete(file); throw; }
            finally { Files.Delete(file + ".stamp"); }
        }
        internal static int WindowWorker(string[] args)
        {
            WindowChoice w = new WindowChoice { Handle = new IntPtr(long.Parse(args[1])), Pid = uint.Parse(args[2]), ProcessStart = long.Parse(args[3]) };
            Native.Rect rect; if (!w.Available() || !Native.GetWindowRect(w.Handle, out rect)) return 3;
            Rectangle r = rect.Bounds; if (r.Width < 2 || r.Height < 2 || (long)r.Width * r.Height > 64000000) return 4;
            using (Bitmap bitmap = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb))
            {
                bool ok;
                using (Graphics g = Graphics.FromImage(bitmap)) { g.Clear(Color.Black); IntPtr dc = g.GetHdc(); try { ok = Native.PrintWindow(w.Handle, dc, 2); } finally { g.ReleaseHdc(dc); } }
                DateTimeOffset wall = DateTimeOffset.Now; double mono = new SystemClock().Now;
                if (!ok || !w.Available()) return 5;
                bitmap.Save(args[4], ImageFormat.Png);
                using (BinaryWriter writer = new BinaryWriter(File.Create(args[4] + ".stamp"))) { writer.Write(wall.Ticks); writer.Write((int)wall.Offset.TotalMinutes); writer.Write(mono); }
            }
            return 0;
        }
    }
    internal static class Preferences
    {
        private static string PathName { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SopRecorder", "capture-settings.txt"); } }
        internal static string Display = "";
        internal static int Delay = 250; internal static double Idle = 2;
        internal static void Load()
        {
            try { string[] lines = File.ReadAllLines(PathName); Display = lines[0]; Delay = Math.Max(0, Math.Min(2000, int.Parse(lines[1]))); Idle = Math.Max(.2, Math.Min(30, double.Parse(lines[2], System.Globalization.CultureInfo.InvariantCulture))); } catch { }
        }
        internal static void Save()
        { try { Directory.CreateDirectory(Path.GetDirectoryName(PathName)); File.WriteAllLines(PathName, new string[] { Display, Delay.ToString(), Idle.ToString(System.Globalization.CultureInfo.InvariantCulture) }); } catch { } }
    }
}
