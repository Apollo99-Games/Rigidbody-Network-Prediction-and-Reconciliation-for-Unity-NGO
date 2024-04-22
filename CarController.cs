using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CarController : PredictionObject<BoxInputPayload, BoxStatePayload>
{
    private Rigidbody _rigidbody;
    [SerializeField] Camera _camera;
    private float springTravel = 0.2f;
    public float springStiffness = 100f;
    public float springDamper = 80f;
    public float DownForce = 40f;
    [SerializeField] Transform SpringReference;
    private float restLength = 0.5f;

    private float wheelRaduis = 0.3f;
    private float wheelMaxTurn = 30f;
    public float tireGripFactor = 0.3f;
    private float tireMass = 5f;

    [SerializeField] Transform[] Axis;
    [SerializeField] Transform mostRearRightAxis;
    [SerializeField] Transform mostRearLeftAxis;
    [SerializeField] Transform mostFrontRightAxis;
    [SerializeField] Transform mostFrontLeftAxis;
    [SerializeField] Transform centerOfMass;
    private float outWheelConst;
    private float innerWheelConst;
    private Vector3 center; 

    // Shooting
    [SerializeField] Transform shooter;

    void Awake() {
        if (Axis.Length > 0) {
            restLength = SpringReference.position.y - Axis[0].position.y;
            wheelRaduis = Axis[0].position.y;
        }

        float wheelBase = mostFrontLeftAxis.position.z - mostRearRightAxis.position.z;
        float rearTrack = mostRearRightAxis.position.x - mostRearLeftAxis.position.x;

        float turnRaduis = wheelBase/Mathf.Tan(wheelMaxTurn/Mathf.Rad2Deg) + rearTrack/2;

        outWheelConst = Mathf.Rad2Deg * Mathf.Atan(wheelBase/(turnRaduis + rearTrack/2));
        innerWheelConst = Mathf.Rad2Deg * Mathf.Atan(wheelBase/(turnRaduis - rearTrack/2));
        center = centerOfMass.localPosition;
    }
    public override void OnPredictionSpawn()
    {
        if (IsOwner) _camera.enabled = true;
        else _camera.enabled = false;
        _rigidbody = GetComponent<Rigidbody>();    
        _rigidbody.centerOfMass = center;
    }

    public override BoxInputPayload GetInput()
    {
        float horizontalInput = Input.GetAxis("Horizontal");
        float verticalInput = Input.GetAxis("Vertical");

        BoxInputPayload inputPayload = new BoxInputPayload();
        inputPayload.SetAxis(horizontalInput, verticalInput);
        // inputPayload.spacePressed = Input.GetKey(KeyCode.Space);

        return inputPayload;
    }

    public override void SetInput(BoxInputPayload inputPayload) {
        if (IsOwner || IsServer) {
            HandleSteering(inputPayload.GetHorizontal());
            HandleEngineForce(inputPayload.GetVertical());

        }
        // if (!PredictionManager.Singleton.IsReconciling || !IsOwner) {
        //     if (inputPayload.spacePressed) Debug.Log("key pressed");
        // }
    }

    // public override void ConstantApplyInput(BoxInputPayload inputPayload)
    // {
    // RaycastHit hit;
    // if (inputPayload.SpacePressed) {
    //     if (predictionRayCastData != null) {
    //         int rewindTick = predictionRayCastData.GetRewindTick(inputPayload.GetTick(), interpolator != null);
    //         PredictionManager.Singleton.RewindHitColliders(ObjectID, rewindTick);
    //         if(rayCast.Start(out hit, inputPayload.tick)) {

    //             Debug.Log("client Hit");
    //         }
    //     }
    // }
    // }

    public override void OnPreSimulation(bool DidRunPhysics)
    {
        if (DidRunPhysics) {
            HandleSuspension();
        }
    }

    private void HandleEngineForce(float input) {
        TireEngineForce(input, mostFrontLeftAxis);
        TireEngineForce(input, mostFrontRightAxis);
    }

    private void TireEngineForce(float input, Transform tire) {
        _rigidbody.AddForceAtPosition(tire.transform.forward * 100f * input, tire.position);
    }

    private void HandleSteering(float input) {
        if (input > 0) {
            mostFrontLeftAxis.localRotation = Quaternion.Euler(mostFrontLeftAxis.localEulerAngles.x, input * outWheelConst, mostFrontLeftAxis.localEulerAngles.z);
            mostFrontRightAxis.localRotation = Quaternion.Euler(mostFrontRightAxis.localEulerAngles.x, input * innerWheelConst, mostFrontRightAxis.localEulerAngles.z);
        }
        else if (input < 0) {
            mostFrontLeftAxis.localRotation = Quaternion.Euler(mostFrontLeftAxis.localEulerAngles.x, input * innerWheelConst, mostFrontLeftAxis.localEulerAngles.z);
            mostFrontRightAxis.localRotation = Quaternion.Euler(mostFrontRightAxis.localEulerAngles.x, input * outWheelConst, mostFrontRightAxis.localEulerAngles.z);
        }
        else {
            mostFrontLeftAxis.localRotation = Quaternion.Euler(mostFrontLeftAxis.localEulerAngles.x, 0, mostFrontLeftAxis.localEulerAngles.z);
            mostFrontRightAxis.localRotation = Quaternion.Euler(mostFrontRightAxis.localEulerAngles.x, 0, mostFrontRightAxis.localEulerAngles.z);
        }
    }

    private void HandleSuspension() {
        for (int i = 0; i < Axis.Length; i++)
        {
            if (Physics.Raycast(Axis[i].position, -Axis[i].up, out RaycastHit hit, springTravel + wheelRaduis)) {

                float hitDistance = hit.distance - wheelRaduis;

                hitDistance = Mathf.Clamp(hitDistance, -springTravel, springTravel);

                float Offset = restLength - hitDistance;

                Vector3 sringVel = _rigidbody.GetPointVelocity(Axis[i].position);
                float velSpring = Vector3.Dot(Axis[i].up, sringVel);
                float velTireSlip = Vector3.Dot(Axis[i].right, sringVel);

                float slipAccel = (-velTireSlip * tireGripFactor)/PredictionTimer.Singleton.minTimeBetweenTicks;
                float suspensionForce = Offset * springStiffness - velSpring * springDamper;

                _rigidbody.AddForceAtPosition(suspensionForce * Axis[i].up, Axis[i].position);
                _rigidbody.AddForceAtPosition(Axis[i].right * slipAccel * tireMass, Axis[i].position);
            }
        }

        _rigidbody.AddForce(-transform.up * DownForce * _rigidbody.velocity.normalized.magnitude);
    }

    public override BoxStatePayload GetState() {
        Quaternion holderRotation = _rigidbody.rotation;
        return new BoxStatePayload()
        {
            position = _rigidbody.position,
            rotation = QuaternionCompressor.CompressQuaternion(ref holderRotation),
            velocity = _rigidbody.velocity,
            angularVelocity = _rigidbody.angularVelocity
        };
    }
    public override void SetState(BoxStatePayload statePayload) {
        Quaternion holderRotation = Quaternion.identity;
        QuaternionCompressor.DecompressQuaternion(ref holderRotation, statePayload.rotation);
        
        transform.position = statePayload.position;
        transform.rotation = holderRotation;
        _rigidbody.position = statePayload.position;
        _rigidbody.rotation = holderRotation;
        _rigidbody.velocity = statePayload.velocity;
        _rigidbody.angularVelocity = statePayload.angularVelocity;
    }

    public override bool ShouldReconcile(BoxStatePayload latestServerState, BoxStatePayload ClientState) {
        float positionError = Vector3.Distance(latestServerState.position, ClientState.position);
        float rotDif = 1f - Quaternion.Dot(latestServerState.GetRot(), ClientState.GetRot());

        if (positionError > 0.01f || rotDif > 0.001f) {
            if (IsOwner) {
                Debug.Log("Did recon");
            }

            return true;
        }
        return false;
    }

    public override void OnPostSimulation(bool DidRunPhysics)
    {
        if (!PredictionManager.Singleton.IsReconciling) {
            interpolator.AddPosition(GetState().position, false);
            interpolator.AddRotation(GetState().GetRot());
        }

        //predictionRayCastData.AddTransform(GetStateAtTick(PredictionTimer.tick).position, GetStateAtTick(PredictionTimer.tick).GetRot());
    }

    // public override void OnProcessServerState(BoxStatePayload statePayload)
    // {
    //     rayCast.SetState(statePayload.DidShoot, statePayload.tick);
    // }

    private void OnServerConfirm(RaycastHit hit, bool didHit) {
        // Debug.Log("Did Server Hit: " + didHit);
    }

    public override void SendClientInputsToServer(List<BoxInputPayload> inputPayloads, Message sender)
    {
        // PayLoadBuffer<BoxInputPayload>.Compress(inputPayloads);
        sender.inputs.AddRange(inputPayloads);
    }

    public override void ReceiveClientInputs(Message receiver)
    {
        List<BoxInputPayload> objectInputs = PayLoadBuffer<BoxInputPayload>.FindObjectItems(receiver.inputs, ObjectID);
        // PayLoadBuffer<BoxInputPayload>.Decompress(objectInputs);
        AddClientInputs(objectInputs, receiver.tick);
    }

    public override void ReceiveServerState(Message receiver) {
        ApplyServerInputs(PayLoadBuffer<BoxInputPayload>.FindObjectItems(receiver.inputs, ObjectID), receiver.tick);
        ApplyServerState(PayLoadBuffer<BoxStatePayload>.FindObjectItem(receiver.states, ObjectID), receiver.tick);
    }

    public override void CompileServerState(BoxStatePayload statePayload, List<BoxInputPayload> inputPayloads, Message Compiler) {
        Compiler.states.Add(statePayload);
        Compiler.inputs.AddRange(inputPayloads);
    }

    public override void SortStateToSendToClient(Message defaultWorldState, Message sender) {
        for (int i = 0; i < defaultWorldState.states.Count; i++)
        {
            if (PayLoadBuffer<BoxStatePayload>.ContainsID(sender.states, defaultWorldState.states[i].ObjectID)) continue;

            if (defaultWorldState.states[i].ObjectID == ObjectID ) {
                sender.states.Add(defaultWorldState.states[i]);
            }
            else if (Vector3.Distance(defaultWorldState.states[i].position, GetState().position) < 40) {
                sender.states.Add(defaultWorldState.states[i]);
                sender.inputs.AddRange(PayLoadBuffer<BoxInputPayload>.FindObjectItems(defaultWorldState.inputs, defaultWorldState.states[i].ObjectID));
            }
        }
        // sender.states.AddRange(defaultWorldState.states);
        // sender.inputs.AddRange(defaultWorldState.inputs);
    }
}
