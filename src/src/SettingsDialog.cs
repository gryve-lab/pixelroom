using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Pixelroom
{
    public sealed class SettingsDialog : Form
    {
        private readonly PixelDrainClient client;
        private readonly Action<string> changeTheme;
        private readonly Action<string, List<CloudFile>> connectAccount;
        private readonly Action disconnectAccount;
        private readonly Func<bool> canChangeAccount;
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly TextBox keyBox;
        private readonly Label connectionStatus, feedback;
        private readonly Button connect, disconnect;
        private readonly CheckBox remember;
        private bool connected, hasAccount, connecting, disposed;
        public SettingsDialog(PixelDrainClient client, string currentKey, bool isConnected, bool rememberKey, Action<string> changeTheme, Action<bool> changeRememberKey, Action<string, List<CloudFile>> connectAccount, Action disconnectAccount, Func<bool> canChangeAccount, bool embedded = false)
        {
            this.client = client; this.changeTheme = changeTheme; this.connectAccount = connectAccount; this.disconnectAccount = disconnectAccount; this.canChangeAccount = canChangeAccount; connected = isConnected; hasAccount = !String.IsNullOrEmpty(currentKey);
            Theme.StyleDialog(this); Text = "Settings — Pixelroom"; ClientSize = new Size(520, 640);
            Label title = Theme.Label("Settings", 24, Theme.Ink, FontStyle.Bold); title.SetBounds(27, 23, 460, 44); Controls.Add(title);
            Label appearance = Theme.Label("Appearance", 11, Theme.Ink, FontStyle.Bold); appearance.SetBounds(29, 88, 460, 26); Controls.Add(appearance);
            RadioButton gray = new ThemeOption("Gray") { Checked = Theme.Name == "Gray" }; gray.SetBounds(29, 125, 224, 135); Controls.Add(gray);
            RadioButton dark = new ThemeOption("Dark") { Checked = Theme.Name == "Dark" }; dark.SetBounds(267, 125, 224, 135); Controls.Add(dark);
            gray.CheckedChanged += delegate { if (gray.Checked) ChooseTheme("Gray"); }; dark.CheckedChanged += delegate { if (dark.Checked) ChooseTheme("Dark"); };
            Label keyLabel = Theme.Label("PixelDrain API key", 11, Theme.Ink, FontStyle.Bold); keyLabel.SetBounds(29, 296, 250, 26); Controls.Add(keyLabel);
            connectionStatus = Theme.Label(connected ? "Connected" : "Not connected", 8, Theme.Muted); connectionStatus.SetBounds(310, 299, 180, 22); connectionStatus.TextAlign = ContentAlignment.MiddleRight; Controls.Add(connectionStatus);
            FieldFrame keyFrame = new FieldFrame(); keyFrame.SetBounds(29, 336, 462, 40); Controls.Add(keyFrame);
            keyBox = new TextBox { Text = currentKey ?? "", UseSystemPasswordChar = true, Font = Theme.Font(11), MaxLength = 512, BorderStyle = BorderStyle.None, AccessibleName = "PixelDrain API Key" }; keyBox.SetBounds(12, 9, 438, 24); keyBox.Enter += delegate { keyFrame.Invalidate(); }; keyBox.Leave += delegate { keyFrame.Invalidate(); }; keyFrame.Controls.Add(keyBox);
            CheckBox show = new CheckBox { Text = "Show API key", AutoSize = true }; show.SetBounds(29, 388, 200, 25); show.CheckedChanged += delegate { keyBox.UseSystemPasswordChar = !show.Checked; }; Controls.Add(show);
            remember = new CheckBox { Text = "Remember API key", AutoSize = true, Checked = rememberKey }; remember.SetBounds(29, 418, 300, 25); Controls.Add(remember);
            Label rememberNote = Theme.Label("Encrypted for your Windows account. Connect automatically on startup.", 8, Theme.Muted); rememberNote.SetBounds(46, 446, 445, 30); Controls.Add(rememberNote);
            bool changingRemember = false;
            remember.CheckedChanged += delegate
            {
                if (changingRemember) return;
                try { changeRememberKey(remember.Checked); rememberKey = remember.Checked; feedback.Text = rememberKey ? (connected ? "Your API key is remembered." : "Your key will be remembered after connecting.") : "Saved API key removed."; }
                catch (Exception ex) { changingRemember = true; remember.Checked = rememberKey; changingRemember = false; feedback.Text = ex.Message; }
            };
            connect = Theme.Button(connected ? "Update key" : "Connect", true); connect.SetBounds(29, 485, 130, 38); connect.Click += Connect; Controls.Add(connect);
            disconnect = Theme.Button("Disconnect"); disconnect.SetBounds(170, 485, 130, 38); disconnect.Enabled = hasAccount; disconnect.Click += delegate
            {
                if (!canChangeAccount()) return;
                try { disconnectAccount(); connected = false; hasAccount = false; keyBox.Clear(); connectionStatus.Text = "Not connected"; connect.Text = "Connect"; disconnect.Enabled = false; feedback.Text = "Disconnected. Saved API key removed."; }
                catch (Exception ex) { feedback.Text = ex.Message; }
            }; Controls.Add(disconnect);
            feedback = Theme.Label("", 9, Theme.Ink); feedback.SetBounds(29, 534, 462, 43); Controls.Add(feedback);
            Button done = Theme.Button("Done"); done.SetBounds(374, 584, 117, 38); done.Click += delegate { Close(); }; Controls.Add(done); CancelButton = done;
            FormClosing += delegate { cancel.Cancel(); keyBox.Clear(); };
            if (embedded)
            {
                TopLevel = false; FormBorderStyle = FormBorderStyle.None; Dock = DockStyle.Fill; AutoScroll = true; title.Visible = false;
                foreach (Control child in Controls) if (child != title) child.Top -= 66;
                AutoScrollMinSize = new Size(520, 574);
            }
        }
        private void ChooseTheme(string name)
        {
            try { changeTheme(name); Theme.Apply(this); feedback.Text = ""; }
            catch (Exception) { feedback.Text = "The theme could not be saved."; }
        }
        private async void Connect(object sender, EventArgs e)
        {
            if (connecting) return; string key = keyBox.Text.Trim();
            if (key.Length == 0) { feedback.Text = "Enter your PixelDrain API key."; keyBox.Focus(); return; }
            if (!canChangeAccount()) return;
            connecting = true; connect.Enabled = false; disconnect.Enabled = false; keyBox.Enabled = false; remember.Enabled = false; connect.Text = "Connecting…"; feedback.Text = "";
            try
            {
                List<CloudFile> files = await client.ListAsync(key, cancel.Token);
                if (cancel.IsCancellationRequested || IsDisposed) return;
                connectAccount(key, files); connected = true; hasAccount = true; connectionStatus.Text = "Connected"; feedback.Text = remember.Checked ? "Connected. Your API key is remembered." : "Connected for this session.";
            }
            catch (OperationCanceledException) { if (!cancel.IsCancellationRequested && !IsDisposed) feedback.Text = "The connection timed out. Please try again."; }
            catch (Exception ex) { if (!IsDisposed) feedback.Text = ex.Message; }
            finally { if (!IsDisposed) { connecting = false; connect.Enabled = true; disconnect.Enabled = hasAccount; keyBox.Enabled = true; remember.Enabled = true; connect.Text = connected ? "Update key" : "Connect"; } }
        }
        protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; cancel.Cancel(); cancel.Dispose(); } base.Dispose(disposing); }
    }
}
