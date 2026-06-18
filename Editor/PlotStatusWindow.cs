#nullable enable
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Plot.Editor
{
    /// <summary>
    /// Editor-only status window for the Plot SDK. Surfaces the protocol version
    /// the runtime speaks, lets a developer stash an app key + server URL while
    /// integrating, and pings the connect endpoint to confirm reachability.
    /// Opened from <c>Window/Plot/Status</c>.
    /// </summary>
    public class PlotStatusWindow : EditorWindow
    {
        private const string AppKeyPref = "Plot.Editor.AppKey";
        private const string ApiUrlPref = "Plot.Editor.ApiUrl";
        private const string DefaultApiUrl = "https://api.plot.ws";
        private const string DocsUrl = "https://plot.ws/docs";
        private const string DocsUnityUrl = "https://plot.ws/docs/sdks/unity";

        private string _appKey = "";
        private string _apiUrl = DefaultApiUrl;

        private bool _testing;
        private string _status = "";
        private MessageType _statusKind = MessageType.None;
        private UnityWebRequest? _request;

        [MenuItem("Window/Plot/Status")]
        public static void Open()
        {
            var window = GetWindow<PlotStatusWindow>(utility: false, title: "Plot");
            window.minSize = new Vector2(360, 280);
            window.Show();
        }

        private void OnEnable()
        {
            _appKey = EditorPrefs.GetString(AppKeyPref, "");
            _apiUrl = EditorPrefs.GetString(ApiUrlPref, DefaultApiUrl);
        }

        private void OnDisable()
        {
            CancelRequest();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Plot Multiplayer SDK", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"Protocol: X-Plot-Protocol: {Plot.Protocol.Version.SchemaVersion}",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _appKey = EditorGUILayout.TextField("App Key", _appKey);
            _apiUrl = EditorGUILayout.TextField("Server URL", _apiUrl);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(AppKeyPref, _appKey);
                EditorPrefs.SetString(ApiUrlPref, _apiUrl);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_testing))
            {
                if (GUILayout.Button(_testing ? "Testing…" : "Test connection"))
                {
                    StartTest();
                }
            }
            using (new EditorGUI.DisabledScope(_apiUrl == DefaultApiUrl && _appKey.Length == 0))
            {
                if (GUILayout.Button("Reset", GUILayout.MaxWidth(80)))
                {
                    ResetFields();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_status.Length > 0)
            {
                EditorGUILayout.HelpBox(_status, _statusKind);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Documentation", EditorStyles.boldLabel);
            if (EditorGUILayout.LinkButton("Plot docs"))
            {
                Application.OpenURL(DocsUrl);
            }
            if (EditorGUILayout.LinkButton("Unity SDK guide"))
            {
                Application.OpenURL(DocsUnityUrl);
            }
            EditorGUILayout.Space();
        }

        private void StartTest()
        {
            if (_testing) return;

            if (string.IsNullOrWhiteSpace(_appKey))
            {
                SetStatus("Enter an app key before testing.", MessageType.Warning);
                return;
            }
            if (!Uri.TryCreate(_apiUrl, UriKind.Absolute, out var baseUri))
            {
                SetStatus($"Server URL is not a valid absolute URL: {_apiUrl}", MessageType.Error);
                return;
            }

            var connectUrl = new Uri(baseUri, "/v1/connect").ToString();
            var body = JsonUtility.ToJson(new ConnectProbe { appKey = _appKey, playerId = "editor-probe" });

            var request = new UnityWebRequest(connectUrl, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10,
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-Plot-Protocol", Plot.Protocol.Version.SchemaVersion);

            _request = request;
            _testing = true;
            SetStatus($"Connecting to {connectUrl}…", MessageType.None);

            var op = request.SendWebRequest();
            op.completed += _ => OnTestCompleted();
        }

        private void OnTestCompleted()
        {
            var request = _request;
            _testing = false;
            _request = null;
            if (request == null) return;

            try
            {
                switch (request.result)
                {
                    case UnityWebRequest.Result.Success:
                        SetStatus(
                            $"Reachable — server replied {(long)request.responseCode}.",
                            MessageType.Info);
                        break;
                    case UnityWebRequest.Result.ProtocolError:
                        SetStatus(
                            $"Reached server but it rejected the request (HTTP {(long)request.responseCode}). " +
                            "Check the app key.",
                            MessageType.Warning);
                        break;
                    default:
                        SetStatus($"Connection failed: {request.error}", MessageType.Error);
                        break;
                }
            }
            finally
            {
                request.Dispose();
                Repaint();
            }
        }

        private void CancelRequest()
        {
            if (_request != null)
            {
                _request.Abort();
                _request.Dispose();
                _request = null;
            }
            _testing = false;
        }

        private void ResetFields()
        {
            CancelRequest();
            _appKey = "";
            _apiUrl = DefaultApiUrl;
            EditorPrefs.DeleteKey(AppKeyPref);
            EditorPrefs.SetString(ApiUrlPref, DefaultApiUrl);
            SetStatus("", MessageType.None);
        }

        private void SetStatus(string message, MessageType kind)
        {
            _status = message;
            _statusKind = kind;
            Repaint();
        }

        [Serializable]
        private struct ConnectProbe
        {
            public string appKey;
            public string playerId;
        }
    }
}
