using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// After every successful player build, pushes the build to another machine running LocalSend
/// (Tools > LocalSend Build Sender to configure).
///
/// Speaks LocalSend's HTTP protocol v2 directly: prepare-upload (the receiver shows its accept
/// prompt here, unless Quick Save is on), then one upload per file. Folders are sent as relative
/// paths inside a timestamped top folder, so every build lands as its own directory on the
/// receiver instead of LocalSend renaming files that collide with the previous build.
///
/// Transport is Windows' own curl.exe, not HttpClient. LocalSend 1.18+ is TLS 1.3-only and demands
/// a client certificate, and UnityTLS fails that handshake ("Cannot request client certificate
/// before receiving one from the server"). Schannel only manages it with a CNG key it can sign
/// RSA-PSS with, which means a certificate living in the user store (a PFX file with a legacy CSP
/// key is rejected by the server). So one self-signed cert, "LocalSend Unity Sender", is created
/// in CurrentUser\My once and reused; its SHA-256 is our stable LocalSend fingerprint.
///
/// The receiver's certificate is self-signed, so its public key is pinned on first contact and
/// curl refuses (exit 90) any other key afterwards.
/// </summary>
public class LocalSendBuildSender : EditorWindow, IPostprocessBuildWithReport
{
    const string k_Prefix = "LocalSendBuildSender.";
    const int k_DefaultPort = 53317;
    const int k_ParallelUploads = 4;
    const string k_CertName = "LocalSend Unity Sender";

    // Unity writes these next to the player; they must not ship.
    static readonly string[] k_SkipFolderSuffixes =
    {
        "_BurstDebugInformation_DoNotShip", "_BackUpThisFolder_ButDontShipItWithYourGame",
    };

    static readonly string k_Curl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");

    static bool Enabled { get => EditorPrefs.GetBool(k_Prefix + "Enabled", true); set => EditorPrefs.SetBool(k_Prefix + "Enabled", value); }
    static string Host { get => EditorPrefs.GetString(k_Prefix + "Host", ""); set => EditorPrefs.SetString(k_Prefix + "Host", value.Trim()); }
    static int Port { get => EditorPrefs.GetInt(k_Prefix + "Port", k_DefaultPort); set => EditorPrefs.SetInt(k_Prefix + "Port", value); }
    static string Pin { get => EditorPrefs.GetString(k_Prefix + "Pin", ""); set => EditorPrefs.SetString(k_Prefix + "Pin", value.Trim()); }
    static string PublicKeyPin { get => EditorPrefs.GetString(k_Prefix + "PublicKeyPin", ""); set => EditorPrefs.SetString(k_Prefix + "PublicKeyPin", value); }
    static string LastBuildPath { get => EditorPrefs.GetString(k_Prefix + "LastBuild", ""); set => EditorPrefs.SetString(k_Prefix + "LastBuild", value); }

    static volatile bool s_Busy;
    static string s_Status = "";
    static (string thumbprint, string fingerprint)? s_ClientCert;

    // ---------------------------------------------------------------- build hook

    public int callbackOrder => int.MaxValue;

    public void OnPostprocessBuild(BuildReport report)
    {
        string source = ResolveSendPath(report.summary.outputPath, report.summary.platform);
        if (source == null) return;
        LastBuildPath = source;

        if (!Enabled || Application.isBatchMode) return;
        if (string.IsNullOrEmpty(Host))
        {
            Debug.LogWarning("[LocalSend] Build not sent: no receiver set. Open Tools > LocalSend Build Sender.");
            return;
        }
        // The report is not final yet and the Editor is still inside the build; start once it returns.
        EditorApplication.delayCall += () => StartSend(source);
    }

    static string ResolveSendPath(string outputPath, BuildTarget target)
    {
        if (string.IsNullOrEmpty(outputPath)) return null;
        if (Directory.Exists(outputPath)) return outputPath;   // macOS .app, Xcode/Gradle projects, WebGL
        if (!File.Exists(outputPath)) return null;
        switch (target)
        {
            // The exe is useless without its _Data folder and runtime DLLs next to it.
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
            case BuildTarget.StandaloneLinux64:
                return Path.GetDirectoryName(outputPath);
            default:
                return outputPath;                              // .apk, .aab, ...
        }
    }

    // ---------------------------------------------------------------- window

    [MenuItem("Tools/LocalSend Build Sender")]
    static void Open() => GetWindow<LocalSendBuildSender>("LocalSend").minSize = new Vector2(380, 250);

    void OnGUI()
    {
        EditorGUILayout.Space();
        Enabled = EditorGUILayout.Toggle("Send after every build", Enabled);
        string host = EditorGUILayout.TextField(new GUIContent("Receiver address", "Laptop IP or hostname. LocalSend shows it on the Receive tab (tap the i)."), Host);
        if (host.Trim() != Host) { Host = host; PublicKeyPin = ""; }
        Port = EditorGUILayout.IntField("Port", Port);
        Pin = EditorGUILayout.TextField(new GUIContent("PIN (optional)", "Only if the receiver has 'Require PIN' on."), Pin);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            string pin = PublicKeyPin;
            EditorGUILayout.LabelField("Pinned receiver key", string.IsNullOrEmpty(pin) ? "(pinned on first connection)" : pin.Substring(8, 16) + "…");
            if (!string.IsNullOrEmpty(pin) && GUILayout.Button("Forget", GUILayout.Width(60))) PublicKeyPin = "";
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(s_Busy || string.IsNullOrEmpty(Host)))
        {
            if (GUILayout.Button("Test connection")) TestConnection();
            bool haveLast = !string.IsNullOrEmpty(LastBuildPath) && (Directory.Exists(LastBuildPath) || File.Exists(LastBuildPath));
            using (new EditorGUI.DisabledScope(!haveLast))
                if (GUILayout.Button(haveLast ? "Send last build now (" + Path.GetFileName(LastBuildPath) + ")" : "Send last build now")) StartSend(LastBuildPath);
        }

        if (!string.IsNullOrEmpty(s_Status)) EditorGUILayout.HelpBox(s_Status, MessageType.None);
        EditorGUILayout.HelpBox("Each build asks for acceptance on the laptop. To skip that, turn on Quick Save in LocalSend on the laptop (for favourites, then add this PC as a favourite).", MessageType.Info);
    }

    void OnInspectorUpdate() => Repaint();

    static async void TestConnection()
    {
        s_Busy = true;
        s_Status = "Connecting…";
        try
        {
            var client = await CreateClientAsync();
            string info = await client.GetInfoAsync(CancellationToken.None);
            s_Status = "Connected to \"" + JsonString(info, "alias") + "\" (" + client.BaseUrl + ")";
            Debug.Log("[LocalSend] " + s_Status);
        }
        catch (Exception e) { s_Status = "Failed: " + e.Message; Debug.LogError("[LocalSend] Test failed: " + e.Message); }
        finally { s_Busy = false; }
    }

    // ---------------------------------------------------------------- sending

    static async void StartSend(string source)
    {
        if (s_Busy) { Debug.LogWarning("[LocalSend] A transfer is already running; skipped."); return; }
        s_Busy = true;

        var files = CollectFiles(source);
        long total = files.Sum(f => f.size);
        var cts = new CancellationTokenSource();
        int progressId = Progress.Start("LocalSend", "Sending " + Path.GetFileName(source) + " to " + Host, Progress.Options.Managed);
        Progress.RegisterCancelCallback(progressId, () => { cts.Cancel(); return true; });

        LocalSendClient client = null;
        string sessionId = null;
        try
        {
            client = await CreateClientAsync();
            await client.GetInfoAsync(cts.Token);   // pins the receiver on first contact, fails fast if it's offline

            Progress.SetDescription(progressId, "Waiting for the laptop to accept…");
            s_Status = "Waiting for " + Host + " to accept " + files.Count + " files (" + EditorUtility.FormatBytes(total) + ")…";

            var session = await client.PrepareUploadAsync(files, cts.Token);
            if (session == null) { Finish("Receiver already has these files.", progressId, Progress.Status.Succeeded); return; }
            sessionId = session.Value.sessionId;
            var tokens = session.Value.tokens;

            Progress.SetDescription(progressId, "Uploading…");
            s_Status = "Uploading to " + Host + "…";
            var perFile = new long[files.Count];
            var watch = Stopwatch.StartNew();
            using (var gate = new SemaphoreSlim(k_ParallelUploads))
            {
                var uploads = files.Select((f, i) => (f, i)).Where(x => tokens.ContainsKey(x.f.id)).Select(async x =>
                {
                    await gate.WaitAsync(cts.Token);
                    try
                    {
                        await client.UploadAsync(sessionId, x.f, tokens[x.f.id],
                            fraction => Interlocked.Exchange(ref perFile[x.i], (long)(fraction * x.f.size)), cts.Token);
                        Interlocked.Exchange(ref perFile[x.i], x.f.size);
                    }
                    finally { gate.Release(); }
                }).ToList();

                // Progress is reported from here, on the main thread, rather than from curl's reader threads.
                var all = Task.WhenAll(uploads);
                while (!all.IsCompleted)
                {
                    long sent = 0;
                    for (int i = 0; i < perFile.Length; i++) sent += Interlocked.Read(ref perFile[i]);
                    Progress.Report(progressId, total == 0 ? 1f : (float)((double)sent / total));
                    await Task.WhenAny(all, Task.Delay(200));
                }
                await all;
            }

            double mbps = total / 1048576.0 / Math.Max(watch.Elapsed.TotalSeconds, 0.001);
            Finish($"Sent {Path.GetFileName(source)} ({EditorUtility.FormatBytes(total)}) to {Host} in {watch.Elapsed.TotalSeconds:0.0}s, {mbps:0.0} MB/s.", progressId, Progress.Status.Succeeded);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (client != null && sessionId != null) _ = client.CancelAsync(sessionId);
            Finish("Transfer cancelled.", progressId, Progress.Status.Canceled);
        }
        catch (Exception e)
        {
            if (client != null && sessionId != null) _ = client.CancelAsync(sessionId);
            Finish("Send failed: " + e.Message, progressId, Progress.Status.Failed);
        }
        finally { s_Busy = false; }
    }

    static void Finish(string message, int progressId, Progress.Status status)
    {
        s_Status = message;
        if (status == Progress.Status.Failed) Debug.LogError("[LocalSend] " + message);
        else Debug.Log("[LocalSend] " + message);
        Progress.Finish(progressId, status);
    }

    struct SendFile { public string id, path, name; public long size; }

    static List<SendFile> CollectFiles(string source)
    {
        var list = new List<SendFile>();
        if (File.Exists(source))
        {
            list.Add(new SendFile { id = "f0", path = source, name = Path.GetFileName(source), size = new FileInfo(source).Length });
            return list;
        }

        // Timestamped root so repeated builds never collide on the receiver.
        string root = Path.GetFileName(source.TrimEnd('/', '\\')) + "_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        int n = 0;
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string rel = file.Substring(source.Length).TrimStart('/', '\\').Replace('\\', '/');
            string top = rel.Split('/')[0];
            if (k_SkipFolderSuffixes.Any(s => top.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(new SendFile { id = "f" + n++, path = file, name = root + "/" + rel, size = new FileInfo(file).Length });
        }
        return list;
    }

    // ---------------------------------------------------------------- protocol

    static async Task<LocalSendClient> CreateClientAsync()
    {
        if (string.IsNullOrEmpty(Host)) throw new InvalidOperationException("No receiver address set.");
        if (!File.Exists(k_Curl)) throw new Exception("curl.exe not found at " + k_Curl + ".");
        if (s_ClientCert == null) s_ClientCert = await Task.Run(EnsureClientCertificate);
        return new LocalSendClient(Host, Port, Pin, s_ClientCert.Value.thumbprint, s_ClientCert.Value.fingerprint);
    }

    sealed class LocalSendClient
    {
        readonly string m_Pin, m_Thumbprint, m_OwnFingerprint;
        public readonly string BaseUrl;

        public LocalSendClient(string host, int port, string pin, string thumbprint, string fingerprint)
        {
            m_Pin = pin;
            m_Thumbprint = thumbprint;
            m_OwnFingerprint = fingerprint;
            BaseUrl = "https://" + host + ":" + port;
        }

        // --insecure only skips CA validation, which a self-signed peer can never pass; --pinnedpubkey still
        // enforces the key. It is omitted only for the single first-contact request that learns the pin.
        List<string> BaseArgs(string pinnedKey)
        {
            var args = new List<string> { "--insecure", "--cert", @"CurrentUser\MY\" + m_Thumbprint, "--connect-timeout", "5" };
            if (!string.IsNullOrEmpty(pinnedKey)) { args.Add("--pinnedpubkey"); args.Add(pinnedKey); }
            return args;
        }

        public async Task<string> GetInfoAsync(CancellationToken ct)
        {
            string pinned = PublicKeyPin;
            if (string.IsNullOrEmpty(pinned))
            {
                var args = BaseArgs(null);
                args.AddRange(new[] { "--max-time", "10", "-o", "NUL", "-w", "%{certs}", BaseUrl + "/api/localsend/v2/info" });
                var first = await RunCurl(args, null, null, ct);
                if (first.exit != 0) Check(first);
                var pem = Regex.Match(first.stdout, "-----BEGIN CERTIFICATE-----(.*?)-----END CERTIFICATE-----", RegexOptions.Singleline);
                if (!pem.Success) throw new Exception("Receiver sent no certificate.");
                pinned = "sha256//" + Convert.ToBase64String(SHA256Of(SubjectPublicKeyInfo(Convert.FromBase64String(Regex.Replace(pem.Groups[1].Value, @"\s", "")))));
                PublicKeyPin = pinned;
            }

            var infoArgs = BaseArgs(pinned);
            infoArgs.AddRange(new[] { "--max-time", "10", BaseUrl + "/api/localsend/v2/info" });
            return Check(await RunCurl(infoArgs, null, null, ct));
        }

        /// <returns>null when the receiver needs nothing (HTTP 204).</returns>
        public async Task<(string sessionId, Dictionary<string, string> tokens)?> PrepareUploadAsync(List<SendFile> files, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.Append("{\"info\":{\"alias\":").Append(Quote("Unity @ " + Environment.MachineName))
              .Append(",\"version\":\"2.1\",\"deviceModel\":\"Unity\",\"deviceType\":\"desktop\",\"fingerprint\":").Append(Quote(m_OwnFingerprint))
              .Append(",\"port\":").Append(k_DefaultPort).Append(",\"protocol\":\"https\",\"download\":false},\"files\":{");
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (i > 0) sb.Append(',');
                sb.Append(Quote(f.id)).Append(":{\"id\":").Append(Quote(f.id)).Append(",\"fileName\":").Append(Quote(f.name))
                  .Append(",\"size\":").Append(f.size).Append(",\"fileType\":\"application/octet-stream\"}");
            }
            sb.Append("}}");

            string url = BaseUrl + "/api/localsend/v2/prepare-upload" + (string.IsNullOrEmpty(m_Pin) ? "" : "?pin=" + Uri.EscapeDataString(m_Pin));
            var args = BaseArgs(PublicKeyPin);
            // Blocks until someone taps Accept on the receiver, so allow a generous wait.
            args.AddRange(new[] { "--max-time", "300", "-X", "POST", "-H", "Content-Type: application/json", "--data-binary", "@-", url });
            var res = await RunCurl(args, sb.ToString(), null, ct);
            if (res.status == 204) return null;
            string body = Check(res);

            string sessionId = JsonString(body, "sessionId");
            var tokens = new Dictionary<string, string>();
            var filesObj = Regex.Match(body, "\"files\"\\s*:\\s*\\{([^}]*)\\}");
            foreach (Match m in Regex.Matches(filesObj.Groups[1].Value, "\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\""))
                tokens[m.Groups[1].Value] = m.Groups[2].Value;
            return (sessionId, tokens);
        }

        public async Task UploadAsync(string sessionId, SendFile f, string token, Action<float> onProgress, CancellationToken ct)
        {
            string url = BaseUrl + "/api/localsend/v2/upload?sessionId=" + Uri.EscapeDataString(sessionId)
                         + "&fileId=" + Uri.EscapeDataString(f.id) + "&token=" + Uri.EscapeDataString(token);
            var args = BaseArgs(PublicKeyPin);
            // -T streams from disk (--data-binary @file would load it whole); "Expect:" skips the 100-continue round trip.
            args.AddRange(new[] { "-X", "POST", "-T", f.path, "-H", "Content-Type: application/octet-stream", "-H", "Expect:", url });
            Check(await RunCurl(args, null, onProgress, ct));
        }

        public async Task CancelAsync(string sessionId)
        {
            var args = BaseArgs(PublicKeyPin);
            args.AddRange(new[] { "--max-time", "5", "-X", "POST", BaseUrl + "/api/localsend/v2/cancel?sessionId=" + Uri.EscapeDataString(sessionId) });
            try { await RunCurl(args, null, null, CancellationToken.None); } catch { /* best effort */ }
        }

        static string Check((int exit, int status, string stdout, string stderr) res)
        {
            switch (res.exit)
            {
                case 0: break;
                case 6: throw new Exception("Receiver address not found.");
                case 7: throw new Exception("Could not connect. Is LocalSend open on the laptop, and is the address right?");
                case 28: throw new Exception("Timed out (nobody accepted in time, or the laptop went offline).");
                case 90: throw new Exception("Receiver key changed (LocalSend reinstalled, or another device at this address). If expected, press Forget in Tools > LocalSend Build Sender.");
                default: throw new Exception("curl exit " + res.exit + ": " + res.stderr.Trim());
            }
            if (res.status >= 200 && res.status < 300) return res.stdout;
            switch (res.status)
            {
                case 401: throw new Exception("Receiver wants a PIN (or the PIN is wrong).");
                case 403: throw new Exception("Declined on the receiver.");
                case 409: throw new Exception("Receiver is busy with another transfer.");
                case 429: throw new Exception("Receiver is rate-limiting; try again shortly.");
                default: throw new Exception("HTTP " + res.status + " " + res.stdout);
            }
        }
    }

    // ---------------------------------------------------------------- processes

    /// <summary>Runs curl; the HTTP status is appended to stdout via -w and split off again.</summary>
    static Task<(int exit, int status, string stdout, string stderr)> RunCurl(List<string> args, string stdin, Action<float> onProgress, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            var all = new List<string>(args);
            bool wantsStatus = !all.Contains("%{certs}");
            if (wantsStatus) { all.Add("-w"); all.Add("\n%{http_code}"); }
            if (onProgress != null) { all.Insert(0, "--progress-bar"); all.Insert(0, "-S"); }
            else all.Insert(0, "-sS");

            var psi = new ProcessStartInfo(k_Curl, string.Join(" ", all.Select(QuoteArg)))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using (var p = Process.Start(psi))
            using (ct.Register(() => { try { p.Kill(); } catch { } }))
            {
                if (stdin != null)
                {
                    var input = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false));
                    await input.WriteAsync(stdin);
                    input.Close();
                }
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                string stderr = onProgress == null ? await p.StandardError.ReadToEndAsync() : await ReadProgress(p.StandardError, onProgress);
                string stdout = await stdoutTask;
                p.WaitForExit();
                ct.ThrowIfCancellationRequested();

                int status = 0;
                if (wantsStatus)
                {
                    int nl = stdout.LastIndexOf('\n');
                    int.TryParse(stdout.Substring(nl + 1).Trim(), out status);
                    stdout = nl >= 0 ? stdout.Substring(0, nl) : "";
                }
                return (p.ExitCode, status, stdout, stderr);
            }
        });
    }

    /// <summary>curl's --progress-bar redraws "###   42.0%" with carriage returns; anything else is an error message.</summary>
    static async Task<string> ReadProgress(StreamReader stderr, Action<float> onProgress)
    {
        var buffer = new char[256];
        var text = new StringBuilder();
        int read;
        while ((read = await stderr.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            text.Append(buffer, 0, read);
            var m = Regex.Match(new string(buffer, 0, read), @"(\d+(?:\.\d+)?)%[^%]*$");
            if (m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pct))
                onProgress(pct / 100f);
        }
        return Regex.Replace(text.ToString(), @"[#\s]*\d+(?:\.\d+)?%", " ");
    }

    /// <summary>Finds or creates the client certificate in CurrentUser\My; returns its thumbprint and SHA-256 fingerprint.</summary>
    static (string thumbprint, string fingerprint) EnsureClientCertificate()
    {
        string script =
            "$c = Get-ChildItem Cert:\\CurrentUser\\My | Where-Object { $_.FriendlyName -eq '" + k_CertName + "' -and $_.NotAfter -gt (Get-Date).AddDays(30) -and $_.HasPrivateKey } | Select-Object -First 1; " +
            "if (-not $c) { $c = New-SelfSignedCertificate -Subject 'CN=" + k_CertName + "' -FriendlyName '" + k_CertName + "' " +
            "-KeyAlgorithm RSA -KeyLength 2048 -CertStoreLocation Cert:\\CurrentUser\\My -NotAfter (Get-Date).AddYears(30) }; " +
            "$h = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($c.RawData)).Replace('-', ''); " +
            "Write-Output ($c.Thumbprint + ' ' + $h)";
        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using (var p = Process.Start(psi))
        {
            var errTask = p.StandardError.ReadToEndAsync();
            string output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            var m = Regex.Match(output, @"([0-9A-F]{40}) ([0-9A-F]{64})");
            if (!m.Success) throw new Exception("Could not create the LocalSend client certificate: " + errTask.Result);
            return (m.Groups[1].Value, m.Groups[2].Value);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Extracts the DER SubjectPublicKeyInfo from a DER certificate (the 7th element of TBSCertificate, or 6th without a version).</summary>
    static byte[] SubjectPublicKeyInfo(byte[] der)
    {
        int i = 0;
        Header(der, ref i);                 // Certificate SEQUENCE
        Header(der, ref i);                 // TBSCertificate SEQUENCE
        if (der[i] == 0xA0) Skip(der, ref i);  // [0] version
        for (int n = 0; n < 5; n++) Skip(der, ref i);  // serial, signature, issuer, validity, subject
        int start = i;
        Skip(der, ref i);
        var spki = new byte[i - start];
        Array.Copy(der, start, spki, 0, spki.Length);
        return spki;
    }

    static int Header(byte[] d, ref int i)
    {
        i++;                                // tag
        int b = d[i++];
        if (b < 0x80) return b;
        int len = 0;
        for (int n = b & 0x7F; n > 0; n--) len = (len << 8) | d[i++];
        return len;
    }

    static void Skip(byte[] d, ref int i) { int len = Header(d, ref i); i += len; }

    static byte[] SHA256Of(byte[] data) { using (var sha = SHA256.Create()) return sha.ComputeHash(data); }

    static string JsonString(string json, string key)
    {
        var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? Regex.Unescape(m.Groups[1].Value) : "";
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Windows command-line quoting (CommandLineToArgvW rules).</summary>
    static string QuoteArg(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"', '\n' }) < 0) return arg;
        var sb = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') sb.Append('\\', slashes * 2 + 1).Append('"');
            else sb.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        return sb.Append('\\', slashes * 2).Append('"').ToString();
    }
}
