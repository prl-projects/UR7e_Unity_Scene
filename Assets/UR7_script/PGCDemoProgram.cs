using UnityEngine;

/// <summary>
/// PGCDemoProgram
///
/// 功能：
/// 1. 示範如何透過 PGCCommandConsole 傳送 URScript。
/// 2. 測試 DH Robotics 提供的 PGC Script API。
/// 3. 作為日後撰寫夾爪控制程式的範例。
///
/// 使用方式：
/// 右鍵元件 → Run Full DH PGC Function Test
///
/// 注意：
/// 本程式僅負責建立 URScript 字串，
/// 實際送出與 Unity 同步由 PGCCommandConsole 負責。
/// </summary>
public class PGCDemoProgram : MonoBehaviour
{
    // ==================================================
    // Inspector
    // ==================================================

    [Header("PGC Command Console")]
    public PGCCommandConsole pgcConsole;

    // ==================================================
    // Demo Program
    // ==================================================

    /// <summary>
    /// 執行完整 DH Robotics PGC Function 測試。
    ///
    /// 測試內容包含：
    /// 1. Scan
    /// 2. Connect
    /// 3. Activate
    /// 4. Speed
    /// 5. Force
    /// 6. Position
    /// 7. Get Position
    /// 8. Object Detection
    /// 9. Wait Function
    ///
    /// 測試過程會搭配幾個手臂 MoveJ 點位，
    /// 方便確認 URScript 是否成功送出以及手臂、夾爪是否同步。
    /// </summary>
    [ContextMenu("Run Full DH PGC Function Test")]
    public void RunFullDHFunctionTest()
    {
        if (pgcConsole == null)
        {
            Debug.LogError("尚未指定 PGCCommandConsole");
            return;
        }

        string programBody =
@"

#################################################
# 1. Scan Device
#################################################

scan_result = dh_pgc_scan()
sleep(1.0)

scanned_result = dh_pgc_is_scanned()

#################################################
# 2. Connect Gripper
#################################################

connected_result = dh_pgc_connect(1)
sleep(1.0)

is_connected_result = dh_pgc_is_connected(1)

#################################################
# 3. Activate Gripper
#################################################

activated_result = dh_pgc_is_activated(1)

if (False == activated_result):
  dh_pgc_set_activate(1)
  dh_pgc_wait_until_activated(1)
end

is_activated_result = dh_pgc_is_activated(1)

#################################################
# 4. Test Speed & Force
#################################################

dh_pgc_set_speed(1, 100)
speed_100 = dh_pgc_get_speed(1)

dh_pgc_set_force(1, 50)
force_50 = dh_pgc_get_force(1)

#################################################
# 5. Open Gripper
#################################################

movej([0.25, -1.76, -2.17, -0.79, 1.58, -1.31], a=0.5, v=0.2)
sleep(1.0)

dh_pgc_set_position(1, 100.0)

dh_pgc_wait_until_gripped_or_arrived(1)
dh_pgc_wait_until_idle(1)

pos_open = dh_pgc_get_position(1)

sleep(1.0)

#################################################
# 6. Close Gripper
#################################################

movej([0.45, -1.54, -2.11, -1.07, 1.58, -1.12], a=0.5, v=0.2)
sleep(1.0)

dh_pgc_set_speed(1, 50)
speed_50 = dh_pgc_get_speed(1)

dh_pgc_set_position(1, 0.0)

dh_pgc_wait_until_gripped_or_arrived(1)
dh_pgc_wait_until_idle(1)

pos_close = dh_pgc_get_position(1)

#################################################
# 7. Detect Object Status
#################################################

gripped_result = dh_pgc_is_gripped(1)
dropped_result = dh_pgc_is_dropped(1)

sleep(1.0)

#################################################
# 8. Half Open Gripper
#################################################

movej([0.45, -1.56, -2.18, -0.99, 1.58, -1.11], a=0.5, v=0.2)
sleep(1.0)

dh_pgc_set_speed(1, 25)
speed_25 = dh_pgc_get_speed(1)

dh_pgc_set_force(1, 100)
force_100 = dh_pgc_get_force(1)

dh_pgc_set_position(1, 50.0)

dh_pgc_wait_until_gripped_or_arrived(1)
dh_pgc_wait_until_idle(1)

pos_mid = dh_pgc_get_position(1)

sleep(1.0)

#################################################
# 9. Return To Fully Open
#################################################

dh_pgc_set_position(1, 100.0)

dh_pgc_wait_until_gripped_or_arrived(1)
dh_pgc_wait_until_idle(1)

";

        // 將 URScript 傳送給 PGCCommandConsole，
        // 後續由 Console 負責：
        // 1. 插入 Template
        // 2. 加入 Unity 同步訊號
        // 3. 傳送至 UR7e
        pgcConsole.SendProgram(programBody);
    }
}