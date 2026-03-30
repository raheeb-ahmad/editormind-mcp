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

        // ── State ─────────────────────────────────────────────────────────
        private string  _serverPath    = "";
        private string  _serverDir     = "";
        private bool    _nodeInstalled = false;
        private bool    _nodeChecked   = false;
        private bool    _serverOnline  = false;
        private bool    _configured    = false;
        private string  _statusMessage = "";
        private bool    _statusIsError = false;
        private double  _nextPingTime  = 0;
        private bool    _pingInFlight  = false;
        private Vector2 _scroll;

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
            win.minSize = new Vector2(380, 520);
            win.Show();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────
        private void OnEnable()
        {
            _configured = EditorPrefs.GetBool(k_PrefsKey, false);
            ResolveServerPath();
            CheckNodeAsync();
            CheckNodeModulesAsync();
            EditorApplication.update += OnEditorUpdate;
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
        private void ResolveServerPath()
        {
            try
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo
                    .FindForAssembly(Assembly.GetExecutingAssembly());

                if (packageInfo != null)
                {
                    string root = packageInfo.resolvedPath;
                    _serverDir  = Path.Combine(root, "Server~").Replace('\\', '/');
                    _serverPath = Path.Combine(_serverDir, "index.js").Replace('\\', '/');
                    return;
                }
            }
            catch { }

            string fallback = Path.GetFullPath(
                Path.Combine(Application.dataPath,
                    "../Packages/com.editormind.editormind-mcp/Server~/index.js"));
            _serverPath = fallback.Replace('\\', '/');
            _serverDir  = Path.GetDirectoryName(_serverPath).Replace('\\', '/');
        }

        // ── Node checks ───────────────────────────────────────────────────
        private async void CheckNodeAsync()
        {
            _nodeInstalled = await Task.Run(() =>
            {
                try
                {
#if UNITY_EDITOR_WIN
                    string nodePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "nodejs", "node.exe");

                    if (!File.Exists(nodePath))
                        nodePath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                            "nodejs", "node.exe");

                    if (!File.Exists(nodePath))
                        nodePath = "node";
#else
                    string nodePath = "node";
#endif
                    var psi = new ProcessStartInfo(nodePath, "--version")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true
                    };
                    using var proc = Process.Start(psi);
                    proc.WaitForExit(3000);
                    return proc.ExitCode == 0;
                }
                catch { return false; }
            });
            _nodeChecked = true;
            Repaint();
        }

        private async void CheckNodeModulesAsync()
        {
            if (string.IsNullOrEmpty(_serverDir)) return;

            string modulesDir  = Path.Combine(_serverDir, "node_modules");
            bool   needInstall = await Task.Run(() => !Directory.Exists(modulesDir));

            if (needInstall)
            {
                _statusMessage = "Installing npm dependencies...";
                _statusIsError = false;
                Repaint();

                bool ok = await Task.Run(() => RunNpm("install", _serverDir));
                _statusMessage = ok
                    ? "npm install completed."
                    : "npm install failed. Check console for details.";
                _statusIsError = !ok;
                Repaint();
            }
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
        }

        private void DrawDiagnostics()
        {
            GUILayout.Label("Status", EditorStyles.boldLabel);
            GUILayout.Space(4);

            // Server path
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Server path:", GUILayout.Width(100));
            bool pathExists = File.Exists(_serverPath);
            GUI.color = pathExists ? Color.green : Color.red;
            GUILayout.Label(pathExists ? "Found" : "Not found", GUILayout.Width(70));
            GUI.color = Color.white;
            GUILayout.Label(_serverPath, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            // Node.js
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Node.js:", GUILayout.Width(100));
            if (!_nodeChecked)
            {
                GUILayout.Label("Checking...", EditorStyles.miniLabel);
            }
            else
            {
                GUI.color = _nodeInstalled ? Color.green : Color.red;
                GUILayout.Label(_nodeInstalled ? "Installed" : "Not found", GUILayout.Width(70));
                GUI.color = Color.white;
                if (!_nodeInstalled)
                    GUILayout.Label("Install from nodejs.org", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            // node_modules
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("node_modules:", GUILayout.Width(100));
            bool hasModules = Directory.Exists(Path.Combine(_serverDir, "node_modules"));
            GUI.color = hasModules ? Color.green : new Color(1f, 0.6f, 0f);
            GUILayout.Label(hasModules ? "Present" : "Missing (installing...)", GUILayout.Width(160));
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            // Bridge ping
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Bridge:", GUILayout.Width(100));
            GUI.color = _serverOnline ? Color.green : Color.red;
            GUILayout.Label(_serverOnline ? "● Online" : "● Offline", _styleDot, GUILayout.Width(70));
            GUI.color = Color.white;
            GUILayout.Label("(port 6400)", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            // Claude MCP
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

            bool canConfigure = _nodeInstalled && File.Exists(_serverPath);

            GUI.enabled = canConfigure;
            if (GUILayout.Button("Configure Claude Code", GUILayout.Height(30)))
                RunConfigureCommand();
            GUI.enabled = true;

            if (!canConfigure && _nodeChecked)
            {
                string hint = !_nodeInstalled
                    ? "Node.js not found. Install from nodejs.org and restart Unity."
                    : "Server path not found. Ensure the package is installed correctly.";
                EditorGUILayout.HelpBox(hint, MessageType.Warning);
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
            $"claude mcp add editormind-mcp --scope user --transport stdio -- node \"{_serverPath}\"";

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
        private static bool RunNpm(string args, string workingDir)
        {
            try
            {
#if UNITY_EDITOR_WIN
                string npmPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "nodejs", "npm.cmd");

                if (!File.Exists(npmPath))
                    npmPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "nodejs", "npm.cmd");

                string quotedDir = $"\"{workingDir}\"";
                string cmdArgs   = $"/c cd /d {quotedDir} && \"{npmPath}\" {args}";

                var psi = new ProcessStartInfo("cmd.exe", cmdArgs)
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };

                using var proc   = Process.Start(psi);
                string    stdout = proc.StandardOutput.ReadToEnd();
                string    stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(60000);

                if (!string.IsNullOrEmpty(stdout))
                    UnityEngine.Debug.Log($"[EditorMind] npm: {stdout}");
                if (!string.IsNullOrEmpty(stderr))
                    UnityEngine.Debug.LogWarning($"[EditorMind] npm stderr: {stderr}");

                return proc.ExitCode == 0;
#else
                var psi = new ProcessStartInfo("npm", args)
                {
                    WorkingDirectory       = workingDir,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };
                using var proc = Process.Start(psi);
                proc.WaitForExit(60000);
                return proc.ExitCode == 0;
#endif
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[EditorMind] npm error: {ex.Message}");
                return false;
            }
        }

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

                // Remove existing registration first to avoid conflict
                var removePsi = new ProcessStartInfo(claudePath, "mcp remove editormind-mcp")
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };
                using var removeProc = Process.Start(removePsi);
                removeProc.WaitForExit(5000);

                // Now add fresh
                string claudeArgs = fullCommand.Substring(fullCommand.IndexOf("mcp "));
                UnityEngine.Debug.Log($"[EditorMind] Claude path: {claudePath}");
                UnityEngine.Debug.Log($"[EditorMind] Claude args: {claudeArgs}");

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
}