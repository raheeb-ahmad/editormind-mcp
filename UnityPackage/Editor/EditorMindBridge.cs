using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
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

        static EditorMindBridge()
        {
            StartListener();
            EditorApplication.quitting += StopListener;
        }

        static void StartListener()
        {
            if (_listener != null)
                return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(ListenPrefix);
                _listener.Start();

                _thread = new Thread(ListenLoop) { IsBackground = true, Name = "EditorMindBridge" };
                _thread.Start();

                Debug.Log("[EditorMind] Listening on " + ListenPrefix);
            }
            catch (Exception ex)
            {
                Debug.LogError("[EditorMind] Failed to start listener: " + ex.Message);
                _listener = null;
            }
        }

        static void StopListener()
        {
            EditorApplication.quitting -= StopListener;

            try { _listener?.Stop(); } catch { /* ignored */ }
            _listener = null;

            try { _thread?.Join(500); } catch { /* ignored */ }
            _thread = null;
        }

        static void ListenLoop()
        {
            while (_listener != null && _listener.IsListening)
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

                    // Dispatch runs on the main thread; block until it completes.
                    string result = null;
                    Exception dispatchError = null;
                    var done = new ManualResetEventSlim(false);

                    EditorApplication.delayCall += () =>
                    {
                        try   { result = EditorMindTools.Dispatch(body); }
                        catch (Exception ex) { dispatchError = ex; }
                        finally { done.Set(); }
                    };

                    done.Wait();

                    if (dispatchError != null)
                    {
                        statusCode = 500;
                        responseJson = JsonUtility.ToJson(new ErrorResponse
                        {
                            ok = false,
                            error = dispatchError.Message
                        });
                    }
                    else
                    {
                        responseJson = result;
                    }
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
