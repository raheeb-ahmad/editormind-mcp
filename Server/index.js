#!/usr/bin/env node

const { McpServer } = require("@modelcontextprotocol/sdk/server/mcp.js");
const { StdioServerTransport } = require("@modelcontextprotocol/sdk/server/stdio.js");
const { z } = require("zod");

const UNITY_URL = "http://localhost:6400";

async function callUnity(tool, params = {}) {
  let response;
  try {
    response = await fetch(UNITY_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ tool, params }),
    });
  } catch {
    throw new Error(
      "Could not connect to Unity. Make sure Unity is open with the EditorMind package installed."
    );
  }

  if (!response.ok) {
    const text = await response.text().catch(() => response.statusText);
    throw new Error(`Unity returned an error (${response.status}): ${text}`);
  }

  return response.json();
}

function makeTextResult(data) {
  return { content: [{ type: "text", text: JSON.stringify(data, null, 2) }] };
}

const server = new McpServer({
  name: "editormind-mcp",
  version: "1.0.0",
});

server.tool("get_scene_hierarchy", "Get the full hierarchy of GameObjects in the current scene.", {}, async () => {
  const data = await callUnity("get_scene_hierarchy");
  return makeTextResult(data);
});

server.tool(
  "get_selected_object",
  "Get details about the currently selected GameObject in the Unity Editor.",
  {},
  async () => {
    const data = await callUnity("get_selected_object");
    return makeTextResult(data);
  }
);

server.tool(
  "create_gameobject",
  "Create a new GameObject in the current scene.",
  {
    name: z.string().describe("Name for the new GameObject"),
    parent: z.string().optional().describe("Path or name of the parent GameObject (optional)"),
    primitiveType: z
      .enum(["Sphere", "Capsule", "Cylinder", "Cube", "Plane", "Quad"])
      .optional()
      .describe("Create as a primitive mesh (optional)"),
  },
  async (params) => {
    const data = await callUnity("create_gameobject", params);
    return makeTextResult(data);
  }
);

server.tool(
  "compile_scripts",
  "Trigger a script compilation in the Unity Editor and wait for it to finish.",
  {},
  async () => {
    const data = await callUnity("compile_scripts");
    return makeTextResult(data);
  }
);

server.tool(
  "get_compile_errors",
  "Get the current list of script compilation errors from Unity.",
  {},
  async () => {
    const data = await callUnity("get_compile_errors");
    return makeTextResult(data);
  }
);

server.tool(
  "read_script",
  "Read the contents of a C# script asset from the Unity project.",
  {
    path: z.string().describe("Project-relative path to the script, e.g. Assets/Scripts/Player.cs"),
  },
  async (params) => {
    const data = await callUnity("read_script", params);
    return makeTextResult(data);
  }
);

server.tool(
  "write_script",
  "Write or overwrite a C# script asset in the Unity project.",
  {
    path: z.string().describe("Project-relative path to the script, e.g. Assets/Scripts/Player.cs"),
    content: z.string().describe("Full source code to write to the file"),
  },
  async (params) => {
    const data = await callUnity("write_script", params);
    return makeTextResult(data);
  }
);

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch((err) => {
  process.stderr.write(`Fatal error: ${err.message}\n`);
  process.exit(1);
});
