using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// PGCCommandConsole
///
/// 功能：
/// 1. 讀取 DH-PGC template script
/// 2. 將外部控制流程插入 {{PROGRAM_BODY}}
/// 3. 透過 TCP 30001 將完整 URScript 送給 UR7e
/// 4. 啟動 Unity Callback Server，接收 URScript 回傳的同步訊號
/// 5. 根據 UR7e 實際執行到的 PGC 指令，同步 Unity 夾爪模型
///
/// 注意：
/// - dh_pgc_template.script 必須放在 Assets/URScripts
/// - template 內必須包含 {{PROGRAM_BODY}}
/// - unityListenIP 必須填 Unity 電腦的 IP，不是 UR7e 的 IP
/// - Windows 防火牆需允許 Unity 接收 unityListenPort
/// </summary>
public class PGCCommandConsole : MonoBehaviour
{
    // ==================================================
    // Inspector Settings
    // ==================================================

    [Header("UR Robot")]
    public string robotIP = "192.168.50.33";
    public int port = 30001;

    [Header("Unity Callback Server")]
    public string unityListenIP = "192.168.50.39";
    public int unityListenPort = 5000;

    [Header("Template File")]
    public string templateFileName = "dh_pgc_template.script";

    [Header("Unity Gripper")]
    public PGCGripper unityGripper;

    // PGC-140-50 在 100% 速度下，全行程開合時間約 0.75 秒
    private const float FullStrokeTimeAt100Speed = 0.75f;

    [Header("Runtime Read Only")]
    [ReadOnly, SerializeField] private float currentPositionPercent = 100f;
    [ReadOnly, SerializeField] private float currentSpeedPercent = 100f;
    [ReadOnly, SerializeField] private float currentForcePercent = 100f;

    // ==================================================
    // Runtime Fields
    // ==================================================

    private TcpListener callbackServer;
    private Thread callbackThread;
    private volatile bool callbackRunning;

    private Coroutine gripperMoveCoroutine;

    // Callback thread 不能直接操作 Unity 物件，
    // 因此先把事件放進 Queue，再由主執行緒 Update() 處理。
    private readonly ConcurrentQueue<PGCSyncEvent> syncEvents =
        new ConcurrentQueue<PGCSyncEvent>();

    // ==================================================
    // Regex Patterns
    // ==================================================
    // 用來偵測使用者 URScript 中的 PGC 指令，
    // 並在指令前自動插入 socket_send_line() 同步訊號。

    private static readonly Regex SetPositionRegex =
        new Regex(
            @"dh_pgc_set_position\s*\(\s*\d+\s*,\s*([0-9]+(?:\.[0-9]+)?)\s*\)",
            RegexOptions.Compiled
        );

    private static readonly Regex SetSpeedRegex =
        new Regex(
            @"dh_pgc_set_speed\s*\(\s*\d+\s*,\s*([0-9]+(?:\.[0-9]+)?)\s*\)",
            RegexOptions.Compiled
        );

    private static readonly Regex SetForceRegex =
        new Regex(
            @"dh_pgc_set_force\s*\(\s*\d+\s*,\s*([0-9]+(?:\.[0-9]+)?)\s*\)",
            RegexOptions.Compiled
        );

    private static readonly Regex PgcMoveRegex =
        new Regex(
            @"pgc_move\s*\(\s*([0-9]+(?:\.[0-9]+)?)\s*,\s*([0-9]+(?:\.[0-9]+)?)\s*,\s*([0-9]+(?:\.[0-9]+)?)\s*\)",
            RegexOptions.Compiled
        );

    // ==================================================
    // Unity Lifecycle
    // ==================================================

    private void Update()
    {
        ProcessSyncEvents();
    }

    private void OnDestroy()
    {
        StopCallbackServer();
    }

    // ==================================================
    // Public API
    // ==================================================

    /// <summary>
    /// 將外部傳入的 URScript 控制流程包進 DH-PGC template 後送給 UR7e。
    ///
    /// programBody 範例：
    /// movej([...], a=0.5, v=0.2)
    /// dh_pgc_set_speed(1, 50)
    /// dh_pgc_set_force(1, 80)
    /// dh_pgc_set_position(1, 100.0)
    /// </summary>
    public void SendProgram(string programBody)
    {
        string templatePath = Path.Combine(
            Application.dataPath,
            "URScripts",
            templateFileName
        );

        if (!File.Exists(templatePath))
        {
            Debug.LogError("找不到 template：" + templatePath);
            return;
        }

        string template = File.ReadAllText(templatePath, Encoding.UTF8);

        if (!template.Contains("{{PROGRAM_BODY}}"))
        {
            Debug.LogError("Template 裡找不到 {{PROGRAM_BODY}}");
            return;
        }

        StartCallbackServer();

        string bodyWithNotify = AddUnityNotifyToUserScript(programBody);
        string body = IndentUserScript(bodyWithNotify);
        string finalScript = template.Replace("{{PROGRAM_BODY}}", body);

        SendURScript(finalScript);
    }

    // ==================================================
    // Script Generation
    // ==================================================

    /// <summary>
    /// 在使用者的 URScript 控制流程中，自動插入 Unity 同步訊號。
    ///
    /// 例如原本：
    /// dh_pgc_set_position(1, 100.0)
    ///
    /// 會變成：
    /// socket_send_line("PGC_SET_POSITION 100.0", "unity_sync")
    /// dh_pgc_set_position(1, 100.0)
    ///
    /// 這樣 Unity 可以在 UR7e 真正執行到該行前收到同步事件。
    /// </summary>
    private string AddUnityNotifyToUserScript(string scriptBody)
    {
        string[] lines = NormalizeLineEndings(scriptBody).Split('\n');
        StringBuilder sb = new StringBuilder();

        AppendCallbackSocketOpen(sb);
        AppendInitialGripperStateReadback(sb);

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();

            AppendSpeedNotifyIfNeeded(sb, line);
            AppendForceNotifyIfNeeded(sb, line);

            if (AppendPgcMoveNotifyIfNeeded(sb, line) == false)
            {
                AppendPositionNotifyIfNeeded(sb, line);
            }

            sb.AppendLine(rawLine);
        }

        AppendCallbackSocketClose(sb);

        return sb.ToString();
    }

    /// <summary>
    /// 因為使用者程式會被插入到 def external_arm_pgc_test(): 裡面，
    /// 所以每一行都要加上兩格縮排。
    /// </summary>
    private string IndentUserScript(string scriptBody)
    {
        string[] lines = NormalizeLineEndings(scriptBody).Split('\n');
        StringBuilder sb = new StringBuilder();

        foreach (string line in lines)
        {
            sb.AppendLine(
                string.IsNullOrWhiteSpace(line)
                    ? ""
                    : "  " + line.Trim()
            );
        }

        return sb.ToString();
    }

    private void AppendCallbackSocketOpen(StringBuilder sb)
    {
        sb.AppendLine($"socket_open(\"{unityListenIP}\", {unityListenPort}, \"unity_sync\")");
        sb.AppendLine("sleep(0.1)");
    }

    /// <summary>
    /// 程式開始時，先讀取真實夾爪目前狀態一次。
    /// Position 會瞬間同步；Speed / Force 只更新 Inspector 顯示。
    /// </summary>
    private void AppendInitialGripperStateReadback(StringBuilder sb)
    {
        sb.AppendLine("initial_pgc_position = dh_pgc_get_position(1)");
        sb.AppendLine("socket_send_line(str_cat(\"PGC_INIT_POSITION \", initial_pgc_position), \"unity_sync\")");

        sb.AppendLine("initial_pgc_speed = dh_pgc_get_speed(1)");
        sb.AppendLine("socket_send_line(str_cat(\"PGC_SET_SPEED \", initial_pgc_speed), \"unity_sync\")");

        sb.AppendLine("initial_pgc_force = dh_pgc_get_force(1)");
        sb.AppendLine("socket_send_line(str_cat(\"PGC_SET_FORCE \", initial_pgc_force), \"unity_sync\")");

        sb.AppendLine("sleep(0.1)");
    }

    private void AppendCallbackSocketClose(StringBuilder sb)
    {
        sb.AppendLine("sleep(0.1)");
        sb.AppendLine("socket_close(\"unity_sync\")");
    }

    private void AppendSpeedNotifyIfNeeded(StringBuilder sb, string line)
    {
        Match match = SetSpeedRegex.Match(line);

        if (match.Success)
        {
            string speed = match.Groups[1].Value;
            sb.AppendLine($"socket_send_line(\"PGC_SET_SPEED {speed}\", \"unity_sync\")");
        }
    }

    private void AppendForceNotifyIfNeeded(StringBuilder sb, string line)
    {
        Match match = SetForceRegex.Match(line);

        if (match.Success)
        {
            string force = match.Groups[1].Value;
            sb.AppendLine($"socket_send_line(\"PGC_SET_FORCE {force}\", \"unity_sync\")");
        }
    }

    private bool AppendPgcMoveNotifyIfNeeded(StringBuilder sb, string line)
    {
        Match match = PgcMoveRegex.Match(line);

        if (!match.Success)
            return false;

        string position = match.Groups[1].Value;
        string speed = match.Groups[2].Value;
        string force = match.Groups[3].Value;

        sb.AppendLine($"socket_send_line(\"PGC_SET_SPEED {speed}\", \"unity_sync\")");
        sb.AppendLine($"socket_send_line(\"PGC_SET_FORCE {force}\", \"unity_sync\")");
        sb.AppendLine($"socket_send_line(\"PGC_SET_POSITION {position}\", \"unity_sync\")");

        return true;
    }

    private void AppendPositionNotifyIfNeeded(StringBuilder sb, string line)
    {
        Match match = SetPositionRegex.Match(line);

        if (match.Success)
        {
            string position = match.Groups[1].Value;
            sb.AppendLine($"socket_send_line(\"PGC_SET_POSITION {position}\", \"unity_sync\")");
        }
    }

    private string NormalizeLineEndings(string text)
    {
        return text
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");
    }

    // ==================================================
    // Callback Server
    // ==================================================

    /// <summary>
    /// 啟動 Unity 端 TCP Server。
    /// URScript 會透過 socket_send_line() 把同步訊號送回這個 Server。
    /// </summary>
    private void StartCallbackServer()
    {
        StopCallbackServer();

        try
        {
            callbackServer = new TcpListener(IPAddress.Any, unityListenPort);
            callbackServer.Start();

            callbackRunning = true;

            callbackThread = new Thread(CallbackServerLoop);
            callbackThread.IsBackground = true;
            callbackThread.Start();

            Debug.Log("[PGC Sync] Callback server started on port " + unityListenPort);
        }
        catch (Exception e)
        {
            Debug.LogError("[PGC Sync] Callback server start failed: " + e.Message);
        }
    }

    private void StopCallbackServer()
    {
        callbackRunning = false;

        try { callbackServer?.Stop(); } catch { }

        callbackServer = null;
    }

    /// <summary>
    /// 背景執行緒：等待 UR7e 連回 Unity，並接收同步訊息。
    /// </summary>
    private void CallbackServerLoop()
    {
        try
        {
            using (TcpClient client = callbackServer.AcceptTcpClient())
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                while (callbackRunning && client.Connected)
                {
                    string line = reader.ReadLine();

                    if (line == null)
                        break;

                    HandleCallbackMessage(line.Trim());
                }
            }
        }
        catch (Exception e)
        {
            if (callbackRunning)
                Debug.LogWarning("[PGC Sync] Callback server stopped: " + e.Message);
        }
    }

    /// <summary>
    /// 解析 URScript 回傳訊息。
    ///
    /// 支援格式：
    /// PGC_INIT_POSITION 100.0
    /// PGC_SET_POSITION 0.0
    /// PGC_SET_SPEED 50
    /// PGC_SET_FORCE 80
    /// </summary>
    private void HandleCallbackMessage(string message)
    {
        Debug.Log("[PGC Sync] Received: " + message);

        string[] parts = message.Split(' ');

        if (parts.Length < 2)
            return;

        if (!float.TryParse(
                parts[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value))
        {
            return;
        }

        if (message.StartsWith("PGC_SET_SPEED"))
            syncEvents.Enqueue(new PGCSyncEvent(PGCSyncEventType.Speed, value));
        else if (message.StartsWith("PGC_SET_FORCE"))
            syncEvents.Enqueue(new PGCSyncEvent(PGCSyncEventType.Force, value));
        else if (message.StartsWith("PGC_INIT_POSITION"))
            syncEvents.Enqueue(new PGCSyncEvent(PGCSyncEventType.InitPosition, value));
        else if (message.StartsWith("PGC_SET_POSITION"))
            syncEvents.Enqueue(new PGCSyncEvent(PGCSyncEventType.Position, value));
    }

    /// <summary>
    /// 在 Unity 主執行緒處理 callback thread 收到的同步事件。
    /// </summary>
    private void ProcessSyncEvents()
    {
        while (syncEvents.TryDequeue(out PGCSyncEvent syncEvent))
        {
            switch (syncEvent.type)
            {
                case PGCSyncEventType.Speed:
                    SetSpeed(syncEvent.value);
                    break;

                case PGCSyncEventType.Force:
                    SetForce(syncEvent.value);
                    break;

                case PGCSyncEventType.InitPosition:
                    SetUnityGripperInstant(syncEvent.value);
                    break;

                case PGCSyncEventType.Position:
                    MoveUnityGripper(syncEvent.value);
                    break;
            }
        }
    }

    // ==================================================
    // Unity Gripper Sync
    // ==================================================

    private void SetSpeed(float speedPercent)
    {
        currentSpeedPercent = Mathf.Clamp(speedPercent, 1f, 100f);
        Debug.Log("[PGC Sync] Current speed = " + currentSpeedPercent + "%");
    }

    private void SetForce(float forcePercent)
    {
        currentForcePercent = Mathf.Clamp(forcePercent, 20f, 100f);
        Debug.Log("[PGC Sync] Current force = " + currentForcePercent + "%");
    }

    private void SetPositionReadOnly(float positionPercent)
    {
        currentPositionPercent = Mathf.Clamp(positionPercent, 0f, 100f);
    }

    /// <summary>
    /// 初始位置同步：直接瞬間對齊，不播放動畫。
    /// </summary>
    private void SetUnityGripperInstant(float targetPercent)
    {
        if (gripperMoveCoroutine != null)
        {
            StopCoroutine(gripperMoveCoroutine);
            gripperMoveCoroutine = null;
        }

        SetPositionReadOnly(targetPercent);

        if (unityGripper != null)
            unityGripper.SetOpening(targetPercent);

        Debug.Log("[PGC Sync] Initial position synced instantly = " + targetPercent + "%");
    }

    /// <summary>
    /// 一般位置同步：依照目前速度百分比播放 Unity 夾爪動畫。
    /// </summary>
    private void MoveUnityGripper(float targetPercent)
    {
        if (unityGripper == null)
        {
            SetPositionReadOnly(targetPercent);
            return;
        }

        if (gripperMoveCoroutine != null)
            StopCoroutine(gripperMoveCoroutine);

        gripperMoveCoroutine = StartCoroutine(
            MoveUnityGripperRoutine(targetPercent)
        );
    }

    private IEnumerator MoveUnityGripperRoutine(float targetPercent)
    {
        float startPercent = unityGripper.openingPercent;
        float distanceRatio = Mathf.Abs(targetPercent - startPercent) / 100f;

        float speedRatio = Mathf.Clamp(currentSpeedPercent, 1f, 100f) / 100f;

        float duration = Mathf.Max(
            0.01f,
            FullStrokeTimeAt100Speed * distanceRatio / speedRatio
        );

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            float current = Mathf.Lerp(startPercent, targetPercent, t);

            unityGripper.SetOpening(current);
            SetPositionReadOnly(current);

            yield return null;
        }

        unityGripper.SetOpening(targetPercent);
        SetPositionReadOnly(targetPercent);

        gripperMoveCoroutine = null;
    }

    // ==================================================
    // URScript Sender
    // ==================================================

    /// <summary>
    /// 透過 Primary Client Interface 30001 將完整 URScript 送給 UR7e。
    /// </summary>
    private void SendURScript(string script)
    {
        try
        {
            script = NormalizeLineEndings(script);

            using (Socket socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp))
            {
                socket.Connect(robotIP, port);

                byte[] data = Encoding.UTF8.GetBytes(script + "\n");
                socket.Send(data);

                Thread.Sleep(1000);

                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }

            Debug.Log("User Script 已送出");
        }
        catch (Exception e)
        {
            Debug.LogError("User Script 送出失敗：" + e.Message);
        }
    }

    // ==================================================
    // Internal Types
    // ==================================================

    private enum PGCSyncEventType
    {
        Speed,
        Force,
        InitPosition,
        Position
    }

    private struct PGCSyncEvent
    {
        public PGCSyncEventType type;
        public float value;

        public PGCSyncEvent(PGCSyncEventType type, float value)
        {
            this.type = type;
            this.value = value;
        }
    }
}

/// <summary>
/// ReadOnlyAttribute
///
/// 讓欄位顯示在 Inspector，但不能被手動修改。
/// </summary>
public class ReadOnlyAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(
        Rect position,
        SerializedProperty property,
        GUIContent label)
    {
        bool oldGUIState = GUI.enabled;

        GUI.enabled = false;
        EditorGUI.PropertyField(position, property, label, true);
        GUI.enabled = oldGUIState;
    }
}
#endif