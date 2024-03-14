# Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO
This is an implementation of Rigidbody Network Prediction and Reconciliation for Unity Netcode for Gameobjects. Prediction and Reconciliation help hide input latency over the network as they allow the client to apply their inputs instantly and then send them to the server. If the client and server desync, the server's state will override the clients. However, this is a bit difficult to do with Rigidbodies over the network, so I made a few scripts to simplify the process. This is made with Unity's Netcode for Game Objects in mind; however, it can be easily modified to work in any networking solution that is capable of sending RPCs with generic types.

## Programs Needed:
- All the scripts in this GitHub Repository 
- Unity Editor 2022.3.17f1
- Netcode for GameObjects 1.7.1

# Prerequisites For Creating a Demo Scene for Rigidbody Network Prediction and Reconciliation using a basic cube:
- Have the Network Manager from the NGO setup
- Add a plane with a box collider at coordinates (0, -1, 0) and with a scale of (1000, 1000, 1000)
- Create a box prefab with a box collider, Rigidbody with a mass of 5kg, and scale of (2, 2, 2). The values can be anything you want. It would be helpful if the box is a different colour than the plane.


# Setting up the Prediction Manager:
- Create an empty game object and call it "Prediction Manager."
- Add the PredictionTick and PredictionManager Components to the game object.
- We will leave the values as default, but if you would like more info on what they do, hover over them with your mouse in the editor

# Setting up the InputPayload for the Cube:
The cube we created before will be our player. First, let's create the payloads that will be sent between the server and the client.
The Client will send inputs to the server. You can have anything here as the input. However, you must use the IPayLoad interface, which forces you to implement tick and ID methods.
Note: The objectID needs to be Serialized but not the tick.
Here is an example:

```cs
public struct BoxInputPayload: IPayLoad
{
    // This tick is important so the client knows at which time the desync happened in order to rewind and correct it
    public int tick;
    // This is our input from the arrow keys on the keyboard
    public float vertical;
    public float horizontal;
    // This is the objectID. Ensures the input is applied to the correct object
    public byte objectID;

    public bool IsEmpty() {
        return Equals(default(BoxInputPayload));
    }

    public int GetTick() {
        return tick;
    }

    public void SetTick(int tick) {
        this.tick = tick;
    }

    public byte GetObjectID() {
        return objectID;
    }

    public void SetObjectID(byte ObjectID) {
        this.objectID = ObjectID;
    }
    
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref objectID);
        serializer.SerializeValue(ref vertical);
        serializer.SerializeValue(ref horizontal);
    }
}
```

# Setting up the StatePayload for the Cube:
This is the final state the server will send to the client to ensure they are synced. The methods required here are the same as the input method.
Here is an example of a state, as we are syncing a rigid body we need send over the velocity and angular velocity to the client:
```cs
public struct BoxStatePayload: IPayLoad
{
    public int tick;
    public byte objectID;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 velocity;
    public Vector3 angularVelocity;

    public bool IsEmpty() {
        return Equals(default(BoxStatePayload));
    }

    public int GetTick() {
        return tick;
    }

    public void SetTick(int tick) {
        this.tick = tick;
    }

    public byte GetObjectID() {
        return objectID;
    }

    public void SetObjectID(byte objectID) {
        this.objectID = objectID;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref objectID);
        serializer.SerializeValue(ref position);
        serializer.SerializeValue(ref rotation);
        serializer.SerializeValue(ref velocity);
        serializer.SerializeValue(ref angularVelocity);
    }
}
```

# Adding the input and state payloads to the messages:
The PayLoad.cs file contains InputMessage and StateMessage classes. These are used to compile all the inputs and states of all prediction objects into one class.
For the program to add our inputs and states, we must add a list containing our payloads. For Example

```cs
public class InputMessage: EventArgs {
    // We add a list to store our inputs
    public List<BoxInputPayload> inputs;

    public int tick;
    public InputMessage() {
        inputs = new List<BoxInputPayload>();
        tick = 0;
    }

    public InputMessage(WorldInputPayload worldInputPayload) {
        //This is so that the packet (struct) received from the server can be converted into an InputMessage to prevent extensive copying
        this.inputs = new List<BoxInputPayload>(worldInputPayload.inputs);

        this.tick = worldInputPayload.tick;
    }
}

public class StateMessage: EventArgs {
    public List<BoxStatePayload> states;
    public List<BoxInputPayload> inputs;
    public int tick;
    public ushort OwnerID;
    public ClientRpcParams sendParams;

    public StateMessage() {
        states = new List<BoxStatePayload>();
        inputs = new List<BoxInputPayload>();
        tick = 0;
        OwnerID = 0;
    }

    public StateMessage(WorldStatePayload worldPayload) {
        this.states = new List<BoxStatePayload>(worldPayload.states);
        this.inputs = new List<BoxInputPayload>(worldPayload.inputs);
        this.tick = worldPayload.tick;
    }
}
```
# Adding the input and state packets to the world packets:
Once the states and inputs have been compiled in the message, they are converted to structs so they can be sent over to the client/server:
Note that the WorldStatePayLoad does not need an OwnerID; it is only needed in the message class.

```cs
public struct WorldInputPayload: INetworkSerializable
{
    public int tick;
    // the inputs to be sent over to the server
    public BoxInputPayload[] inputs;

    public static WorldInputPayload Create(InputMessage inputMessage) {
        WorldInputPayload holder = new WorldInputPayload();
        holder.inputs = inputMessage.inputs.ToArray();
        holder.tick = inputMessage.tick;

        return holder;
    }
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref inputs);
        serializer.SerializeValue(ref tick);
    }
}

public struct WorldStatePayload: INetworkSerializable
{
    public int tick;
    // this inputs and states of all the objects needed to be sent from the server to all clients
    public BoxStatePayload[] states;
    public BoxInputPayload[] inputs;

    public static WorldStatePayload Create(StateMessage statemessage) {
        WorldStatePayload holder = new WorldStatePayload();
        holder.states = statemessage.states.ToArray();
        holder.inputs = statemessage.inputs.ToArray();
        holder.tick = statemessage.tick;
        return holder;
    }
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref states);
        serializer.SerializeValue(ref inputs);
        serializer.SerializeValue(ref tick);
    }
}
```

# Creating the movement logic Class:
Now that we are done creating the data to be sent over, we can start writing the movement logic to control our cube. Let's create a new class called "CubeMove." 
We will ensure it inherits the Prediction<T, T>. The first generic will be our input payload; the next will be the state. For example:

```cs
// Unity Netcode for Gameobjects requires tags when working with generics
[GenerateSerializationForTypeAttribute(typeof(BoxInputPayload))]
[GenerateSerializationForTypeAttribute(typeof(BoxStatePayload))]
public class CubeMove : Prediction<BoxInputPayload, BoxStatePayload>
{
    // As we are working with a Rigid body, we will need a reference of that component
    [SerializeField] Rigidbody _rigidbody;
}
```

# Getting and setting the input:
Inside this class, we will override the GetInput() method to write the logic in order to get the user input and return it as an InputPayload

```cs
public override BoxInputPayload GetInput()
{
    float horizontalInput = Input.GetAxis("Horizontal");
    float verticalInput = Input.GetAxis("Vertical");

    BoxInputPayload inputPayload = new BoxInputPayload();
    inputPayload.horizontal = horizontalInput;
    inputPayload.vertical = verticalInput;

    return inputPayload;
}
```

Then, we will override the SetInput() function to apply our input. Here, we will apply a force on the cube.
```cs
public override BoxInputPayload SetInput(BoxInputPayload inputPayload)
{
    _rigidbody.AddForce(new Vector3(inputPayload.horizontal, 0f, inputPayload.vertical) * 100f);
}
```

# Getting and setting the states:
We will now do the same with the states:
```cs
public override BoxStatePayload GetState() {
    return new BoxStatePayload()
    {
        position = _rigidbody.position,
        rotation = _rigidbody.rotation,
        velocity = _rigidbody.velocity,
        angularVelocity = _rigidbody.angularVelocity
    };
}

public override void SetState(BoxStatePayload statePayload) {
    _rigidbody.position = statePayload.position;
    _rigidbody.rotation = statePayload.rotation;
    _rigidbody.velocity = statePayload.velocity;
    _rigidbody.angularVelocity = statePayload.angularVelocity;
}
```

# Defining the conditions for a Reconcile
We must define what conditions constitute a desync that requires a correction. We will do this by overriding the ShouldReconciliate() method.
It gives us two parameters: the latest received server State (which is in the past due to latency) and the client state that happened around that time.
```cs
public override bool ShouldReconciliate(BoxStatePayload latestServerState, BoxStatePayload ClientState) {
    // We will get the error in rotation and position between the server and client states
    float positionError = Vector3.Distance(latestServerState.position, ClientState.position);
    float rotDif = 1f - Quaternion.Dot(latestServerState.rotation, ClientState.rotation);

    //If the error is above a certain threshold, we will tell the client to correct itself to sync with the server
    if (positionError > 0.01f || rotDif > 0.001f) {
        if (IsOwner) {
            Debug.Log("Did recon");
        }
        return true;
    }
    return false;
}
```

# Sending and receiving inputs
First, we will write the logic to send all the inputs from the client to the server
We will override the SendClientInputsToServer() method to add all our input payloads to the input list we created in the input message:
```cs
public override void SendClientInputsToServer(List<BoxInputPayload> inputPayloads, InputMessage sender)
{
    sender.inputs.AddRange(inputPayloads);
}
```
Now we will apply the inputs we received from the client on the server:
```cs
public override void ReceiveClientInputs(InputMessage receiver)
{
    // We will find all the inputs that have the ID associated with this object
    List<BoxInputPayload> objectInputs = PayLoadBuffer<BoxInputPayload>.FindObjectItems(receiver.inputs, ObjectID);
    // We will add it to the client inputs where it will eventually be applied 
    AddClientInputs(objectInputs, receiver.tick);
}
```

# Sending and receiving states
It is a similar process for states but requires one extra method called SortStateToSendToClient(). This extra method allows you to compare all the states of all objects at once.
This will give you more control over what you want to send, as the state payload costs a lot of bandwidth. We must only send what we need. 
For example, if one client is far away from another, there is no point of sending these two clients the state about each other.

Note that here we can also send inputs to all the clients. However, be careful when applying movement from other clients on devices that are not the server.
As the clients already get the state, they will then apply the input on top of that, essentially doing the same thing twice. This can result in gitter. 
We will talk about solutions to this later on.
```cs
public override void ReceiveServerState(StateMessage receiver) {
    ApplyServerInputs(PayLoadBuffer<BoxInputPayload>.FindObjectItems(receiver.inputs, ObjectID), receiver.tick);
    ApplyServerState(PayLoadBuffer<BoxStatePayload>.FindObjectItem(receiver.states, ObjectID), receiver.tick);
}

public override void CompileServerState(BoxStatePayload statePayload, List<BoxInputPayload> inputPayloads, StateMessage Compiler) {
    Compiler.states.Add(statePayload);
    Compiler.inputs.AddRange(inputPayloads);
}

public override void SortStateToSendToClient(StateMessage defaultWorldState, StateMessage sender) {
    // here we just send all the states and inputs. Ideally, you would only want to send what is needed
    sender.states.AddRange(defaultWorldState.states);
    sender.inputs.AddRange(defaultWorldState.inputs);
}
```

# Interpolation
This demo provides a basic interpolator. You would probably want to write your own, as this one is just meant for testing but does work.
We will not be interpolating the actual rigid body but simply the visual. To do this:
- create an empty game object in your cube prefab and parent the current cube to it.
- Now move all your components (besides the ones that hold visual data like the material and mesh renderer) onto the parent
- We will rename the child game object that just has the box to "visual."
- Add the interpolator script to the visual and pass a reference of it to the our CubeMove script
- Finally, we will write this logic that runs right after the physics simulation that sends the cube's new state to the interpolator for interpolation
```cs
public override void OnPostSimulation(bool DidRunPhysics)
{
    // No point of sending the position and rotation if the client is reconciliating as we won't see it anyways
    if (!PredictionManager.Singleton.IsReconciliating) {
        // DidReconciliate returns a bool if the client did reconciliate sometime during this tick
        interpolator.addPosition(GetState().position, PredictionManager.Singleton.DidReconciliate);
        interpolator.addRotation(GetState().rotation);
    }
}
```

# Other Features:
**The Compressor
Because of packet loss, we have to send redundant inputs to the server to mitigate this. This could increase bandwidth.
A lot of the time, the input may have been the same as the last, so we can remove these redundant copies before sending them to the client and duplicate them once the server receives them.
To do this we will have to modify our input payload and the receiver and sender methods for inputs:
```cs
public struct BoxInputPayload: ICompressible
{
    // the number of duplicates this input had
    private byte numberOfCopies;

    // our previous code goes here

    public void SetNumberOfCopies(int num)
    {
        numberOfCopies = (byte)num;
    }

    public byte GetNumberOfCopies()
    {
        return numberOfCopies;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        // our previous code goes here
        serializer.SerializeValue(ref numberOfCopies);
    }
}
```
For our Send and Receive functions we will merely pass them into the compress function and that's it:
```cs
public override void SendClientInputsToServer(List<BoxInputPayload> inputPayloads, InputMessage sender)
{
    PayLoadBuffer<BoxInputPayload>.Compress(inputPayloads);
    sender.inputs.AddRange(inputPayloads);
}

public override void ReceiveClientInputs(InputMessage receiver)
{
    List<BoxInputPayload> objectInputs = PayLoadBuffer<BoxInputPayload>.FindObjectItems(receiver.inputs, ObjectID);
    PayLoadBuffer<BoxInputPayload>.Decompress(objectInputs);
    AddClientInputs(objectInputs, receiver.tick);
}
```
Here is a before and after. Whenever the inputs are the same, the data usage drops significantly. 
To even further optimize this, we can serialize the numberOfCopies variable only when there is a duplicate.






