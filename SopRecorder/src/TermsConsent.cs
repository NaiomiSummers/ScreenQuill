using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
namespace SopRecorder
{
    internal sealed class TermsAgreement
    {
        internal readonly string Version, Text, Hash;
        internal TermsAgreement(string version, string text)
        {
            Version = version; Text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            using (SHA256 sha = SHA256.Create()) Hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Text))).Replace("-", "").ToLowerInvariant();
        }
        internal static TermsAgreement Bundled()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenQuill.terms"))
            {
                if (stream == null) throw new InvalidDataException("The bundled ScreenQuill agreement is missing.");
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string text = reader.ReadToEnd();
                    Match version = Regex.Match(text, @"(?m)^Version (\d+(?:\.\d+)*) \|");
                    if (!version.Success) throw new InvalidDataException("The bundled agreement version is missing.");
                    return new TermsAgreement(version.Groups[1].Value, text);
                }
            }
        }
    }
    internal sealed class TermsReceipt
    {
        private readonly string filename;
        internal TermsReceipt(string filename) { this.filename = filename; }
        internal bool HasAccepted(TermsAgreement agreement)
        {
            try { return File.ReadAllText(filename, Encoding.UTF8) == agreement.Version + "\n" + agreement.Hash + "\n"; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        internal void Accept(TermsAgreement agreement)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filename));
            string temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, agreement.Version + "\n" + agreement.Hash + "\n", new UTF8Encoding(false));
                if (File.Exists(filename)) File.Replace(temporary, filename, null); else File.Move(temporary, filename);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    internal sealed class TermsDialog : Form
    {
        internal readonly Button Agree = new Button(), Decline = new Button();
        internal readonly TextBox AgreementText = new TextBox();
        internal TermsDialog(TermsAgreement agreement, TermsReceipt receipt, bool required)
        {
            Text = "ScreenQuill Terms of Service"; StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(820, 700); MinimumSize = new Size(550, 450); Font = new Font("Segoe UI", 10);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.Controls.Add(new Label { Text = "Acceptance applies to this official build. It does not limit rights granted by the MIT source-code license or any separate component license.\nPublisher-approved agreement " + agreement.Version, Dock = DockStyle.Fill }, 0, 0);
            AgreementText.Multiline = true; AgreementText.ReadOnly = true; AgreementText.ScrollBars = ScrollBars.Vertical;
            AgreementText.Dock = DockStyle.Fill; AgreementText.Text = agreement.Text.Replace("\n", Environment.NewLine); AgreementText.AccessibleName = "Full Terms of Service";
            layout.Controls.Add(AgreementText, 0, 1);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
            Agree.Text = required ? "I agree" : "Close"; Agree.AutoSize = true;
            Decline.Text = "Decline"; Decline.AutoSize = true; Decline.Visible = required;
            Agree.Click += delegate {
                try { if (required) receipt.Accept(agreement); DialogResult = DialogResult.OK; Close(); }
                catch (Exception error) { MessageBox.Show(this, "Could not save agreement: " + error.Message, Text); }
            };
            Decline.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            actions.Controls.Add(Agree); actions.Controls.Add(Decline); layout.Controls.Add(actions, 0, 2); Controls.Add(layout);
            CancelButton = Decline; AcceptButton = Agree;
        }
    }
    internal static class TermsConsent
    {
        internal static readonly TermsAgreement Agreement = TermsAgreement.Bundled();
        private static TermsReceipt Receipt { get { return new TermsReceipt(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenQuill", "terms-acceptance.txt")); } }
        internal static bool Ensure()
        {
            TermsReceipt receipt = Receipt;
            if (receipt.HasAccepted(Agreement)) return true;
            using (TermsDialog dialog = new TermsDialog(Agreement, receipt, true)) return dialog.ShowDialog() == DialogResult.OK;
        }
        internal static void Review(IWin32Window owner)
        {
            using (TermsDialog dialog = new TermsDialog(Agreement, Receipt, false)) dialog.ShowDialog(owner);
        }
    }
}
