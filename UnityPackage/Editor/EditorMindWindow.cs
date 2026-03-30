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
            win.minSize = new Vector2(380, 480);
            win.Show();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────
        private void OnEnable()
        {
            _configured = EditorPrefs.GetBool(k_PrefsKey, false);
            ResolveServerPath();
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

#if UNITY_EDITOR_WIN
                    _serverPath = Path.Combine(_serverDir, "bin", "editormind-mcp-win.exe").Replace('\\', '/');
#elif UNITY_EDITOR_OSX
                    _serverPath = Path.Combine(_serverDir, "bin", "editormind-mcp-macos").Replace('\\', '/');
#else
                    _serverPath = Path.Combine(_serverDir, "bin", "editormind-mcp-linux").Replace('\\', '/');
#endif
                    return;
                }
            }
            catch { }

            string fallback = Path.GetFullPath(
                Path.Combine(Application.dataPath,
                    "../Packages/com.editormind.editormind-mcp/Server~/bin/editormind-mcp-win.exe"));
            _serverPath = fallback.Replace('\\', '/');
            _serverDir  = Path.GetDirectoryName(Path.GetDirectoryName(_serverPath)).Replace('\\', '/');
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

            // Server binary path
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Server binary:", GUILayout.Width(100));
            bool pathExists = File.Exists(_serverPath);
            GUI.color = pathExists ? Color.green : Color.red;
            GUILayout.Label(pathExists ? "Found" : "Not found", GUILayout.Width(70));
            GUI.color = Color.white;
            GUILayout.Label(_serverPath, EditorStyles.miniLabel);
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

            bool canConfigure = File.Exists(_serverPath);

            GUI.enabled = canConfigure;
            if (GUILayout.Button("Configure Claude Code", GUILayout.Height(30)))
                RunConfigureCommand();
            GUI.enabled = true;

            if (!canConfigure)
            {
                EditorGUILayout.HelpBox(
                    "Server binary not found. Reinstall the package via Package Manager.",
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