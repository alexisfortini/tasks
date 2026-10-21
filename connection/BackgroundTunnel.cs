using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class BackgroundTunnel {
  static readonly string StatusPath = Path.Combine(TunnelSupport.Root, "connection", "background-status.txt");
  static void Status(string code) {
    try { File.WriteAllText(StatusPath, DateTime.UtcNow.ToString("o") + " " + code + Environment.NewLine); } catch { }
  }
  static int ConfigureKey(bool environmentFile) {
    Application.EnableVisualStyles();
    using (var form = new Form())
    using (var label = new Label())
    using (var input = new TextBox())
    using (var save = new Button()) {
      form.Text = "Google Tasks tunnel startup key";
      form.ClientSize = new System.Drawing.Size(520, 175);
      form.StartPosition = FormStartPosition.CenterScreen;
      form.FormBorderStyle = FormBorderStyle.FixedDialog; form.MaximizeBox = false; form.MinimizeBox = false;
      label.Text = environmentFile ? "Enter your Restricted OpenAI runtime key (Tunnels Read + Use).\r\nSave as plaintext in the Git-ignored .env, accessible only to your Windows account." : "Enter your existing personal Restricted OpenAI runtime key (Tunnels Read + Use).\r\nWindows will encrypt it for your account so the tunnel can start at sign-in.";
      label.SetBounds(16, 16, 488, 50);
      input.UseSystemPasswordChar = true; input.MaxLength = 4096; input.SetBounds(16, 80, 488, 25);
      save.Text = environmentFile ? "Save local .env key" : "Save encrypted key"; save.SetBounds(324, 125, 180, 30);
      int result = 1;
      save.Click += delegate {
        if (!TunnelSupport.ValidKey(input.Text)) { MessageBox.Show(form, "Enter a valid runtime key."); return; }
        try {
          if (environmentFile) TunnelSupport.SaveEnvironmentKey(TunnelSupport.EnvironmentFile, input.Text);
          else TunnelSupport.SaveKey(TunnelSupport.KeyFile, input.Text);
          input.Clear(); result = 0; form.Close();
        }
        catch { MessageBox.Show(form, "The key could not be saved. No key was logged."); }
      };
      form.Controls.Add(label); form.Controls.Add(input); form.Controls.Add(save); form.AcceptButton = save;
      form.ShowDialog(); return result;
    }
  }
  static int SelfTest() {
    string directory = Path.Combine(Path.GetTempPath(), "tasks-background-" + Guid.NewGuid().ToString());
    string target = Path.Combine(directory, "key.dpapi");
    const string dummy = "sk-" + "NONSECRET_OFFLINE_DUMMY";
    try {
      TunnelSupport.SaveKey(target, dummy);
      if (TunnelSupport.LoadKey(target) != dummy || Encoding.UTF8.GetString(File.ReadAllBytes(target)).Contains(dummy)) return 1;
      TunnelSupport.SaveKey(target, dummy + "2");
      if (TunnelSupport.LoadKey(target) != dummy + "2") return 2;
      var acl = Directory.GetAccessControl(directory);
      var rules = acl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
      if (!acl.AreAccessRulesProtected || rules.Count != 1 || !rules[0].IdentityReference.Equals(System.Security.Principal.WindowsIdentity.GetCurrent().User)) return 3;
      if (TunnelSupport.ValidKey("invalid") || TunnelSupport.ValidKey(dummy + "\n")) return 4;
      foreach (string text in new string[] {"CONTROL_PLANE_API_KEY=" + dummy, "export CONTROL_PLANE_API_KEY='" + dummy + "' # comment", "CONTROL_PLANE_API_KEY=\"" + dummy + "\"\r\nOTHER=value"}) {
        if (TunnelSupport.EnvironmentKey(text) != dummy) return 8;
      }
      if (TunnelSupport.EnvironmentKey("CONTROL_PLANE_API_KEY=\n") != null) return 9;
      bool rejected = false;
      try { TunnelSupport.EnvironmentKey("CONTROL_PLANE_API_KEY=" + dummy + "\nCONTROL_PLANE_API_KEY=" + dummy); } catch { rejected = true; }
      if (!rejected) return 10;
      string env = Path.Combine(directory, ".env");
      File.WriteAllText(env, "GOOGLE_TASKS_ACCOUNT_EMAIL=personal@example.com\nCONTROL_PLANE_API_KEY=\n");
      TunnelSupport.SaveEnvironmentKey(env, dummy);
      if (TunnelSupport.EnvironmentKey(File.ReadAllText(env)) != dummy || !File.ReadAllText(env).Contains("GOOGLE_TASKS_ACCOUNT_EMAIL=personal@example.com")) return 11;
      var fileAcl = File.GetAccessControl(env);
      var fileRules = fileAcl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
      if (!fileAcl.AreAccessRulesProtected || fileRules.Count != 1 || !fileRules[0].IdentityReference.Equals(System.Security.Principal.WindowsIdentity.GetCurrent().User)) return 12;
      string scope = "self-test-" + Guid.NewGuid().ToString();
      using (var mutex = TunnelSupport.Instance(scope)) { if (mutex == null) return 5; using (var duplicate = TunnelSupport.Instance(scope)) { if (duplicate != null) return 6; } }
      return TunnelSupport.Run(Process.GetCurrentProcess().MainModule.FileName, "--self-test-child", dummy, 10000) == 23 ? 0 : 7;
    } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
  }
  [STAThread] static int Main(string[] args) {
    try {
      if (args.Length == 1 && args[0] == "--self-test-child") {
        return Environment.GetEnvironmentVariable("CONTROL_PLANE_API_KEY") == "sk-" + "NONSECRET_OFFLINE_DUMMY" && Environment.GetEnvironmentVariable("OPENAI_API_KEY") == null && Environment.GetEnvironmentVariable("OPENAI_ADMIN_KEY") == null ? 23 : 1;
      }
      if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
      if (args.Length == 1 && args[0] == "--configure-key") return ConfigureKey(false);
      if (args.Length == 1 && args[0] == "--configure-env-key") return ConfigureKey(true);
      if (args.Length == 1 && args[0] == "--check-key") { TunnelSupport.LoadRuntimeKey(); return 0; }
      if (args.Length != 0) return 2;
      using (var mutex = TunnelSupport.Instance()) {
        if (mutex == null) return 0;
        if (!File.Exists(TunnelSupport.Profile) || !File.Exists(TunnelSupport.Executable)) { Status("CONFIGURATION_REQUIRED"); return 2; }
        int delay = 5000;
        for (;;) {
          string key;
          try { key = TunnelSupport.LoadRuntimeKey(); }
          catch { Status("RUNTIME_KEY_REQUIRED"); Thread.Sleep(60000); continue; }
          var started = DateTime.UtcNow;
          try {
            Status("TUNNEL_STARTING");
            string arguments = "run --profile-file " + TunnelSupport.Quote(TunnelSupport.Profile) +
              " --log.level error --log.http-raw-unsafe=false --admin-ui.log-buffer-events 1 --allow-remote-ui=false --open-web-ui=false --health.listen-addr 127.0.0.1:0" +
              " --health.url-file " + TunnelSupport.Quote(Path.Combine(TunnelSupport.Root, "connection", "health.url")) +
              " --pid.file " + TunnelSupport.Quote(Path.Combine(TunnelSupport.Root, "connection", "foreground.pid"));
            TunnelSupport.Run(TunnelSupport.Executable, arguments, key, -1);
          } catch { }
          finally { key = null; }
          if ((DateTime.UtcNow - started).TotalMinutes >= 2) delay = 5000;
          Status("TUNNEL_RESTART_PENDING"); Thread.Sleep(delay); delay = Math.Min(delay * 2, 60000);
        }
      }
    } catch { Status("BACKGROUND_START_FAILED"); return 1; }
  }
}
