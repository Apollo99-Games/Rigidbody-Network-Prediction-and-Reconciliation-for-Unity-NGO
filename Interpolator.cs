using Unity.VisualScripting;
using UnityEngine;

public class Interpolator : MonoBehaviour
{
    public float interpolationValue = 0.1f;
    private float timeToTarget = 0.1f;
    private float time;
    private Vector3 lastPos;
    private Quaternion lastRot;
    private Vector3 curPos;
    private Quaternion curRot;
    Vector3 nextPos;
    Quaternion nextRot;
    private bool DidReconcile;
    [HideInInspector] public bool IsOwner = false;
    [HideInInspector] public bool IsServer = false;

    void Start()
    {
        nextPos = transform.parent.position;
        nextRot = transform.parent.rotation;
        lastPos = Vector3.zero;
        lastRot = Quaternion.identity;
        timeToTarget = PredictionTimer.Singleton.minTimeBetweenTicks;
        time = PredictionTimer.Singleton.minTimeBetweenTicks;
    }

    void Update() {
        time += Time.deltaTime;
        if (time > timeToTarget) time = timeToTarget;

        if ((!IsOwner && !IsServer) || DidReconcile) {
            curPos = Vector3.Lerp(lastPos, nextPos, time/interpolationValue);
            curRot = Quaternion.Slerp(lastRot, nextRot, time/interpolationValue);
        }
        else {
            curPos = Vector3.Lerp(lastPos, nextPos, time/timeToTarget);
            curRot = Quaternion.Slerp(lastRot, nextRot, time/timeToTarget);
        }

        transform.position = curPos;
        transform.rotation = curRot;  
    }

    public void AddPosition(Vector3 newPosition, bool DidReconcile = false) {
        if (this.DidReconcile && IsOwner) {
            timeToTarget = 0f;
            time = 0f;
        }
        if (IsOwner || IsServer) {
            timeToTarget += PredictionTimer.Singleton.minTimeBetweenTicks - time;
        }

        time = 0f;

        lastPos = curPos;
        nextPos = newPosition;

        this.DidReconcile = DidReconcile;
    }

    public void AddRotation(Quaternion newRotation) {
        lastRot = curRot;
        nextRot = newRotation;
    }

    
}
