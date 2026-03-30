using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EditorMind
{
    public static class EditorMindTools
    {
        // ── Entry point ──────────────────────────────────────────────────────

        public static string Dispatch(string requestJson)
        {
            ToolRequest req;
            try { req = JsonUtility.FromJson<ToolRequest>(requestJson); }
            catch (Exception ex) { return Error("Invalid JSON: " + ex.Message); }

            if (string.IsNullOrEmpty(req.tool))
                return Error("Missing 'tool' field.");

            try
            {
                return req.tool switch
                {
                    "get_scene_hierarchy" => GetSceneHierarchy(),
                    "get_selected_object" => GetSelectedObject(),
                    "create_gameobject"   => CreateGameObject(req.@params),
                    "compile_scripts"     => CompileScripts(),
                    "get_compile_errors"  => GetCompileErrors(),
                    "read_script"         => ReadScript(req.@params),
                    "write_script"        => WriteScript(req.@params),
                    _                     => Error("Unknown tool: " + req.tool)
                };
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        // ── Tools ────────────────────────────────────────────────────────────

        static string GetSceneHierarchy()
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var nodes = new List<HierarchyNode>();
            foreach (var root in roots)
                nodes.Add(BuildNode(root));

            var payload = new HierarchyResult
            {
                sceneName = scene.name,
                gameObjects = nodes.ToArray()
            };
            return Ok(JsonUtility.ToJson(payload));
        }

        static HierarchyNode BuildNode(GameObject go)
        {
            var node = new HierarchyNode
            {
                name       = go.name,
                active     = go.activeSelf,
                tag        = go.tag,
                layer      = LayerMask.LayerToName(go.layer),
                components = GetComponentNames(go),
                children   = new HierarchyNode[go.transform.childCount]
            };
            for (int i = 0; i < go.transform.childCount; i++)
                node.children[i] = BuildNode(go.transform.GetChild(i).gameObject);
            return node;
        }

        static string[] GetComponentNames(GameObject go)
        {
            var comps = go.GetComponents<Component>();
            var names = new string[comps.Length];
            for (int i = 0; i < comps.Length; i++)
                names[i] = comps[i] != null ? comps[i].GetType().Name : "(Missing)";
            return names;
        }

        // ─────────────────────────────────────────────────────────────────────

        static string GetSelectedObject()
        {
            var go = Selection.activeGameObject;
            if (go == null)
                return Ok(@"{""selected"":null}");

            var t = go.transform;
            var detail = new SelectedObjectResult
            {
                name       = go.name,
                active     = go.activeSelf,
                tag        = go.tag,
                layer      = LayerMask.LayerToName(go.layer),
                components = GetComponentNames(go),
                position   = new float[] { t.position.x, t.position.y, t.position.z },
                rotation   = new float[] { t.eulerAngles.x, t.eulerAngles.y, t.eulerAngles.z },
                scale      = new float[] { t.localScale.x, t.localScale.y, t.localScale.z },
                path       = GetPath(go)
            };
            return Ok(JsonUtility.ToJson(detail));
        }

        static string GetPath(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            var parent = go.transform.parent;
            while (parent != null)
            {
                sb.Insert(0, parent.name + "/");
                parent = parent.parent;
            }
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────

        static string CreateGameObject(Params p)
        {
            GameObject go;

            if (!string.IsNullOrEmpty(p.primitiveType) &&
                Enum.TryParse<PrimitiveType>(p.primitiveType, out var prim))
            {
                go = GameObject.CreatePrimitive(prim);
            }
            else
            {
                go = new GameObject();
            }

            go.name = string.IsNullOrEmpty(p.name) ? "GameObject" : p.name;

            if (!string.IsNullOrEmpty(p.parent))
            {
                var parentGo = GameObject.Find(p.parent);
                if (parentGo != null)
                    go.transform.SetParent(parentGo.transform, false);
            }

            Undo.RegisterCreatedObjectUndo(go, "EditorMind: Create " + go.name);
            Selection.activeGameObject = go;

            return Ok(JsonUtility.ToJson(new CreateResult
            {
                name = go.name,
                path = GetPath(go)
            }));
        }

        // ─────────────────────────────────────────────────────────────────────

        static string CompileScripts()
        {
            CompilationPipeline.RequestScriptCompilation();
            return Ok(@"{""message"":""Script compilation requested.""}");
        }

        // ─────────────────────────────────────────────────────────────────────

        static string GetCompileErrors()
        {
            var messages = CompilationPipeline.GetAssemblies();
            // Collect errors from the Unity console via CompilerMessages.
            var errors = new List<CompileError>();

            foreach (var assembly in messages)
            {
                foreach (var msg in assembly.compilerMessages)
                {
                    if (msg.type == CompilerMessageType.Error)
                    {
                        errors.Add(new CompileError
                        {
                            message = msg.message,
                            file    = msg.file,
                            line    = msg.line,
                            column  = msg.column
                        });
                    }
                }
            }

            var wrapper = new CompileErrorsResult { errors = errors.ToArray() };
            return Ok(JsonUtility.ToJson(wrapper));
        }

        // ─────────────────────────────────────────────────────────────────────

        static string ReadScript(Params p)
        {
            if (string.IsNullOrEmpty(p.path))
                return Error("'path' is required.");

            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", p.path));
            if (!File.Exists(fullPath))
                return Error("File not found: " + p.path);

            string content = File.ReadAllText(fullPath, Encoding.UTF8);
            return Ok(JsonUtility.ToJson(new ScriptContent { path = p.path, content = content }));
        }

        // ─────────────────────────────────────────────────────────────────────

        static string WriteScript(Params p)
        {
            if (string.IsNullOrEmpty(p.path))
                return Error("'path' is required.");
            if (p.content == null)
                return Error("'content' is required.");

            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", p.path));
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, p.content, Encoding.UTF8);
            AssetDatabase.ImportAsset(p.path, ImportAssetOptions.ForceUpdate);

            return Ok(JsonUtility.ToJson(new WriteResult { path = p.path, written = true }));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static string Ok(string resultJson)
        {
            // Inline-wrap so we avoid double-escaping through JsonUtility.
            return @"{""ok"":true,""result"":" + resultJson + "}";
        }

        static string Error(string message)
        {
            // Escape quotes/backslashes in the message for safety.
            string safe = message.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return @"{""ok"":false,""error"":""" + safe + @"""}";
        }

        // ── Serialisable types ───────────────────────────────────────────────

        [Serializable] class ToolRequest  { public string tool; public Params @params; }
        [Serializable] class Params
        {
            public string name;
            public string parent;
            public string primitiveType;
            public string path;
            public string content;
        }

        [Serializable] class HierarchyResult   { public string sceneName; public HierarchyNode[] gameObjects; }
        [Serializable] class HierarchyNode
        {
            public string name;
            public bool   active;
            public string tag;
            public string layer;
            public string[] components;
            public HierarchyNode[] children;
        }

        [Serializable] class SelectedObjectResult
        {
            public string   name;
            public bool     active;
            public string   tag;
            public string   layer;
            public string[] components;
            public float[]  position;
            public float[]  rotation;
            public float[]  scale;
            public string   path;
        }

        [Serializable] class CreateResult       { public string name; public string path; }
        [Serializable] class CompileErrorsResult { public CompileError[] errors; }
        [Serializable] class CompileError
        {
            public string message;
            public string file;
            public int    line;
            public int    column;
        }
        [Serializable] class ScriptContent      { public string path; public string content; }
        [Serializable] class WriteResult        { public string path; public bool written; }
    }
}
