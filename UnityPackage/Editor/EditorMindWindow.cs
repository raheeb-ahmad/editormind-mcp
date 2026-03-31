// EditorMind MCP — Unity AI Bridge
// Author: Raheeb Ahmad (github.com/raheeb-ahmad)

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace EditorMind
{
    public class EditorMindWindow : EditorWindow
    {
        // ── Constants ─────────────────────────────────────────────────────
        private const string k_MenuPath     = "Tools/EditorMind";
        private const string k_PrefsKey     = "EditorMind_Configured";
        private const string k_ServerPort   = "http://localhost:6400";
        private const double k_PingInterval = 2.0;

        private const string k_PrefLatestVersion = "EditorMind_LatestVersion";
        private const string k_PrefReleaseNotes  = "EditorMind_ReleaseNotes";
        private const string k_PrefLastCheck     = "EditorMind_LastCheckTime";
        private const long   k_CheckIntervalSec  = 3600;
        private const string k_ReleasesUrl        = "https://github.com/raheeb-ahmad/editormind-mcp/releases/latest";
        private const string k_GitHubApiUrl       = "https://api.github.com/repos/raheeb-ahmad/editormind-mcp/releases/latest";
        private const string k_GitHubDownloadBase = "https://github.com/raheeb-ahmad/editormind-mcp/releases/latest/download/";

        // ── State ─────────────────────────────────────────────────────────
        private string  _serverPath    = "";
        private string  _serverDir     = "";
        private bool    _serverOnline  = false;
        private bool    _configured    = false;
        private string  _statusMessage = "";
        private bool    _statusIsError = false;
        private double  _nextPingTime  = 0;
        private bool    _pingInFlight  = false;
        private Vector2 _scroll;

        // ── Version check state ───────────────────────────────────────────
        private string _currentVersion  = "";
        private string _latestVersion   = "";
        private string _releaseNotes    = "";
        private bool   _updateAvailable = false;
        private string _packageName     = "com.editormind.editormind-mcp";

        // ── Download state ────────────────────────────────────────────────
        private bool  _isDownloading    = false;
        private float _downloadProgress = 0f;

        // ── Style cache ───────────────────────────────────────────────────
        private GUIStyle _styleHeading;
        private GUIStyle _styleFooter;
        private GUIStyle _styleDot;
        private GUIStyle _styleToolRow;
        private bool     _stylesBuilt = false;

        private static readonly string[] k_Tools = new[]
        {
            "get_scene_hierarchy",
            "get_selected_object",
            "create_gameobject",
            "compile_scripts",
            "get_compile_errors",
            "read_script",
            "write_script"
        };

        // ── Open window ───────────────────────────────────────────────────
        [MenuItem(k_MenuPath)]
        public static void OpenWindow()
        {
            var win = GetWindow<EditorMindWindow>(false, "EditorMind", true);
            win.minSize = new Vector2(380, 480);
            win.Show();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────
        private void OnEnable()
        {
            _configured = EditorPrefs.GetBool(k_PrefsKey, false);
            ResolveServerPath();
            EditorApplication.update += OnEditorUpdate;
            CheckVersionAsync();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup >= _nextPingTime && !_pingInFlight)
            {
                _nextPingTime = EditorApplication.timeSinceStartup + k_PingInterval;
                PingServerAsync();
            }
        }

        // ── Path detection ────────────────────────────────────────────────
        private static string PlatformBinaryName
        {
            get
            {
#if UNITY_EDITOR_WIN
                return "editormind-mcp-win.exe";
#elif UNITY_EDITOR_OSX
                return "editormind-mcp-macos";
#else
                return "editormind-mcp-linux";
#endif
            }
        }

        private static string GetPersistentBinaryPath() =>
            Path.Combine(Application.persistentDataPath, "EditorMind", "bin", PlatformBinaryName)
                .Replace('\\', '/');

        private void ResolveServerPath()
        {
            string persistentPath = GetPersistentBinaryPath();
            if (File.Exists(persistentPath))
            {
                _serverPath = persistentPath;
                _serverDir  = Path.GetDirectoryName(persistentPath).Replace('\\', '/');
                return;
            }

            try
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo
                    .FindForAssembly(Assembly.GetExecutingAssembly());

                if (packageInfo != null)
                {
                    string root  = packageInfo.resolvedPath;
                    _serverDir   = Path.Combine(root, "Server~").Replace('\\', '/');
                    _packageName = packageInfo.name;
                    _serverPath  = Path.Combine(_serverDir, "bin", PlatformBinaryName).Replace('\\', '/');
                    return;
                }
            }
            catch { }

            string fallback = Path.Combine(Application.dataPath,
                "..", "Packages", "com.editormind.editormind-mcp",
                "Server~", "bin", PlatformBinaryName);
            _serverPath = Path.GetFullPath(fallback).Replace('\\', '/');
            _serverDir  = Path.GetDirectoryName(_serverPath).Replace('\\', '/');
        }

        // ── Server ping ───────────────────────────────────────────────────
        private async void PingServerAsync()
        {
            _pingInFlight = true;
            bool online   = false;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
                var resp = await client.GetAsync(k_ServerPort);
                online   = (int)resp.StatusCode < 500;
            }
            catch { online = false; }

            _serverOnline = online;
            _pingInFlight = false;
            Repaint();
        }

        // ── Version check ─────────────────────────────────────────────────
        private async void CheckVersionAsync()
        {
            // Step 1 — PackageInfo (works for git URL installs)
            try
            {
                var pkg = UnityEditor.PackageManager.PackageInfo
                    .FindForAssembly(Assembly.GetExecutingAssembly());
                if (pkg != null)
                {
                    _currentVersion = pkg.version;
                    _packageName    = pkg.name;
                }
            }
            catch { }

            // Step 2 — PackageCache scan (works for both git URL and local installs)
            if (string.IsNullOrEmpty(_currentVersion))
            {
                try
                {
                    string cacheDir = new System.Uri(
                        Application.dataPath + "/../Library/PackageCache/").LocalPath;

                    if (Directory.Exists(cacheDir))
                    {
                        foreach (string dir in Directory.GetDirectories(
                            cacheDir, "com.editormind.editormind-mcp*"))
                        {
                            string pkgJson = Path.Combine(dir, "package.json");
                            if (File.Exists(pkgJson))
                            {
                                string ver = ExtractJsonString(
                                    File.ReadAllText(pkgJson), "version");
                                if (!string.IsNullOrEmpty(ver))
                                {
                                    _currentVersion = ver;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Step 3 — Packages folder (works for local file: installs)
            if (string.IsNullOrEmpty(_currentVersion))
            {
                try
                {
                    string projectRoot = new System.Uri(
                        Application.dataPath + "/../").LocalPath;
                    string pkgJson = Path.Combine(projectRoot, "Packages",
                        "com.editormind.editormind-mcp", "package.json");

                    if (File.Exists(pkgJson))
                    {
                        string ver = ExtractJsonString(
                            File.ReadAllText(pkgJson), "version");
                        if (!string.IsNullOrEmpty(ver))
                            _currentVersion = ver;
                    }
                }
                catch { }
            }

            // Restore cached latest version
            _latestVersion = EditorPrefs.GetString(k_PrefLatestVersion, "");
            _releaseNotes  = EditorPrefs.GetString(k_PrefReleaseNotes, "");
            if (!string.IsNullOrEmpty(_latestVersion) && !string.IsNullOrEmpty(_currentVersion))
                _updateAvailable = IsNewer(_latestVersion, _currentVersion);

            // Check rate limit cache
            long lastCheck = 0;
            long.TryParse(EditorPrefs.GetString(k_PrefLastCheck, "0"), out lastCheck);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - lastCheck < k_CheckIntervalSec)
            {
                Repaint();
                return;
            }

            // Fetch from GitHub
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                client.DefaultRequestHeaders.Add("User-Agent", "editormind-mcp-unity");
                string json = await client.GetStringAsync(k_GitHubApiUrl);

                string tag   = ExtractJsonString(json, "tag_name");
                string notes = ExtractJsonString(json, "body");

                if (!string.IsNullOrEmpty(tag))
                {
                    _latestVersion = tag;
                    _releaseNotes  = notes;
                    EditorPrefs.SetString(k_PrefLatestVersion, _latestVersion);
                    EditorPrefs.SetString(k_PrefReleaseNotes,  _releaseNotes);
                    EditorPrefs.SetString(k_PrefLastCheck,     now.ToString());

                    if (!string.IsNullOrEmpty(_currentVersion))
                        _updateAvailable = IsNewer(_latestVersion, _currentVersion);
                }
            }
            catch { }

            Repaint();
        }

        // ── Binary download ───────────────────────────────────────────────
        private async void DownloadBinaryAsync()
        {
            _isDownloading    = true;
            _downloadProgress = 0f;
            Repaint();

            string binaryName  = PlatformBinaryName;
            string downloadUrl = k_GitHubDownloadBase + binaryName;
            string destPath    = GetPersistentBinaryPath();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath));

                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                client.DefaultRequestHeaders.Add("User-Agent", "editormind-mcp-unity");

                using var response = await client.GetAsync(
                    downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength ?? -1;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var fs     = new FileStream(destPath, FileMode.Create,
                    FileAccess.Write, FileShare.None);

                byte[] buffer   = new byte[81920];
                long   received = 0;
                int    read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, read);
                    received += read;
                    if (total > 0)
                        _downloadProgress = (float)received / total;
                    Repaint();
                }

#if !UNITY_EDITOR_WIN
                var chmod = new ProcessStartInfo("chmod", $"+x \"{destPath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow  = true
                };
                using var chmodProc = Process.Start(chmod);
                chmodProc?.WaitForExit(5000);
#endif
                _statusMessage = "Server binary downloaded successfully.";
                _statusIsError = false;
            }
            catch (Exception ex)
            {
                _statusMessage = $"Download failed: {ex.Message}";
                _statusIsError = true;
                UnityEngine.Debug.LogError("[EditorMind] Binary download failed: " + ex.Message);
            }
            finally
            {
                _isDownloading    = false;
                _downloadProgress = 0f;
            }

            ResolveServerPath();
            Repaint();
        }

        // ── JSON helpers ──────────────────────────────────────────────────
        private static string ExtractJsonString(string json, string key)
        {
            string search = $"\"{key}\":\"";
            int start = json.IndexOf(search, StringComparison.Ordinal);
            if (start < 0) return "";
            start += search.Length;
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[++i];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        default:   sb.Append(next); break;
                    }
                }
                else if (c == '"') break;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool IsNewer(string latest, string current)
        {
            static string Strip(string v) => v.TrimStart('v', 'V').Split('-')[0];
            return Version.TryParse(Strip(latest),  out var lv)
                && Version.TryParse(Strip(current), out var cv)
                && lv > cv;
        }

        // ── IMGUI ─────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EnsureStyles();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            GUILayout.Space(6);
            DrawDivider();
            GUILayout.Space(6);
            DrawDiagnostics();
            GUILayout.Space(6);
            DrawDivider();
            GUILayout.Space(6);
            DrawTools();
            GUILayout.Space(6);
            DrawDivider();
            GUILayout.Space(6);
            DrawActions();
            GUILayout.Space(6);

            if (!string.IsNullOrEmpty(_statusMessage))
                DrawStatusMessage();

            GUILayout.FlexibleSpace();
            DrawDivider();
            DrawFooter();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            GUILayout.Space(10);
            GUILayout.Label("EditorMind MCP", _styleHeading);
            GUILayout.Label("Unity AI Bridge for Claude Code", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(2);

            if (_updateAvailable)
            {
                EditorGUILayout.HelpBox(
                    $"Update available: {_latestVersion} — you have v{_currentVersion}",
                    MessageType.Warning);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Update", GUILayout.Height(22)))
                    Application.OpenURL(k_ReleasesUrl);
                if (GUILayout.Button("Release notes", GUILayout.Height(22)))
                    Application.OpenURL(k_ReleasesUrl);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(4);
            }
        }

        private void DrawDiagnostics()
        {
            GUILayout.Label("Status", EditorStyles.boldLabel);
            GUILayout.Space(4);

            bool pathExists = File.Exists(_serverPath);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Server binary:", GUILayout.Width(100));
            if (_isDownloading)
            {
                GUILayout.Label("Downloading...", GUILayout.Width(90));
            }
            else if (pathExists)
            {
                GUI.color = Color.green;
                GUILayout.Label("Found", GUILayout.Width(70));
                GUI.color = Color.white;
                GUILayout.Label(_serverPath, EditorStyles.miniLabel);
            }
            else
            {
                GUI.color = Color.red;
                GUILayout.Label("Not found", GUILayout.Width(70));
                GUI.color = Color.white;
                if (GUILayout.Button("Download Server", GUILayout.Height(18)))
                    DownloadBinaryAsync();
            }
            EditorGUILayout.EndHorizontal();

            if (_isDownloading)
            {
                var progressRect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(progressRect, _downloadProgress,
                    $"Downloading... {Mathf.RoundToInt(_downloadProgress * 100)}%");
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Bridge:", GUILayout.Width(100));
            GUI.color = _serverOnline ? Color.green : Color.red;
            GUILayout.Label(_serverOnline ? "● Online" : "● Offline", _styleDot, GUILayout.Width(70));
            GUI.color = Color.white;
            GUILayout.Label("(port 6400)", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Claude MCP:", GUILayout.Width(100));
            GUI.color = _configured ? Color.green : new Color(0.7f, 0.7f, 0.7f);
            GUILayout.Label(_configured ? "● Configured" : "● Not configured", _styleDot);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTools()
        {
            GUILayout.Label("Available Tools", EditorStyles.boldLabel);
            GUILayout.Space(4);

            foreach (var tool in k_Tools)
            {
                EditorGUILayout.BeginHorizontal(_styleToolRow);
                GUI.color = Color.green;
                GUILayout.Label("●", _styleDot, GUILayout.Width(16));
                GUI.color = Color.white;
                GUILayout.Label(tool, EditorStyles.label);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawActions()
        {
            GUILayout.Label("Setup", EditorStyles.boldLabel);
            GUILayout.Space(4);

            bool canConfigure = File.Exists(_serverPath);

            GUI.enabled = canConfigure;
            if (GUILayout.Button("Configure Claude Code", GUILayout.Height(30)))
                RunConfigureCommand();
            GUI.enabled = true;

            if (!canConfigure)
            {
                EditorGUILayout.HelpBox(
                    "Server binary not found. Click Download Server above.",
                    MessageType.Warning);
            }

            GUILayout.Space(8);
            GUILayout.Label("Usage", EditorStyles.boldLabel);
            GUILayout.Space(4);

            string projectPath = Application.dataPath.Replace("/Assets", "");
            EditorGUILayout.HelpBox(
                "After configuring, open a terminal at your Unity project root and run: claude\n\n" +
                "Your project path:\n" + projectPath,
                MessageType.Info);

            GUILayout.Space(4);
            if (GUILayout.Button("Copy Project Path", GUILayout.Height(24)))
            {
                GUIUtility.systemCopyBuffer = projectPath;
                _statusMessage = "Project path copied to clipboard.";
                _statusIsError = false;
                Repaint();
            }
        }

        private void DrawStatusMessage()
        {
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                _statusMessage,
                _statusIsError ? MessageType.Error : MessageType.Info);
        }

        private void DrawDivider()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f));
        }

        private void DrawFooter()
        {
            GUILayout.Space(6);
            GUILayout.Label(
                "Made by Raheeb Ahmad  ·  github.com/raheeb-ahmad",
                _styleFooter);
            GUILayout.Space(6);
        }

        // ── Commands ──────────────────────────────────────────────────────
        private string BuildClaudeCommand() =>
            $"claude mcp add editormind-mcp --scope user --transport stdio -- \"{_serverPath}\"";

        private void RunConfigureCommand()
        {
            bool ok = RunClaudeCommand(BuildClaudeCommand());
            if (ok)
            {
                EditorPrefs.SetBool(k_PrefsKey, true);
                _configured    = true;
                _statusMessage = "Claude Code configured successfully.";
                _statusIsError = false;
            }
            else
            {
                _statusMessage = "Configure failed. Copy the project path and run the command manually in terminal.";
                _statusIsError = true;
            }
            Repaint();
        }

        // ── Process helpers ───────────────────────────────────────────────
        private static bool RunClaudeCommand(string fullCommand)
        {
            try
            {
#if UNITY_EDITOR_WIN
                string claudePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "bin", "claude.exe");

                if (!File.Exists(claudePath))
                    claudePath = "claude";

                var removePsi = new ProcessStartInfo(claudePath, "mcp remove editormind-mcp")
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };
                using var removeProc = Process.Start(removePsi);
                removeProc.WaitForExit(5000);

                string claudeArgs = fullCommand.Substring(fullCommand.IndexOf("mcp "));
                var psi = new ProcessStartInfo(claudePath, claudeArgs)
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };
                using var proc   = Process.Start(psi);
                string    stdout = proc.StandardOutput.ReadToEnd();
                string    stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(15000);

                if (!string.IsNullOrEmpty(stdout))
                    UnityEngine.Debug.Log($"[EditorMind] claude: {stdout}");
                if (!string.IsNullOrEmpty(stderr))
                    UnityEngine.Debug.LogWarning($"[EditorMind] claude stderr: {stderr}");

                return proc.ExitCode == 0;
#else
                var psi = new ProcessStartInfo("/bin/sh", $"-c \"{fullCommand}\"")
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };
                using var proc = Process.Start(psi);
                proc.WaitForExit(15000);
                return proc.ExitCode == 0;
#endif
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[EditorMind] claude error: {ex.Message}");
                return false;
            }
        }

        // ── Style builder ─────────────────────────────────────────────────
        private void EnsureStyles()
        {
            if (_stylesBuilt) return;

            _styleHeading = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize  = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _styleFooter = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize  = 10,
                alignment = TextAnchor.MiddleCenter
            };

            _styleDot = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold
            };

            _styleToolRow = new GUIStyle
            {
                padding = new RectOffset(4, 4, 2, 2)
            };

            _stylesBuilt = true;
        }
    }

    public class EditorMindWindowHelper : ScriptableObject { }
}