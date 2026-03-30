# editormind-mcp

> AI agent control for Unity Editor via the Model Context Protocol.

**editormind-mcp** bridges AI coding agents — currently Claude Code — with the Unity Editor. It exposes Unity Editor functionality as MCP tools, allowing an AI agent to inspect scenes, create GameObjects, read and write C# scripts, trigger compilation, and more, all through natural language.

Built to be AI-agnostic: Claude Code today, other MCP clients in the future.

---

## Demo

> GIF coming soon — shows Claude Code querying a live Unity scene hierarchy in real time.

---

## How it works

```
Claude Code  ──(MCP stdio)──►  EditorMind Server (Node.js, bundled)  ──(HTTP)──►  Unity Editor (C# Bridge)
```

- **Unity package** — an `[InitializeOnLoad]` C# class starts an HTTP listener on `localhost:6400` automatically when the Editor opens.
- **MCP server** — bundled inside the package at `Server~/index.js`. No separate install needed.
- **EditorMind window** — one-click configuration via `Tools → EditorMind`.
- **No internet required** — everything runs locally.

---

## Tools

| Tool | Description |
|---|---|
| `get_scene_hierarchy` | Returns all GameObjects in the active scene with components and children |
| `get_selected_object` | Returns the selected GameObject's name, components, transform, and hierarchy path |
| `create_gameobject` | Creates a new GameObject with a given name, optional primitive type and parent |
| `compile_scripts` | Triggers script compilation via `CompilationPipeline.RequestScriptCompilation()` |
| `get_compile_errors` | Reports whether the project has compile errors |
| `read_script` | Reads a C# script file from the Unity project |
| `write_script` | Writes content to a C# script file and triggers reimport |

---

## Requirements

- Unity **2021.3**, **2022.3**, or **Unity 6** (URP or Built-in)
- Node.js **18+** — download from [nodejs.org](https://nodejs.org)
- Claude Code CLI — install from [claude.ai/code](https://claude.ai/code)

---

## Installation

### Step 1 — Install the Unity package

Open your Unity project, go to:

**Window → Package Manager → + → Add package from git URL**

Paste this URL:

```
https://github.com/raheeb-ahmad/editormind-mcp.git?path=UnityPackage
```

Unity will compile automatically. Check the Console for:

```
[EditorMind] Listening on http://localhost:6400/
```

### Step 2 — Configure Claude Code

In Unity, open:

**Tools → EditorMind**

Wait for all status indicators to turn green, then click **Configure Claude Code**.

The window will automatically register editormind-mcp with your Claude Code installation.

### Step 3 — Start using it

Click **Copy Project Path** in the EditorMind window, then open a terminal at your Unity project root and run:

```bash
claude
```

That's it. Start prompting.

---

## Usage examples

```
What GameObjects are in my current Unity scene?

Create a GameObject called EnemySpawner

Read the script at Assets/Scripts/PlayerMovement.cs and add a sprint mechanic

Trigger a recompile in Unity

Are there any compile errors in my project?
```

---

## EditorMind window

Open via **Tools → EditorMind**. The window shows:

- **Server path** — auto-detected location of the bundled Node.js server
- **Node.js** — checks if Node.js is installed
- **node_modules** — auto-installs dependencies on first open
- **Bridge** — live ping to the HTTP bridge (updates every 2 seconds)
- **Claude MCP** — whether Claude Code has been configured
- **Available tools** — list of all 7 tools
- **Copy Project Path** — copies your Unity project path for opening Claude Code

---

## Windows: fix port access (if needed)

If Unity throws an `HttpListener` access denied error, run once in PowerShell as Administrator:

```powershell
netsh http add urlacl url=http://localhost:6400/ user=Everyone
```

---

## Project structure

```
editormind-mcp/
├── UnityPackage/
│   ├── Editor/
│   │   ├── EditorMindBridge.cs     # HTTP listener, boots with Unity
│   │   ├── EditorMindTools.cs      # Tool handlers
│   │   └── EditorMindWindow.cs     # Tools → EditorMind setup window
│   ├── Server~/                    # Bundled Node.js MCP server (hidden from Unity importer)
│   │   ├── index.js
│   │   └── package.json
│   └── package.json                # Unity Package Manager manifest
├── .gitignore
└── README.md
```

---

## Roadmap

- [ ] Support for Cursor and other MCP clients
- [ ] `get_asset_list` — list assets by type
- [ ] `add_component` — add a component to a selected GameObject
- [ ] `run_tests` — execute Unity Test Runner and return results
- [ ] `take_screenshot` — capture the Scene or Game view
- [ ] npm publish for `npx` installation

---

## License

MIT — see [LICENSE](LICENSE)

---

## Author

[Raheeb Ahmad](https://github.com/raheeb-ahmad) — Software Engineer & Game Developer
