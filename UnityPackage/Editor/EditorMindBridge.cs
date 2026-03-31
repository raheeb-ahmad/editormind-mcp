using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace EditorMind
{
    [InitializeOnLoad]
    public static class EditorMindBridge
    {
        const string ListenPrefix = "http://localhost:6400/";

        static HttpListener _listener;
        static Thread _thread;
        static readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        static bool _shuttingDown = false;

        static EditorMindBridge()
        {
            EditorApplication.update += DrainMainThreadQueue;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            StartListener();
            EditorApplication.quitting += StopListener;
        }

        static void DrainMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception ex) { Debug.LogWarning("[EditorMind] Main thread action threw: " + ex.Message); }
            }
        }

        static void OnBeforeAssemblyReload()
        {
            EditorApplication.update -= DrainMainThreadQueue;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            StopListener();

            // Drain and cancel any pending work so background threads don't hang.
            while (_mainThreadQueue.TryDequeue(out _)) { }
        }

        static void StartListener()
        {
            // Dispose any existing listener first so the port is released before we try to rebind.
            DisposeListener();

            TryStartOnce();
        }

        static void TryStartOnce()
        {
            // Wait up to 2 seconds (10 x 200 ms) for the port to become free.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    var l = new HttpListener();
                    l.Prefixes.Add(ListenPrefix);
                    l.Start();

                    // Committed — store and kick off the listen thread.
                    _listener = l;
                    _shuttingDown = false;

                    _thread = new Thread(ListenLoop) { IsBackground = true, Name = "EditorMindBridge" };
                    _thread.Start();

                    Debug.Log("[EditorMind] Listening on " + ListenPrefix);
                    return;
                }
                catch (HttpListenerException)
                {
                    // Port still occupied — wait and retry.
                    Thread.Sleep(200);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[EditorMind] Failed to start listener: " + ex.Message);
                    _listener = null;
                    return;
                }
            }

            // All 10 attempts failed — try once more after a longer delay.
            Thread.Sleep(500);
            try
            {
                var l = new HttpListener();
                l.Prefixes.Add(ListenPrefix);
                l.Start();

                _listener = l;
                _shuttingDown = false;

                _thread = new Thread(ListenLoop) { IsBackground = true, Name = "EditorMindBridge" };
                _thread.Start();

                Debug.Log("[EditorMind] Listening on " + ListenPrefix);
            }
            catch (Exception ex)
            {
                Debug.LogError("[EditorMind] Failed to start listener after retries: " + ex.Message);
                _listener = null;
            }
        }

        static void DisposeListener()
        {
            _shuttingDown = true;

            var l = _listener;
            _listener = null;

            if (l != null)
            {
                try { if (l.IsListening) l.Stop(); } catch { /* ignored */ }
                try { l.Close(); } catch { /* ignored */ }
            }

            try { _thread?.Join(500); } catch { /* ignored */ }
            _thread = null;
        }

        static void StopListener()
        {
            EditorApplication.quitting -= StopListener;
            DisposeListener();
        }

        static void ListenLoop()
        {
            while (!_shuttingDown && _listener != null && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped — exit cleanly.
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[EditorMind] Accept error: " + ex.Message);
                    break;
                }

                // Hand off to the thread pool so the listen loop stays responsive.
                ThreadPool.QueueUserWorkItem(_ => HandleContext(ctx));
            }
        }

        static void HandleContext(HttpListenerContext ctx)
        {
            string responseJson;
            int statusCode = 200;

            try
            {
                if (ctx.Request.HttpMethod != "POST")
                {
                    statusCode = 405;
                    responseJson = @"{""ok"":false,""error"":""Method not allowed""}";
                }
                else
                {
                    string body;
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                        body = reader.ReadToEnd();

                    // Enqueue dispatch onto the main thread via EditorApplication.update so it
                    // completes even when Unity is not in focus (delayCall throttles when unfocused).
                    var tcs = new TaskCompletionSource<string>();

                    _mainThreadQueue.Enqueue(() =>
                    {
                        try   { tcs.SetResult(EditorMindTools.Dispatch(body)); }
                        catch (Exception ex) { tcs.SetException(ex); }
                    });

                    // Block the pool thread until the main thread finishes the dispatch.
                    responseJson = tcs.Task.GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                responseJson = JsonUtility.ToJson(new ErrorResponse { ok = false, error = ex.Message });
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(responseJson);
                ctx.Response.StatusCode = statusCode;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[EditorMind] Failed to write response: " + ex.Message);
            }
        }

        [Serializable]
        class ErrorResponse
        {
            public bool ok;
            public string error;
        }
    }
}
