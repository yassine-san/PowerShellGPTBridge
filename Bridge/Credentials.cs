using System.Security.Cryptography;
using System.Text.Json;
namespace PersistentBridge;
internal sealed record Credential(string Username, string Method, string Secret, string KeyPath, bool Save);
internal static class CredentialStore
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersistentPSBridge");
    private static string FileFor(string target) => Path.Combine(Root, "credential-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(target))) + ".bin");
    public static Credential? Load(string target) { try { var bytes = ProtectedData.Unprotect(File.ReadAllBytes(FileFor(target)), null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<Credential>(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); } } catch { return null; } }
    public static void Save(string target, Credential credential) { if (!credential.Save) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credential);
        try { File.WriteAllBytes(FileFor(target), ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); } }
    public static void Forget(string target) { File.Delete(FileFor(target)); }
    public static Task<Credential?> Prompt(Window window, string target, CancellationToken token, bool sudo = false) => window.OnUi<Credential?>(() => {
        using var form = new Form { Text = sudo ? "Local sudo credential — " + target : "SSH credentials — " + target,
            Width = 580, Height = 360, TopMost = true, StartPosition = FormStartPosition.CenterParent };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new(12) };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 130)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        var username = new TextBox { Dock = DockStyle.Fill }; var method = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        method.Items.AddRange(["password", "private-key"]); method.SelectedIndex = 0;
        var secret = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
        var path = new TextBox { Dock = DockStyle.Fill }; var choose = new Button { Text = "Choose private key…", AutoSize = true };
        choose.Click += (_, _) => { using var open = new OpenFileDialog(); if (open.ShowDialog(form) == DialogResult.OK) path.Text = open.FileName; };
        var save = new CheckBox { Text = "Save encrypted for this Windows user", AutoSize = true, Enabled = !sudo };
        layout.Controls.Add(new Label { Text = "SSH username" }, 0, 0); layout.Controls.Add(username, 1, 0);
        layout.Controls.Add(new Label { Text = "Authentication" }, 0, 1); layout.Controls.Add(method, 1, 1);
        layout.Controls.Add(new Label { Text = "Password/passphrase" }, 0, 2); layout.Controls.Add(secret, 1, 2);
        layout.Controls.Add(choose, 0, 3); layout.Controls.Add(path, 1, 3); layout.Controls.Add(save, 1, 4);
        if (sudo) { username.Enabled = false; method.Enabled = false; path.Enabled = false; choose.Enabled = false; }
        var row = new FlowLayoutPanel { AutoSize = true }; var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "Use credential", DialogResult = DialogResult.OK, AutoSize = true }; row.Controls.AddRange([cancel, ok]); layout.Controls.Add(row, 1, 5);
        form.Controls.Add(layout); form.AcceptButton = cancel; form.CancelButton = cancel;
        using var timer = new System.Windows.Forms.Timer { Interval = 100 }; timer.Tick += (_, _) => { if (token.IsCancellationRequested) form.DialogResult = DialogResult.Cancel; }; timer.Start();
        if (form.ShowDialog(window) != DialogResult.OK || token.IsCancellationRequested || (!sudo && string.IsNullOrWhiteSpace(username.Text))) return null;
        return new Credential(username.Text, method.SelectedItem!.ToString()!, secret.Text, path.Text, save.Checked);
    }, null, token);
}
