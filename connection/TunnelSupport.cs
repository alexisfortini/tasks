using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;

internal static class TunnelSupport {
  internal static readonly string Root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
  internal static readonly string Profile = Path.Combine(Root, "connection", "profiles", "google-tasks.yaml");
  internal static readonly string Executable = Path.Combine(Root, "tools", "tunnel-client-v0.0.15", "tunnel-client.exe");
  internal static readonly string KeyFile = Path.Combine(Root, ".private", "tunnel-runtime", "key.dpapi");
  internal static readonly string EnvironmentFile = Path.Combine(Root, ".env");

  internal static string Quote(string value) { return "\"" + value + "\""; }
  internal static bool ValidKey(string key) {
    return key != null && key.StartsWith("sk-", StringComparison.Ordinal) && key.Length >= 20 && key.Length <= 4096 && key.IndexOfAny(new char[] {' ', '\t', '\r', '\n', '\0'}) < 0;
  }
  internal static Mutex Instance(string scope = null) {
    string digest;
    using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(scope ?? Root.ToUpperInvariant()))).Replace("-", "");
    bool created;
    var mutex = new Mutex(true, "Local\\GoogleTasksTunnel-" + digest, out created);
    if (!created) { mutex.Dispose(); return null; }
    return mutex;
  }
  internal static void SaveKey(string target, string key) {
    if (!ValidKey(key)) throw new InvalidOperationException();
    string directory = Path.GetDirectoryName(target);
    Directory.CreateDirectory(directory);
    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException();
    var identity = WindowsIdentity.GetCurrent().User;
    var acl = new DirectorySecurity();
    acl.SetAccessRuleProtection(true, false);
    acl.SetOwner(identity);
    acl.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
      InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
    Directory.SetAccessControl(directory, acl);
    byte[] plain = Encoding.UTF8.GetBytes(key);
    string temp = Path.Combine(directory, Guid.NewGuid().ToString() + ".tmp");
    try {
      byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
      using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
        file.Write(cipher, 0, cipher.Length); file.Flush(true);
      }
      if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
    } finally { Array.Clear(plain, 0, plain.Length); if (File.Exists(temp)) File.Delete(temp); }
  }
  internal static string LoadKey(string target) {
    byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(target), null, DataProtectionScope.CurrentUser);
    try { string key = Encoding.UTF8.GetString(plain); if (!ValidKey(key)) throw new InvalidOperationException(); return key; }
    finally { Array.Clear(plain, 0, plain.Length); }
  }
  internal static bool IsKeyAssignment(string line) {
    line = line.Trim().TrimStart('\uFEFF');
    if (line.StartsWith("export ", StringComparison.Ordinal)) line = line.Substring(7).TrimStart();
    int equal = line.IndexOf('=');
    return equal >= 0 && line.Substring(0, equal).Trim() == "CONTROL_PLANE_API_KEY";
  }
  internal static string EnvironmentKey(string content) {
    if (content.Length > 65536) throw new InvalidOperationException();
    string key = null; bool found = false;
    foreach (string raw in content.Split('\n')) {
      string line = raw.Trim().TrimStart('\uFEFF');
      if (!IsKeyAssignment(line)) continue;
      if (found) throw new InvalidOperationException();
      found = true;
      string value = line.Substring(line.IndexOf('=') + 1).Trim();
      if (value.Length == 0 || value[0] == '#') continue;
      if (value[0] == '\'' || value[0] == '"') {
        int end = value.IndexOf(value[0], 1);
        if (end < 0) throw new InvalidOperationException();
        string tail = value.Substring(end + 1).Trim();
        if (tail.Length > 0 && tail[0] != '#') throw new InvalidOperationException();
        value = value.Substring(1, end - 1);
      } else {
        int comment = value.IndexOf('#');
        if (comment >= 0) value = value.Substring(0, comment).TrimEnd();
      }
      if (value.Length == 0) continue;
      if (!ValidKey(value)) throw new InvalidOperationException();
      key = value;
    }
    return key;
  }
  internal static string LoadRuntimeKey() {
    string key = Environment.GetEnvironmentVariable("CONTROL_PLANE_API_KEY");
    if (!String.IsNullOrEmpty(key)) { if (!ValidKey(key)) throw new InvalidOperationException(); return key; }
    if (File.Exists(EnvironmentFile)) {
      key = EnvironmentKey(File.ReadAllText(EnvironmentFile));
      if (key != null) return key;
    }
    return LoadKey(KeyFile);
  }
  internal static void SaveEnvironmentKey(string target, string key) {
    if (!ValidKey(key)) throw new InvalidOperationException();
    string content = File.Exists(target) ? File.ReadAllText(target) : "";
    EnvironmentKey(content); // Reject duplicate or malformed existing key entries.
    var lines = new System.Collections.Generic.List<string>(content.Replace("\r\n", "\n").Split('\n'));
    int index = lines.FindIndex(IsKeyAssignment);
    if (index < 0) lines.Add("CONTROL_PLANE_API_KEY=" + key); else lines[index] = "CONTROL_PLANE_API_KEY=" + key;
    if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException();
    var identity = WindowsIdentity.GetCurrent().User;
    var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(identity);
    acl.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
    if (File.Exists(target)) File.SetAccessControl(target, acl);
    string temp = Path.Combine(Path.GetDirectoryName(target), ".env-" + Guid.NewGuid().ToString() + ".tmp");
    byte[] plain = Encoding.UTF8.GetBytes(String.Join("\n", lines).TrimEnd('\n') + "\n");
    try {
      using (var file = new FileStream(temp, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.WriteThrough, acl)) {
        file.Write(plain, 0, plain.Length); file.Flush(true);
      }
      if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
    } finally { Array.Clear(plain, 0, plain.Length); if (File.Exists(temp)) File.Delete(temp); }
  }
  internal static ProcessStartInfo StartInfo(string executable, string arguments, string key) {
    var start = new ProcessStartInfo(executable, arguments) {
      UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Root,
      RedirectStandardOutput = true, RedirectStandardError = true
    };
    start.EnvironmentVariables.Remove("OPENAI_API_KEY");
    start.EnvironmentVariables.Remove("OPENAI_ADMIN_KEY");
    start.EnvironmentVariables.Remove("GOOGLE_TASKS_ACCOUNT_EMAIL");
    start.EnvironmentVariables["CONTROL_PLANE_API_KEY"] = key;
    start.EnvironmentVariables["LOG_HTTP_RAW_UNSAFE"] = "false";
    start.EnvironmentVariables["LOG_FILE"] = "stdout";
    return start;
  }

  // Closing the job also terminates tunnel-client's stdio server descendants.
  // This prevents orphaned clients when Task Scheduler stops/restarts the wrapper.
  internal sealed class ChildJob : IDisposable {
    IntPtr handle;
    [StructLayout(LayoutKind.Sequential)] struct Basic {
      public long ProcessTime, JobTime; public uint Flags;
      public UIntPtr Minimum, Maximum; public uint Active; public UIntPtr Affinity;
      public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] struct Io {
      public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)] struct Limits {
      public Basic Basic; public Io Io;
      public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int kind, IntPtr data, uint length);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    internal ChildJob() {
      handle = CreateJobObject(IntPtr.Zero, null);
      if (handle == IntPtr.Zero) throw new InvalidOperationException();
      var limits = new Limits(); limits.Basic.Flags = 0x2000;
      int size = Marshal.SizeOf(limits); IntPtr data = Marshal.AllocHGlobal(size);
      try { Marshal.StructureToPtr(limits, data, false); if (!SetInformationJobObject(handle, 9, data, (uint)size)) { Dispose(); throw new InvalidOperationException(); } }
      finally { Marshal.FreeHGlobal(data); }
    }
    internal void Add(Process process) { if (!AssignProcessToJobObject(handle, process.Handle)) throw new InvalidOperationException(); }
    public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
  }
  internal static int Run(string executable, string arguments, string key, int timeoutMilliseconds) {
    var start = StartInfo(executable, arguments, key);
    using (var job = new ChildJob())
    using (var child = Process.Start(start)) {
      start.EnvironmentVariables.Remove("CONTROL_PLANE_API_KEY");
      try { job.Add(child); }
      catch { try { child.Kill(); } catch { } throw; }
      // Drain without storing or displaying request data or credential diagnostics.
      child.OutputDataReceived += delegate { }; child.ErrorDataReceived += delegate { };
      child.BeginOutputReadLine(); child.BeginErrorReadLine();
      if (!child.WaitForExit(timeoutMilliseconds)) return 124;
      child.WaitForExit(); return child.ExitCode;
    }
  }
}
