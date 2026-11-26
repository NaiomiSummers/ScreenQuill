using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace SopRecorder
{
    internal sealed class TextViewer : Form
    {
        private readonly TextBox text = new TextBox();
        internal TextViewer(string[] args)
        {
            string reply = args[1], nonce = args[2], testFile = args.Length > 5 ? args[5] : null;
            Text = "SOP text viewer"; Size = new Size(700, 550); StartPosition = FormStartPosition.Manual; Location = new Point(int.Parse(args[3]), int.Parse(args[4]));
            text.Multiline = true; text.ReadOnly = true; text.ScrollBars = ScrollBars.Both; text.WordWrap = false; text.Dock = DockStyle.Fill; text.Font = new Font("Consolas", 12); Controls.Add(text);
            Shown += delegate
            {
                try
                {
                    if (testFile != null && args.Length > 6) Thread.Sleep(int.Parse(args[6]));
                    string file = testFile;
                    if (file == null)
                        using (OpenFileDialog dialog = new OpenFileDialog { Title = "Open a local text file with verified SOP evidence", Filter = "Plain text files (*.txt)|*.txt", CheckFileExists = true, Multiselect = false })
                        { if (dialog.ShowDialog(this) != DialogResult.OK) { Close(); return; } file = dialog.FileName; }
                    string location;
                    using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (stream.Length > 1024 * 1024) throw new InvalidOperationException("This viewer supports text files up to 1 MB.");
                        StringBuilder path = new StringBuilder(32768); uint count = Native.GetFinalPathNameByHandle(stream.SafeFileHandle.DangerousGetHandle(), path, (uint)path.Capacity, 0);
                        if (count == 0 || count >= path.Capacity) throw new InvalidOperationException("Windows could not verify this file's location.");
                        location = path.ToString(); if (location.StartsWith(@"\\?\UNC\")) throw new InvalidOperationException("Use a local file for this verified workflow.");
                        if (location.StartsWith(@"\\?\")) location = location.Substring(4);
                        if (location.StartsWith(@"\\") || new DriveInfo(Path.GetPathRoot(location)).DriveType == DriveType.Network) throw new InvalidOperationException("Use a local file for this workflow.");
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true)) text.Text = reader.ReadToEnd();
                    }
                    Text = "SOP text viewer — " + Path.GetFileName(location); text.SelectionStart = 0; text.SelectionLength = 0; Update();
                    DateTimeOffset wall = DateTimeOffset.Now; double mono = new SystemClock().Now;
                    // One-use, local metadata handoff. No file content is copied. Revoking the permit cancels tracking.
                    if (!File.Exists(reply + ".permit")) return;
                    using (BinaryWriter writer = new BinaryWriter(new FileStream(reply + ".pending", FileMode.CreateNew)))
                    { writer.Write(nonce); writer.Write(Process.GetCurrentProcess().Id); writer.Write(wall.Ticks); writer.Write((int)wall.Offset.TotalMinutes); writer.Write(mono); writer.Write(Handle.ToInt64()); writer.Write(location); }
                    File.Move(reply + ".pending", reply);
                    if (!File.Exists(reply + ".permit")) Files.Delete(reply);
                }
                catch (Exception ex)
                {
                    if (testFile != null) { File.WriteAllText(testFile + ".viewer-error.txt", ex.ToString()); Close(); return; }
                    MessageBox.Show(this, "File evidence was not recorded: " + ex.Message + "\nEnter the path manually during review if needed.", "SOP text viewer");
                    if (string.IsNullOrEmpty(text.Text)) Close();
                }
            };
        }
    }
    internal sealed class FileOpenTracker
    {
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        internal int LastViewerPid;
        internal void Begin() { cancellation.Cancel(); cancellation = new CancellationTokenSource(); }
        internal void Stop() { cancellation.Cancel(); }
        internal async Task<Step> Open(CaptureTarget target, string directory, string testFile, int testDelay = 0)
        {
            CancellationToken token = cancellation.Token; token.ThrowIfCancellationRequested();
            string reply = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".file-open"), nonce = Guid.NewGuid().ToString("N");
            File.WriteAllText(reply + ".permit", "");
            try
            {
                Rectangle bounds = target.IsDisplay ? target.Bounds : Screen.PrimaryScreen.Bounds;
                string args = "--text-viewer \"" + reply + "\" " + nonce + " " + (bounds.Left + 30) + " " + (bounds.Top + 30);
                if (testFile != null) args += " \"" + testFile + "\" " + testDelay;
                using (Process viewer = Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { UseShellExecute = false }))
                {
                    LastViewerPid = viewer.Id; Stopwatch timeout = Stopwatch.StartNew();
                    while (!File.Exists(reply))
                    {
                        await Task.Delay(60, token);
                        if (viewer.HasExited) return null;
                        if (testFile != null && timeout.ElapsedMilliseconds > 12000) throw new InvalidOperationException("The test viewer did not return file evidence within 12 seconds.");
                    }
                    token.ThrowIfCancellationRequested();
                    using (BinaryReader reader = new BinaryReader(File.OpenRead(reply)))
                    {
                        if (reader.ReadString() != nonce || reader.ReadInt32() != viewer.Id) throw new InvalidDataException("Unrecognized file evidence.");
                        DateTimeOffset wall = new DateTimeOffset(reader.ReadInt64(), TimeSpan.FromMinutes(reader.ReadInt32())); double mono = reader.ReadDouble(); IntPtr hwnd = new IntPtr(reader.ReadInt64()); string location = reader.ReadString();
                        uint owner; Native.GetWindowThreadProcessId(hwnd, out owner);
                        if (owner != viewer.Id || location.Length > 32768 || !Path.IsPathRooted(location)) throw new InvalidDataException("Invalid file evidence.");
                        Native.Rect rect;
                        if (!target.IsDisplay || !Native.GetWindowRect(hwnd, out rect) || !target.Bounds.Contains((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2)) return null;
                        return new Step { Captured = wall, Mono = mono, ReviewedPath = location, PathEvidence = "Verified open: explicit text-viewer open; location from the handle that supplied displayed content", Source = "Local SOP text viewer", Reason = "File opened", Note = "Opened this local text file in the evidence viewer." };
                    }
                }
            }
            finally { Files.Delete(reply + ".permit"); Files.Delete(reply + ".pending"); Files.Delete(reply); }
        }
    }
}
