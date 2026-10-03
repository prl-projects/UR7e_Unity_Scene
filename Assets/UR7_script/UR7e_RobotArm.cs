using System;
using Assets.Scripts;
using UnityEngine;

/// <summary>
/// UR7e_RobotArm
///
/// 功能：
/// 1. 連線至 URSim 或真實 UR 機械手臂。
/// 2. 透過 UR7ePackageListener 讀取六軸關節角度。
/// 3. 將真實手臂角度同步到 Unity 中的 URDF 模型。
/// 4. 提供 SendCommand() 傳送單行 URScript，方便快速測試手臂指令。
///
/// 注意：
/// - 本腳本主要負責「手臂姿態同步」。
/// - PGC 夾爪完整控制與雙向同步由 PGCCommandConsole 負責。
/// - SendCommand() 僅保留給簡單手臂指令或快速測試使用。
/// </summary>
public class UR7e_RobotArm : MonoBehaviour
{
    // ==================================================
    // Constants
    // ==================================================

    private const int JointCount = 6;

    // 預設 Play 後自動連線，不顯示在 Inspector。
    private const bool ConnectOnStart = true;

    // ==================================================
    // Inspector Settings
    // ==================================================

    [Header("Connection")]
    [Tooltip("URSim 或真實 UR 機械手臂 IP(Read IP from Polyscope)")]
    public string robotIP = "192.168.50.33";

    [Header("Robot Joint Transforms")]
    [Tooltip(
        "依序指定：\n" +
        "0 shoulder_link\n" +
        "1 upper_arm_link\n" +
        "2 forearm_link\n" +
        "3 wrist_1_link\n" +
        "4 wrist_2_link\n" +
        "5 wrist_3_link")]
    public Transform[] transforms = new Transform[JointCount];

    [Header("Rotation Axis")]
    [Tooltip("每個關節在 Unity 中的旋轉軸")]
    public Axis[] rotationAxis = new Axis[JointCount]
    {
        Axis.NegativeY,
        Axis.NegativeY,
        Axis.NegativeY,
        Axis.NegativeY,
        Axis.NegativeY,
        Axis.NegativeY
    };

    [Header("Rotation Offsets")]
    [Tooltip("每個關節額外補償角度，單位：degree")]
    public float[] rotationOffsets = new float[JointCount]
    {
        0f, 0f, 0f, 0f, 0f, 0f
    };

    [Header("Runtime Read Only")]
    [Tooltip("目前從 UR 機械手臂讀取到的角度，單位：degree")]
    public float[] anglesDeg = new float[JointCount];

    // ==================================================
    // Runtime Fields
    // ==================================================

    [HideInInspector]
    public bool isConnected;

    private Quaternion[] startRotations;
    private UR7ePackageListener listener;
    // Lets other scripts (e.g. PGCDigitalOutputSync) read DO/DI and joint data.

    public UR7ePackageListener Listener => listener;

    // ==================================================
    // Unity Lifecycle
    // ==================================================

    private void Start()
    {
        InitializeListener();
        CacheInitialJointRotations();

        if (ConnectOnStart)
            Connect();
    }

    private void Update()
    {
        if (listener == null)
            return;

        isConnected = listener.Connected;

        if (!isConnected)
            return;

        ApplyRobotJointAngles();
    }

    private void OnDestroy()
    {
        listener?.Close();
    }

    // ==================================================
    // Initialization
    // ==================================================

    /// <summary>
    /// 初始化 UR7ePackageListener。
    /// </summary>
    private void InitializeListener()
    {
        if (listener == null)
            listener = new UR7ePackageListener();
    }

    /// <summary>
    /// 快取 URDF 模型各關節的初始旋轉。
    ///
    /// 後續同步時，會先還原初始角度，再套用真實 UR 關節角度，
    /// 避免每一幀累積旋轉誤差。
    /// </summary>
    private void CacheInitialJointRotations()
    {
        startRotations = new Quaternion[JointCount];

        for (int i = 0; i < JointCount; i++)
        {
            if (transforms[i] != null)
                startRotations[i] = transforms[i].localRotation;
        }
    }

    // ==================================================
    // Robot Joint Sync
    // ==================================================

    /// <summary>
    /// 將 UR7ePackageListener 讀取到的六軸角度套用到 Unity URDF 模型。
    /// </summary>
    private void ApplyRobotJointAngles()
    {
        for (int i = 0; i < JointCount; i++)
        {
            if (transforms[i] == null)
                continue;

            anglesDeg[i] = (float)(listener.JointAnglesRad[i] * Mathf.Rad2Deg);

            transforms[i].localRotation = startRotations[i];

            transforms[i].Rotate(
                AxisToVector3(rotationAxis[i]),
                anglesDeg[i] + rotationOffsets[i],
                Space.Self
            );
        }
    }

    // ==================================================
    // Connection Control
    // ==================================================

    /// <summary>
    /// 連線至 URSim 或真實 UR 機械手臂。
    /// </summary>
    public void Connect()
    {
        InitializeListener();

        try
        {
            listener.Connect(robotIP);
            Debug.Log("[UR7e_RobotArm] Connected to UR robot : " + robotIP);
        }
        catch (Exception e)
        {
            Debug.LogError("[UR7e_RobotArm] Connection failed : " + e.Message);
        }
    }

    /// <summary>
    /// 中斷與 UR 機械手臂的連線。
    /// </summary>
    public void Disconnect()
    {
        listener?.Close();
        Debug.Log("[UR7e_RobotArm] Disconnected");
    }

    // ==================================================
    // URScript Sending
    // ==================================================

    /// <summary>
    /// 傳送單行 URScript 指令。
    ///
    /// 注意：
    /// - 本方法不負責 PGC 夾爪同步。
    /// - PGC 夾爪控制請使用 PGCCommandConsole。
    /// </summary>
    public void SendCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        if (listener == null || !listener.Connected)
            return;

        listener.SendCommand(command);
    }

    // ==================================================
    // Utility
    // ==================================================

    /// <summary>
    /// 將自訂 Axis enum 轉成 Unity Vector3。
    /// </summary>
    private static Vector3 AxisToVector3(Axis axis)
    {
        switch (axis)
        {
            case Axis.PositiveX:
                return Vector3.right;

            case Axis.PositiveY:
                return Vector3.up;

            case Axis.PositiveZ:
                return Vector3.forward;

            case Axis.NegativeX:
                return Vector3.left;

            case Axis.NegativeY:
                return Vector3.down;

            case Axis.NegativeZ:
                return Vector3.back;

            default:
                return Vector3.zero;
        }
    }
}

/// <summary>
/// Unity 模型中每個關節使用的旋轉軸方向。
/// </summary>
public enum Axis
{
    PositiveX,
    PositiveY,
    PositiveZ,
    NegativeX,
    NegativeY,
    NegativeZ
}