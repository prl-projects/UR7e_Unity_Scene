using UnityEngine;

/// <summary>
/// PGCGripper
///
/// 功能：
/// 1. 控制 Unity 中 PGC-140-50 夾爪模型開合
/// 2. 支援 Inspector Opening Percent 拉桿即時控制
/// 3. 提供 SetOpening(percent) 給其他控制腳本呼叫
///
/// 對應真實夾爪：
/// - 0%   = 完全閉合
/// - 100% = 完全開啟
///
/// 目前模型設定：
/// - GripperX+ 關閉方向為 local Z = -1
/// - PGC-140-50 父物件 Scale = 0.001
/// - OBJ 模型本身仍以 mm 為單位，因此位移不再乘 0.001
/// </summary>
public class PGCGripper : MonoBehaviour
{
    // ==================================================
    // Inspector Settings
    // ==================================================

    [Header("Finger Transforms")]
    [Tooltip("GripperX+ 爪子 Transform")]
    public Transform gripperXPlus;

    [Tooltip("GripperX- 爪子 Transform")]
    public Transform gripperXMinus;

    [Header("Opening Setting")]
    [Tooltip("夾爪開合百分比：0 = 完全閉合，100 = 完全開啟")]
    [Range(0f, 100f)]
    public float openingPercent = 100f;

    [Tooltip("PGC-140-50 最大開口，單位：mm")]
    public float maxOpening = 50f;

    [Header("Move Axis")]
    [Tooltip("GripperX+ 朝閉合方向的 local 軸向，目前為 Z = -1")]
    public Vector3 plusCloseAxis = new Vector3(0f, 0f, -1f);

    // ==================================================
    // Runtime Fields
    // ==================================================

    // 記錄模型在「完全開啟」時的初始位置。
    private Vector3 plusOpenPos;
    private Vector3 minusOpenPos;

    // 用來偵測 Inspector 中 openingPercent 是否被手動修改。
    private float lastOpeningPercent = -1f;

    // ==================================================
    // Unity Lifecycle
    // ==================================================

    private void Awake()
    {
        CacheOpenPositions();
    }

    private void Start()
    {
        SetOpening(openingPercent);
        lastOpeningPercent = openingPercent;
    }

    private void Update()
    {
        UpdateOpeningFromInspector();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
            return;

        SetOpening(openingPercent);
    }
#endif

    // ==================================================
    // Public API
    // ==================================================

    /// <summary>
    /// 設定 Unity 夾爪模型開合百分比。
    ///
    /// percent:
    /// - 0   = 完全閉合
    /// - 100 = 完全開啟
    /// </summary>
    public void SetOpening(float percent)
    {
        if (!HasValidFingerTransforms())
            return;

        openingPercent = Mathf.Clamp(percent, 0f, 100f);

        float oneSideMove = CalculateOneSideMove(openingPercent);
        Vector3 move = plusCloseAxis.normalized * oneSideMove;

        gripperXPlus.localPosition = plusOpenPos + move;
        gripperXMinus.localPosition = minusOpenPos - move;
    }

    // ==================================================
    // Initialization
    // ==================================================

    /// <summary>
    /// 快取左右夾爪在完全開啟狀態下的位置。
    /// 之後所有開合位移都會以這個位置作為基準。
    /// </summary>
    private void CacheOpenPositions()
    {
        if (gripperXPlus != null)
            plusOpenPos = gripperXPlus.localPosition;

        if (gripperXMinus != null)
            minusOpenPos = gripperXMinus.localPosition;
    }

    // ==================================================
    // Inspector Runtime Control
    // ==================================================

    /// <summary>
    /// 支援 Play Mode 中直接拖曳 Inspector 的 Opening Percent。
    /// 若數值改變，就即時更新夾爪模型。
    /// </summary>
    private void UpdateOpeningFromInspector()
    {
        if (Mathf.Abs(lastOpeningPercent - openingPercent) <= 0.001f)
            return;

        SetOpening(openingPercent);
        lastOpeningPercent = openingPercent;
    }

    // ==================================================
    // Calculation
    // ==================================================

    /// <summary>
    /// 根據開合百分比計算單側爪子需要移動的距離。
    ///
    /// maxOpening 是總開口寬度，因此單側爪子只移動 maxOpening / 2。
    /// </summary>
    private float CalculateOneSideMove(float percent)
    {
        float closeRatio = 1f - percent / 100f;
        return closeRatio * (maxOpening / 2f);
    }

    /// <summary>
    /// 確認左右爪子的 Transform 是否都已指定。
    /// </summary>
    private bool HasValidFingerTransforms()
    {
        return gripperXPlus != null && gripperXMinus != null;
    }
}