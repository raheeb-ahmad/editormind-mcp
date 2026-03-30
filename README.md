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
Claude Code  ──(MCP stdio)──►  editormind-mcp Server (Node.js)  ──(HTTP)──►  Unity Editor (C# Bridge)
```

- **Unity package** — an `[InitializeOnLoad]` C# class starts an HTTP listener on `localhost:6400` automatically when the Editor opens.
- **MCP server** — a Node.js process that speaks the MCP protocol to Claude Code and forwards tool calls to the Unity bridge.
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
- Node.js **18+**
- Claude Code CLI

---

## Installation

### 1. Clone the repository

```bash
git clone https://github.com/raheeb-ahmad/editormind-mcp
cd editormind-mcp/Server
npm install
```

### 2. Install the Unity package

Open your Unity project's `Packages/manifest.json` and add:

```json
{
  "dependencies": {
    "com.editormind.editormind-mcp": "file:/absolute/path/to/editormind-mcp/UnityPackage"
  }
}
```

Save the file. Unity will compile automatically. Check the Console for:

```
[EditorMind] Listening on http://localhost:6400/
```

### 3. Register with Claude Code

```bash
claude mcp add editormind-mcp \
  --scope user \
  --transport stdio \
  -- node "/absolute/path/to/editormind-mcp/Server/index.js"
```

Verify:

```bash
claude mcp list
```

You should see `editormind-mcp` listed as connected.

---

## Usage

Open Claude Code in any directory and ask:

```
What GameObjects are in my current Unity scene?

Create a GameObject called EnemySpawner

Read the script at Assets/Scripts/PlayerMovement.cs and add a sprint mechanic

Trigger a recompile in Unity
```

---

## Windows: fix port access (if needed)

If Unity throws an `HttpListener` access denied error, run once in PowerShell as Administrator:

```powershell
netsh http add urlacl url=http://localhost:6400/ user=Everyone
```

---

## Cloning on a new machine

```bash
git clone https://github.com/raheeb-ahmad/editormind-mcp
cd editormind-mcp/Server
npm install
```

Then repeat steps 2 and 3 above with the correct local path.

---

## Project structure

```
editormind-mcp/
├── UnityPackage/
│   ├── Editor/
│   │   ├── EditorMindBridge.cs   # HTTP listener, boots with Unity
│   │   └── EditorMindTools.cs    # Tool handlers
│   └── package.json              # Unity Package Manager manifest
├── Server/
│   ├── index.js                  # MCP server entry point
│   └── package.json
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
