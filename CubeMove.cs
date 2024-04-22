using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

// This example CubeMove file is slightly different from the readme file. 
// It gives a few more examples on how it can be modified to fit your needs

[GenerateSerializationForTypeAttribute(typeof(BoxInputPayload))]
[GenerateSerializationForTypeAttribute(typeof(BoxStatePayload))]
public class CubeMove : PredictionObject<BoxInputPayload, BoxStatePayload>
{
    [SerializeField] Rigidbody _rigidbody;
    private int counter;

    public override BoxInputPayload GetInput()
    {
        float horizontalInput = Input.GetAxis("Horizontal");
        float verticalInput = Input.GetAxis("Vertical");

        BoxInputPayload inputPayload = new BoxInputPayload();
        inputPayload.SetAxis(horizontalInput, verticalInput);

        inputPayload.spacePressed = Input.GetKey(KeyCode.Space);

        return inputPayload;
    }

    public override void SetInput(BoxInputPayload inputPayload) {
        if (IsOwner || IsServer)
        _rigidbody.AddForce(new Vector3(inputPayload.GetHorizontal(), 0f, inputPayload.GetVertical()) * 100f);

        if (inputPayload.spacePressed && !PredictionManager.Singleton.IsReconciling) {
            counter++;
            Debug.Log("Counter: " + counter);
        }
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
        _rigidbody.position = statePayload.position;
        _rigidbody.rotation = statePayload.GetRot();
        _rigidbody.velocity = statePayload.velocity;
        _rigidbody.angularVelocity = statePayload.angularVelocity;
    }

    public override bool ShouldReconcile(BoxStatePayload latestServerState, BoxStatePayload ClientState) {
        float positionError = Vector3.Distance(latestServerState.position, ClientState.position);
        float rotDif = 1f - Quaternion.Dot(latestServerState.GetRot(), ClientState.GetRot());

        if (positionError > 0.01f || rotDif > 0.001f) {
            return true;
        }
        return false;
    }

    public override void OnPostSimulation(bool DidRunPhysics)
    {
        if (!PredictionManager.Singleton.IsReconciling && interpolator != null) {
            interpolator.AddPosition(GetStateAtTick(PredictionTimer.Singleton.tick).position, NeededToReconcile);
            interpolator.AddRotation(GetStateAtTick(PredictionTimer.Singleton.tick).GetRot());
        }
    }

    public override void SendClientInputsToServer(List<BoxInputPayload> inputPayloads, Message sender)
    {
        PayLoadBuffer<BoxInputPayload>.Compress(inputPayloads);
        sender.inputs.AddRange(inputPayloads);
    }

    public override void ReceiveClientInputs(Message receiver)
    {
        List<BoxInputPayload> objectInputs = PayLoadBuffer<BoxInputPayload>.FindObjectItems(receiver.inputs, ObjectID);
        PayLoadBuffer<BoxInputPayload>.Decompress(objectInputs);
        AddClientInputs(objectInputs, receiver.tick);
    }

    public override void CompileServerState(BoxStatePayload statePayload, List<BoxInputPayload> inputPayloads, Message Compiler) {
        Compiler.states.Add(statePayload);
        Compiler.inputs.AddRange(inputPayloads);
    }

    public override void ReceiveServerState(Message receiver) {
        ApplyServerState(PayLoadBuffer<BoxStatePayload>.FindObjectItem(receiver.states, ObjectID), receiver.tick);
        ApplyServerInputs(PayLoadBuffer<BoxInputPayload>.FindObjectItems(receiver.inputs, ObjectID), receiver.tick);
    }

    public override void SortStateToSendToClient(Message defaultWorldState, Message sender) {
        for (int i = 0; i < defaultWorldState.states.Count; i++)
        {
            if (PayLoadBuffer<BoxStatePayload>.ContainsID(sender.states, defaultWorldState.states[i].ObjectID)) continue;

            if (defaultWorldState.states[i].ObjectID == ObjectID ) {
                sender.states.Add(defaultWorldState.states[i]);
            }
            else if (Vector3.Distance(defaultWorldState.states[i].position, GetState().position) < 1000f) {
                sender.states.Add(defaultWorldState.states[i]);
                sender.inputs.AddRange(PayLoadBuffer<BoxInputPayload>.FindObjectItems(defaultWorldState.inputs, defaultWorldState.states[i].ObjectID));
            }
        }
    }
}
