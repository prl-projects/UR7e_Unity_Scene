using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PGCDigitalOutputSync : MonoBehaviour
{
    // Append on PGC_gripper and UR7e_RobotArm to sync gripper opening with digital output (DO4) state.
    public UR7e_RobotArm arm;
    public PGCGripper gripper;
    public int outputIndex = 4;                // Change the output index whereever you like and sync with URSim
    public bool onMeansClosed = true;          // DO4 ON = close
    public float fullStrokeSeconds = 0.75f;    // PGC-140-50 at 100% speed
    float target = -1f;

    void Update()
    {
        var l = arm != null ? arm.Listener : null;
        if (l == null || !l.Connected || gripper == null) return;
        bool on = l.GetDigitalOut(outputIndex);
        target = (on == onMeansClosed) ? 0f : 100f;
        float step = 100f / fullStrokeSeconds * Time.deltaTime;
        gripper.SetOpening(Mathf.MoveTowards(gripper.openingPercent, target, step));
    }
}
