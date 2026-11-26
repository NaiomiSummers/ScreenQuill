using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace SopRecorder
{
    // Raw key identities live only in this short-lived repeat-suppression set. No text conversion, log, clipboard access or serialization.
    internal sealed class TriggerPolicy
    {
        private readonly HashSet<int> held = new HashSet<int>();
        private double lastTyping = double.NaN;
        internal double Idle = 2;
        internal void SeedHeld(int key) { held.Add(key); }
        internal void Reset() { held.Clear(); lastTyping = double.NaN; }
        internal string Key(int key, bool down, bool scope, double now)
        {
            if (!down) { held.Remove(key); return null; }
            if (!held.Add(key)) return null;
            if (!scope || Modifier(key)) return null;
            bool shortcut = held.Any(k => k == 17 || k == 18 || k == 91 || k == 92 || (k >= 162 && k <= 165));
            if (shortcut) return null;
            if (key == 9) return "Focus change"; // Tab/Shift+Tab never reads or changes lastTyping.
            bool typing = key == 8 || key == 13 || key == 32 || (key >= 48 && key <= 90) || (key >= 96 && key <= 111) || (key >= 186 && key <= 226) || key == 229;
            if (!typing) return null;
            bool burst = double.IsNaN(lastTyping) || now - lastTyping >= Idle;
            lastTyping = now; return burst ? "Typing burst" : null;
        }
        private static bool Modifier(int key) { return key == 16 || key == 17 || key == 18 || key == 91 || key == 92 || (key >= 160 && key <= 165); }
    }
    internal sealed class InputHooks : IDisposable
    {
        private Thread thread;
        private Control dispatcher;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private IntPtr keyboard, mouse;
        private Native.HookProc keyboardCallback, mouseCallback;
        private readonly TriggerPolicy policy = new TriggerPolicy();
        private volatile bool enabled;
        private volatile bool disposed;
        private readonly Dictionary<int, Native.Point> clicks = new Dictionary<int, Native.Point>();
        internal bool Available; internal int Error;
        internal InputHooks(CaptureTarget target, double idle, Action<string> trigger)
        {
            thread = new Thread(delegate()
            {
                using (dispatcher = new Control())
                {
                    IntPtr handle = dispatcher.Handle;
                    policy.Idle = idle;
                    // LL hooks report left/right modifiers, not the generic aliases; seeding aliases could leave a phantom Ctrl/Alt held after Resume.
                    for (int key = 1; key < 256; key++) if (key != 16 && key != 17 && key != 18 && (Native.GetAsyncKeyState(key) & 0x8000) != 0) policy.SeedHeld(key);
                    keyboardCallback = delegate(int code, IntPtr message, IntPtr data)
                    {
                        try
                        {
                            if (code >= 0 && enabled)
                            {
                                int msg = message.ToInt32(); bool down = msg == 0x100 || msg == 0x104;
                                if (down || msg == 0x101 || msg == 0x105)
                                {
                                    int key = Marshal.ReadInt32(data);
                                    string reason = policy.Key(key, down, down && target.KeyboardInScope(), new SystemClock().Now);
                                    if (reason != null) trigger(reason);
                                }
                            }
                        }
                        catch { /* Never interrupt the user's input or log the raw event. */ }
                        return Native.CallNextHookEx(keyboard, code, message, data);
                    };
                    mouseCallback = delegate(int code, IntPtr message, IntPtr data)
                    {
                        try
                        {
                            if (code >= 0 && enabled)
                            {
                                int msg = message.ToInt32();
                                int button = msg == 0x201 || msg == 0x202 ? 1 : msg == 0x204 || msg == 0x205 ? 2 : msg == 0x207 || msg == 0x208 ? 3 : msg == 0x20B || msg == 0x20C ? 4 : 0;
                                if (button != 0)
                                {
                                    Native.MouseData m = (Native.MouseData)Marshal.PtrToStructure(data, typeof(Native.MouseData));
                                    bool down = msg == 0x201 || msg == 0x204 || msg == 0x207 || msg == 0x20B;
                                    if (down) { if (target.PointInScope(m.Point)) clicks[button] = m.Point; else clicks.Remove(button); }
                                    else
                                    {
                                        Native.Point start;
                                        if (clicks.TryGetValue(button, out start) && Math.Abs(start.X - m.Point.X) <= 8 && Math.Abs(start.Y - m.Point.Y) <= 8 && target.PointInScope(m.Point)) trigger("Mouse click");
                                        clicks.Remove(button);
                                    }
                                }
                            }
                        }
                        catch { }
                        return Native.CallNextHookEx(mouse, code, message, data);
                    };
                    keyboard = Native.SetWindowsHookEx(13, keyboardCallback, Native.GetModuleHandle(null), 0);
                    if (keyboard == IntPtr.Zero) Error = Marshal.GetLastWin32Error();
                    mouse = Native.SetWindowsHookEx(14, mouseCallback, Native.GetModuleHandle(null), 0);
                    if (mouse == IntPtr.Zero) Error = Marshal.GetLastWin32Error();
                    Available = keyboard != IntPtr.Zero && mouse != IntPtr.Zero && !disposed; enabled = Available; ready.Set();
                    if (Available && !disposed) Application.Run();
                    if (keyboard != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboard);
                    if (mouse != IntPtr.Zero) Native.UnhookWindowsHookEx(mouse);
                    policy.Reset(); clicks.Clear();
                }
            });
            thread.IsBackground = true; thread.Name = "SOP transient input triggers"; thread.SetApartmentState(ApartmentState.STA); thread.Start();
            if (!ready.WaitOne(2000)) { disposed = true; enabled = false; Error = 1460; }
        }
        public void Dispose()
        {
            disposed = true; enabled = false;
            try { if (dispatcher != null && dispatcher.IsHandleCreated) dispatcher.BeginInvoke(new Action(Application.ExitThread)); } catch { }
            if (thread != null && thread.IsAlive) thread.Join(1000);
        }
    }
    internal sealed class CaptureEngine
    {
        private readonly Session session;
        private readonly IFrameSource source;
        private readonly SemaphoreSlim acquisition = new SemaphoreSlim(1, 1);
        private readonly List<Task> tasks = new List<Task>();
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        internal CaptureTarget Target;
        internal int Pending;
        internal Action<Step> Completed;
        internal Action<string> Failed;
        internal Action Changed;
        internal CaptureEngine(Session session, IFrameSource source) { this.session = session; this.source = source; }
        internal void Begin() { cancellation.Cancel(); cancellation = new CancellationTokenSource(); }
        internal void Cancel() { cancellation.Cancel(); }
        internal Task Drain() { return Task.WhenAll(tasks.ToArray()); }
        internal void Request(string reason, int delay, bool requireExternalFocus)
        {
            if (session.State != SessionState.Recording) return;
            tasks.RemoveAll(x => x.IsCompleted); Task task = Run(reason, delay, requireExternalFocus, cancellation.Token, Target); tasks.Add(task);
        }
        private async Task Run(string reason, int delay, bool requireExternalFocus, CancellationToken token, CaptureTarget target)
        {
            Pending++; if (Changed != null) Changed(); Step frame = null;
            try
            {
                if (delay > 0) await Task.Delay(delay, token);
                token.ThrowIfCancellationRequested();
                await acquisition.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    // If the user returned to recorder controls during a delay, don't capture those controls.
                    if (requireExternalFocus && Native.Own(Native.GetForegroundWindow())) return;
                    frame = await Task.Run(delegate { return source.Take(target, session.DirectoryPath, token); }, token);
                }
                finally { acquisition.Release(); }
                token.ThrowIfCancellationRequested();
                if (session.State != SessionState.Recording) return;
                frame.Reason = reason; session.Add(frame); if (Completed != null) Completed(frame); frame = null;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!token.IsCancellationRequested && Failed != null) Failed(ex.Message); }
            finally { if (frame != null) Files.Delete(frame.File); Pending--; if (Changed != null) Changed(); }
        }
    }
}
