using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace SopRecorder
{
    internal sealed class TargetPicker : Form
    {
        internal CaptureTarget Choice;
        internal readonly ListBox List = new ListBox();
        internal readonly Button Use = new Button { Text = "Use selection", Dock = DockStyle.Fill };
        internal TargetPicker(CaptureTarget previous)
        {
            Text = "Choose a display or optional application window"; Size = new Size(800, 490); StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 11);
            TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 3, ColumnCount = 1 };
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            grid.Controls.Add(new Label { Text = "A display follows you across apps. Single-window mode captures only that window.\nIf a display disconnects, recording pauses; it will not silently switch displays.", Dock = DockStyle.Fill }, 0, 0);
            List.Dock = DockStyle.Fill; List.HorizontalScrollbar = true; List.IntegralHeight = false; grid.Controls.Add(List, 0, 1);
            foreach (Screen s in Screen.AllScreens) List.Items.Add(new Item(CaptureTarget.FromScreen(s), "DISPLAY" + (s.Primary ? " (primary)" : "") + " — " + s.DeviceName + " — " + s.Bounds.Width + " × " + s.Bounds.Height));
            foreach (WindowChoice w in WindowChoice.List()) List.Items.Add(new Item(new CaptureTarget { Window = w }, "WINDOW — " + w));
            for (int i = 0; i < List.Items.Count; i++) { Item item = (Item)List.Items[i]; if (item.Target.IsDisplay && previous.IsDisplay && item.Target.DisplayName == previous.DisplayName) List.SelectedIndex = i; }
            Use.Click += delegate { if (List.SelectedItem != null) { Choice = ((Item)List.SelectedItem).Target; DialogResult = DialogResult.OK; } };
            List.DoubleClick += delegate { Use.PerformClick(); }; grid.Controls.Add(Use, 0, 2); Controls.Add(grid); AcceptButton = Use;
        }
        internal sealed class Item { internal CaptureTarget Target; private string text; internal Item(CaptureTarget t, string text) { Target = t; this.text = text; } public override string ToString() { return text; } }
    }
    internal sealed class RecorderForm : Form
    {
        internal Session Session;
        internal CaptureEngine Engine;
        internal CaptureTarget Target;
        internal readonly Button Record = new Button(), Pause = new Button(), Stop = new Button(), Manual = new Button(), Pick = new Button(), OpenFile = new Button(), Export = new Button(), Up = new Button(), Down = new Button(), Remove = new Button(), Undo = new Button();
        internal readonly TextBox TaskName = new TextBox(), Goal = new TextBox(), Begins = new TextBox(), Done = new TextBox(), Note = new TextBox(), PathEntry = new TextBox();
        internal readonly CheckBox Reviewed = new CheckBox();
        internal readonly ListBox Steps = new ListBox();
        internal readonly PictureBox Preview = new PictureBox();
        internal readonly Label StateLabel = new Label(), Timing = new Label(), Feedback = new Label(), TargetLabel = new Label(), StepLabel = new Label();
        internal readonly NumericUpDown Delay = new NumericUpDown(), Idle = new NumericUpDown();
        internal readonly TabControl Tabs = new TabControl();
        internal bool Dirty, HooksAvailable; internal int HookError;
        internal Task PendingFile = Task.FromResult(0);
        private InputHooks hooks;
        private readonly FileOpenTracker tracker = new FileOpenTracker();
        private readonly Timer timer = new Timer();
        private bool changing, closing;
        private int epoch;
        private Step removed; private int removedIndex;
        private readonly string testDirectory;
        internal RecorderForm(string testDirectory)
        {
            this.testDirectory = testDirectory; if (testDirectory == null) Preferences.Load(); Target = CaptureTarget.Default(Preferences.Display);
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            Text = "SOP Recorder"; Size = new Size(Math.Min(1180, workArea.Width - 30), Math.Min(920, workArea.Height - 30)); MinimumSize = new Size(900, 640); StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(244, 247, 249);
            TableLayoutPanel root = Grid(1, 7); root.Padding = new Padding(20, 14, 20, 12);
            foreach (int h in new int[] { 54, 58, 44, 44 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, h)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            TableLayoutPanel header = Grid(2, 1); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            header.Controls.Add(new Label { Text = "SOP Recorder", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 21, FontStyle.Bold), ForeColor = Color.FromArgb(24, 65, 77) }, 0, 0);
            StateLabel.Dock = DockStyle.Fill; StateLabel.TextAlign = ContentAlignment.MiddleRight; StateLabel.Font = new Font("Segoe UI", 16, FontStyle.Bold); header.Controls.Add(StateLabel, 1, 0); root.Controls.Add(header, 0, 0);
            FlowLayoutPanel transport = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            ButtonSetup(Record, "Record Screen", StartRecording, true); Record.AccessibleName = "Start recording selected display";
            ButtonSetup(Pause, "Pause", TogglePause, false); ButtonSetup(Stop, "Stop and review", StopRecording, false); ButtonSetup(Manual, "Capture now", CaptureNow, false);
            Button summary = new Button(); ButtonSetup(summary, "Timing...", ShowTiming, false);
            Button termsButton = new Button(); ButtonSetup(termsButton, "Terms", delegate { TermsConsent.Review(this); }, false);
            transport.Controls.AddRange(new Control[] { Record, Pause, Stop, Manual, summary, termsButton }); root.Controls.Add(transport, 0, 1);
            TableLayoutPanel targetRow = Grid(2, 1); targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            TargetLabel.Dock = DockStyle.Fill; TargetLabel.TextAlign = ContentAlignment.MiddleLeft; TargetLabel.AutoEllipsis = true; ButtonSetup(Pick, "Choose target...", PickTarget, false); Pick.AutoSize = false; Pick.Padding = Padding.Empty; Pick.Margin = new Padding(2, 0, 6, 0); Pick.Dock = DockStyle.Fill;
            targetRow.Controls.Add(TargetLabel, 0, 0); targetRow.Controls.Add(Pick, 1, 0); root.Controls.Add(targetRow, 0, 2);
            FlowLayoutPanel settings = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            settings.Controls.Add(new Label { Text = "Click / Tab delay (ms)", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
            Delay.Minimum = 0; Delay.Maximum = 2000; Delay.Increment = 50; Delay.Value = Preferences.Delay; Delay.Width = 75; settings.Controls.Add(Delay);
            settings.Controls.Add(new Label { Text = "New typing burst after idle (s)", AutoSize = true, Padding = new Padding(14, 8, 0, 0) });
            Idle.Minimum = .2m; Idle.Maximum = 30; Idle.DecimalPlaces = 1; Idle.Increment = .5m; Idle.Value = (decimal)Preferences.Idle; Idle.Width = 70; settings.Controls.Add(Idle);
            Timing.AutoSize = true; Timing.Padding = new Padding(15, 8, 0, 0); settings.Controls.Add(Timing); root.Controls.Add(settings, 0, 3);
            Tabs.Dock = DockStyle.Fill; TabPage review = new TabPage("Steps and review") { Padding = new Padding(12), BackColor = Color.White }, overview = new TabPage("Task details (optional)") { Padding = new Padding(18), BackColor = Color.White }; Tabs.TabPages.Add(review); Tabs.TabPages.Add(overview); root.Controls.Add(Tabs, 0, 4);
            TableLayoutPanel body = Grid(1, 3); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            body.Controls.Add(new Label { Text = "Record once, then work across apps. Clicks, typing bursts, and Tab changes create steps.\nPause stops all triggers. Add notes and review paths before sharing the ZIP with your AI agent.", Dock = DockStyle.Fill }, 0, 0);
            TableLayoutPanel split = Grid(2, 1); split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            TableLayoutPanel left = Grid(1, 3); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            left.Controls.Add(new Label { Text = "Retained steps", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) }, 0, 0); Steps.Dock = DockStyle.Fill; Steps.IntegralHeight = false; Steps.HorizontalScrollbar = true; left.Controls.Add(Steps, 0, 1);
            TableLayoutPanel actions = Grid(2, 2); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.RowStyles.Clear(); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            ButtonSetup(Up, "Move up", delegate { Move(-1); }, false); ButtonSetup(Down, "Move down", delegate { Move(1); }, false); ButtonSetup(Remove, "Remove", RemoveStep, false); ButtonSetup(Undo, "Undo", UndoRemove, false);
            int n = 0; foreach (Button b in new Button[] { Up, Down, Remove, Undo }) { b.AutoSize = false; b.Dock = DockStyle.Fill; b.Padding = new Padding(0); actions.Controls.Add(b, n % 2, n / 2); n++; } left.Controls.Add(actions, 0, 2); split.Controls.Add(left, 0, 0);
            TableLayoutPanel right = Grid(1, 5); right.Padding = new Padding(10, 0, 0, 0); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            StepLabel.Dock = DockStyle.Fill; StepLabel.AutoEllipsis = true; right.Controls.Add(StepLabel, 0, 0);
            Preview.Dock = DockStyle.Fill; Preview.SizeMode = PictureBoxSizeMode.Zoom; Preview.BackColor = Color.FromArgb(228, 234, 237); Preview.DoubleClick += delegate { Enlarge(); }; right.Controls.Add(Preview, 0, 1);
            right.Controls.Add(new Label { Text = "Note: what happened, why, or what to check", Dock = DockStyle.Fill }, 0, 2);
            Note.Multiline = true; Note.ScrollBars = ScrollBars.Vertical; Note.Dock = DockStyle.Fill; Note.AccessibleName = "Step note"; right.Controls.Add(Note, 0, 3);
            TableLayoutPanel pathRow = Grid(1, 2); pathRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); pathRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); pathRow.Controls.Add(new Label { Text = "File location: edit or clear (does not redact image pixels)", Dock = DockStyle.Fill }, 0, 0);
            PathEntry.Dock = DockStyle.Fill; PathEntry.AccessibleName = "Reviewed file location"; pathRow.Controls.Add(PathEntry, 0, 1); right.Controls.Add(pathRow, 0, 4); split.Controls.Add(right, 1, 0); body.Controls.Add(split, 0, 1);
            TableLayoutPanel exportRow = Grid(2, 1); exportRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); exportRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); Reviewed.Text = "I reviewed retained images, notes, paths, and optional task details."; Reviewed.Dock = DockStyle.Fill; ButtonSetup(Export, "Export ZIP...", ExportDialog, true); Export.Dock = DockStyle.Fill; exportRow.Controls.Add(Reviewed, 0, 0); exportRow.Controls.Add(Export, 1, 0); body.Controls.Add(exportRow, 0, 2);
            Panel reviewScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; body.Dock = DockStyle.Top; body.Height = 440; reviewScroll.Controls.Add(body);
            reviewScroll.Resize += delegate { body.Height = Math.Max(440, reviewScroll.ClientSize.Height); }; review.Controls.Add(reviewScroll);
            TableLayoutPanel fields = Grid(1, 10); fields.AutoScroll = true; fields.AutoScrollMinSize = new Size(0, 490); fields.RowStyles.Clear();
            TextBox[] values = { TaskName, Goal, Begins, Done }; string[] labels = { "Task name (optional)", "Goal (optional)", "Where the work begins (optional)", "What done looks like (optional)" };
            for (int i = 0; i < values.Length; i++)
            { fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 0 ? 38 : 72)); fields.Controls.Add(new Label { Text = labels[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, i * 2); values[i].Dock = DockStyle.Fill; values[i].Multiline = i != 0; values[i].ScrollBars = i == 0 ? ScrollBars.None : ScrollBars.Vertical; fields.Controls.Add(values[i], 0, i * 2 + 1); }
            ButtonSetup(OpenFile, "Open text file with verified evidence...", delegate { PendingFile = OpenTracked(null); }, false); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); fields.Controls.Add(OpenFile, 0, 8); fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fields.Controls.Add(new Label { Text = "Verified workflow: open a local .txt file in the SOP text viewer while recording a display.\nOther apps: add the file path manually to a step during review.", Dock = DockStyle.Fill }, 0, 9); overview.Controls.Add(fields);
            Feedback.Dock = DockStyle.Fill; Feedback.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(Feedback, 0, 5);
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft }; LinkLabel fresh = new LinkLabel { Text = "New task", AutoSize = true }; fresh.LinkClicked += delegate { NewTask(); }; footer.Controls.Add(fresh); footer.Controls.Add(new Label { Text = "Local only | No uploads | Export before closing", AutoSize = true }); root.Controls.Add(footer, 0, 6); Controls.Add(root);
            SetupSession(new WindowsCapture());
            Steps.SelectedIndexChanged += delegate { if (!changing) ShowStep(); };
            Note.TextChanged += delegate { Step s = Selected(); if (!changing && s != null) { s.Note = Note.Text; Changed(); } };
            PathEntry.TextChanged += delegate { Step s = Selected(); if (!changing && s != null) { s.ReviewedPath = PathEntry.Text; s.PathEvidence = string.IsNullOrWhiteSpace(PathEntry.Text) ? "" : "Manually entered or edited; not independently verified"; Changed(); } };
            foreach (TextBox field in values) field.TextChanged += delegate { if (!changing) Changed(); };
            Reviewed.CheckedChanged += delegate { UpdateControls(); };
            timer.Interval = 500; timer.Tick += delegate { if (Session.State == SessionState.Recording && !Target.Available()) { PauseRecording(); Say("Target unavailable. Choose an available display/window and Resume.", true); } UpdateControls(); }; timer.Start();
            Shown += async delegate { if (testDirectory != null) await IntegrationTests.Run(this, testDirectory); };
            FormClosing += async delegate(object sender, FormClosingEventArgs e)
            {
                if (closing) return;
                if (testDirectory == null && Dirty && MessageBox.Show(this, "Discard unexported changes and close?", "SOP Recorder", MessageBoxButtons.YesNo) != DialogResult.Yes) { e.Cancel = true; return; }
                e.Cancel = true; closing = true; StopTriggers(); timer.Stop(); await Engine.Drain(); ClearPreview(); Session.Dispose(); BeginInvoke(new Action(Close));
            };
            Say("Ready. Click Record Screen, then work normally. Task details can wait until review.", false); UpdateControls();
        }
        private static TableLayoutPanel Grid(int columns, int rows) { TableLayoutPanel p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows }; if (rows == 1) p.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); return p; }
        private static void ButtonSetup(Button button, string text, Action action, bool primary)
        { button.Text = text; button.AutoSize = true; button.Height = 40; button.Padding = new Padding(8, 3, 8, 3); button.Margin = new Padding(2, 4, 6, 4); button.FlatStyle = FlatStyle.Flat; button.BackColor = primary ? Color.FromArgb(26, 103, 119) : Color.White; button.ForeColor = primary ? Color.White : Color.FromArgb(24, 65, 77); button.Click += delegate { action(); }; }
        internal void SetupSession(IFrameSource source)
        {
            Session = new Session(new SystemClock()); Engine = new CaptureEngine(Session, source); Engine.Target = Target;
            Engine.Completed = delegate(Step s) { Changed(); RefreshSteps(s.Id); Say("Captured " + s.Reason.ToLowerInvariant() + ". " + Session.Steps.Count + " retained steps.", false); };
            Engine.Failed = delegate(string error) { PauseRecording(); Say(error + " Recording paused.", true); };
            Engine.Changed = delegate { if (!IsDisposed) UpdateControls(); };
        }
        internal void Say(string text, bool error) { Feedback.Text = text; Feedback.ForeColor = error ? Color.FromArgb(151, 47, 32) : Color.FromArgb(30, 87, 71); }
        private void Changed() { Dirty = true; Reviewed.Checked = false; UpdateControls(); }
        internal void StartRecording()
        {
            if (Session.State == SessionState.Recording || Session.State == SessionState.Paused) return;
            if (!Target.Available()) { Say("Select an available display/window first.", true); return; }
            ReadOverview(); Session.Start(); ActivateTriggers(); Changed();
            if (testDirectory == null) { if (Target.IsDisplay) Preferences.Display = Target.DisplayName; Preferences.Delay = (int)Delay.Value; Preferences.Idle = (double)Idle.Value; Preferences.Save(); }
            if (testDirectory == null && HooksAvailable) { WindowState = FormWindowState.Minimized; Engine.Request("Recording started", 300, false); }
            UpdateControls();
        }
        private void ActivateTriggers()
        {
            Engine.Target = Target; Engine.Begin(); tracker.Begin(); int current = ++epoch;
            hooks = new InputHooks(Target, (double)Idle.Value, delegate(string reason)
            {
                try { BeginInvoke(new Action(delegate { if (testDirectory == null && current == epoch && Session.State == SessionState.Recording) Engine.Request(reason, reason == "Typing burst" ? 0 : (int)Delay.Value, true); })); } catch { }
            });
            HooksAvailable = hooks.Available; HookError = hooks.Error;
            if (!HooksAvailable) Say("Automatic triggers unavailable (Windows error " + HookError + "). Recording is MANUAL ONLY: use Capture now. Automatic recording needs a normal unlocked Windows desktop.", true);
            else Say("Recording. Clicks, typing bursts and Tab changes capture automatically. Return here to Pause or Stop.", false);
        }
        private void StopTriggers() { epoch++; Engine.Cancel(); tracker.Stop(); if (hooks != null) { hooks.Dispose(); hooks = null; } }
        internal void PauseRecording() { if (Session.State != SessionState.Recording) return; Session.Pause(); StopTriggers(); UpdateControls(); }
        internal void TogglePause()
        {
            if (Session.State == SessionState.Recording) { PauseRecording(); Say("PAUSED. Screenshot triggers and file-open tracking are off; pending captures were canceled.", false); }
            else if (Session.State == SessionState.Paused)
            { if (!Target.Available()) { Say("Choose an available display/window before resuming.", true); return; } Session.Start(); ActivateTriggers(); if (testDirectory == null && HooksAvailable) WindowState = FormWindowState.Minimized; }
            UpdateControls();
        }
        internal void StopRecording() { Session.Stop(); StopTriggers(); WindowState = FormWindowState.Normal; Say("Stopped. Review retained screenshots, notes and paths, then confirm review and export.", false); RefreshSteps(Guid.Empty); UpdateControls(); }
        internal void CaptureNow()
        { if (Session.State != SessionState.Recording) return; if (testDirectory == null && Target.IsDisplay) WindowState = FormWindowState.Minimized; Engine.Request("Manual capture", testDirectory == null ? 250 : 0, false); }
        internal void SelectTarget(CaptureTarget target)
        { if (Session.State == SessionState.Recording) PauseRecording(); Target = target; Engine.Target = target; if (target.IsDisplay && testDirectory == null) { Preferences.Display = target.DisplayName; Preferences.Delay = (int)Delay.Value; Preferences.Idle = (double)Idle.Value; Preferences.Save(); } UpdateControls(); }
        private void PickTarget()
        { if (Session.State == SessionState.Recording) PauseRecording(); using (TargetPicker picker = new TargetPicker(Target)) if (picker.ShowDialog(this) == DialogResult.OK) SelectTarget(picker.Choice); }
        internal async Task OpenTracked(string testFile, int testDelay = 0)
        {
            if (Session.State != SessionState.Recording || !Target.IsDisplay) return; int current = epoch;
            try { Step step = await tracker.Open(Target, Session.DirectoryPath, testFile, testDelay); if (step != null && current == epoch && Session.State == SessionState.Recording) { Session.Add(step); Changed(); RefreshSteps(step.Id); Say("Verified file-open evidence added. Review its path before export.", false); } }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Say("File location was not verified: " + ex.Message + ". Enter it manually in review. Screen capture is still available.", true); }
        }
        internal Step Selected() { return Steps.SelectedIndex >= 0 && Steps.SelectedIndex < Session.Steps.Count ? Session.Steps[Steps.SelectedIndex] : null; }
        internal void RefreshSteps(Guid selected)
        {
            if (IsDisposed) return; if (selected == Guid.Empty && Selected() != null) selected = Selected().Id;
            changing = true; Steps.BeginUpdate(); Steps.Items.Clear();
            foreach (Step s in Session.Steps) Steps.Items.Add((s.HasImage ? "Image " : "File ") + (Session.Steps.IndexOf(s) + 1) + " · " + s.Captured.ToString("HH:mm:ss") + " · " + s.Reason);
            int index = Session.Steps.FindIndex(s => s.Id == selected); if (Session.Steps.Count > 0) Steps.SelectedIndex = index >= 0 ? index : Session.Steps.Count - 1; Steps.EndUpdate(); changing = false; ShowStep(); UpdateControls();
        }
        private void ClearPreview() { Image image = Preview.Image; Preview.Image = null; if (image != null) image.Dispose(); }
        private void ShowStep()
        {
            changing = true; ClearPreview(); Step s = Selected(); Note.Text = s == null ? "" : s.Note; PathEntry.Text = s == null ? "" : s.ReviewedPath;
            StepLabel.Text = s == null ? "Your screenshots will appear here" : s.Stamp + " · " + s.Reason;
            if (s != null && s.HasImage) try { Preview.Image = Exporter.WithFooter(s, Session); } catch (Exception ex) { Say("Preview failed: " + ex.Message, true); }
            changing = false; UpdateControls();
        }
        private void Enlarge()
        { if (Preview.Image == null) return; if (Session.State == SessionState.Recording) PauseRecording(); using (Form view = new Form { Text = StepLabel.Text, Size = new Size(1100, 780), StartPosition = FormStartPosition.CenterParent }) { PictureBox image = new PictureBox { Dock = DockStyle.Fill, Image = Preview.Image, SizeMode = PictureBoxSizeMode.Zoom }; view.Controls.Add(image); view.ShowDialog(this); image.Image = null; } }
        internal new void Move(int delta)
        { int index = Steps.SelectedIndex, next = index + delta; if (Session.State == SessionState.Recording || Engine.Pending != 0 || index < 0 || next < 0 || next >= Session.Steps.Count) return; Step s = Session.Steps[index]; Session.Steps.RemoveAt(index); Session.Steps.Insert(next, s); Changed(); RefreshSteps(s.Id); }
        internal void RemoveStep()
        { if (Selected() == null || Session.State == SessionState.Recording || Engine.Pending != 0) return; removedIndex = Steps.SelectedIndex; removed = Selected(); Session.Steps.RemoveAt(removedIndex); Changed(); RefreshSteps(Guid.Empty); }
        private void UndoRemove() { if (removed == null || Session.State == SessionState.Recording) return; Session.Steps.Insert(Math.Min(removedIndex, Session.Steps.Count), removed); Guid id = removed.Id; removed = null; Changed(); RefreshSteps(id); }
        internal void ReadOverview() { Session.Name = TaskName.Text.Trim(); Session.Goal = Goal.Text.Trim(); Session.Begins = Begins.Text.Trim(); Session.Done = Done.Text.Trim(); }
        internal void ExportTo(string file) { ReadOverview(); Exporter.Write(Session, file, Reviewed.Checked); Dirty = false; Say("Saved " + file + ". Give this reviewed ZIP to your existing AI agent.", false); }
        private void ExportDialog()
        {
            try { using (SaveFileDialog save = new SaveFileDialog { Filter = "ZIP (*.zip)|*.zip", DefaultExt = "zip", AddExtension = true, FileName = "SOP-evidence-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", OverwritePrompt = true }) if (save.ShowDialog(this) == DialogResult.OK) ExportTo(save.FileName); }
            catch (Exception ex) { Say("Export failed: " + ex.Message, true); }
        }
        private void ShowTiming() { if (Session.State == SessionState.Recording) PauseRecording(); using (Form form = new Form { Text = "Time between captures (can include idle time)", Size = new Size(800, 600), StartPosition = FormStartPosition.CenterParent }) { form.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Text = Durations.Summary(Session).Replace("\n", "\r\n"), Font = Font }); form.ShowDialog(this); } }
        internal void UpdateControls()
        {
            if (Session == null || closing) return; SessionState state = Session.State;
            StateLabel.Text = state == SessionState.Recording ? (HooksAvailable ? "● RECORDING" : "● RECORDING — MANUAL ONLY") : state == SessionState.Paused ? "Ⅱ PAUSED" : state == SessionState.Review ? "✓ STOPPED · REVIEW" : "READY";
            StateLabel.ForeColor = state == SessionState.Recording ? Color.Firebrick : Color.FromArgb(24, 91, 105);
            Timing.Text = "Elapsed " + Durations.Live(Session.Elapsed) + " · Active " + Durations.Live(Session.Active);
            TargetLabel.Text = (Target.IsDisplay ? "Full display across apps: " : "Optional window mode: ") + Target.Label;
            Record.Text = state == SessionState.Review ? "Record more" : Target.IsDisplay ? "Record Screen" : "Record Window"; Record.Enabled = (state == SessionState.Ready || state == SessionState.Review) && Engine.Pending == 0;
            Pause.Text = state == SessionState.Paused ? "Resume" : "Pause"; Pause.Enabled = state == SessionState.Recording || state == SessionState.Paused; Stop.Enabled = state == SessionState.Recording || state == SessionState.Paused; Manual.Enabled = state == SessionState.Recording;
            Delay.Enabled = Idle.Enabled = state != SessionState.Recording; Pick.Enabled = Engine.Pending == 0; OpenFile.Enabled = state == SessionState.Recording && Target.IsDisplay;
            bool edit = state != SessionState.Recording && Engine.Pending == 0; Up.Enabled = edit && Steps.SelectedIndex > 0; Down.Enabled = edit && Steps.SelectedIndex >= 0 && Steps.SelectedIndex < Session.Steps.Count - 1; Remove.Enabled = edit && Selected() != null; Undo.Enabled = edit && removed != null;
            Note.Enabled = PathEntry.Enabled = Selected() != null; Reviewed.Enabled = state == SessionState.Review && Engine.Pending == 0 && Session.Steps.Count > 0; Export.Enabled = Reviewed.Enabled && Reviewed.Checked;
        }
        private void NewTask()
        {
            if (Engine.Pending != 0) { Say("Pause or Stop and wait for pending work to finish first.", true); return; }
            if (Dirty && MessageBox.Show(this, "Discard unexported changes and start a new task?", "SOP Recorder", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            StopTriggers(); ClearPreview(); Session.Dispose(); SetupSession(new WindowsCapture()); removed = null; changing = true; foreach (TextBox field in new TextBox[] { TaskName, Goal, Begins, Done, Note, PathEntry }) field.Clear(); changing = false; Dirty = false; Reviewed.Checked = false; RefreshSteps(Guid.Empty); UpdateControls();
        }
        internal void SavePreview(string path) { using (Bitmap b = new Bitmap(Width, Height)) { DrawToBitmap(b, new Rectangle(0, 0, Width, Height)); b.Save(path); } }
        internal int ViewerPid { get { return tracker.LastViewerPid; } }
    }
}
