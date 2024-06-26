# Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO
This is an implementation of Rigidbody Network Prediction and Reconciliation for Unity Netcode for Gameobjects. Prediction and Reconciliation help hide input latency over the network as they allow the client to apply their inputs instantly and then send them to the server. If the client and server desync, the server's state will override the clients. However, this is a bit difficult to do with Rigidbodies over the network, so I made a few scripts to simplify the process. This is made with Unity's Netcode for Game Objects in mind; however, it can be easily modified to work in any networking solution that is capable of sending RPCs with generic types.

# Demo:
Note that all the demos are from the client's view. The server does not see any jitter or snapping, as it has the final say in the game state.
At the start of each video, the left side is the player on the client, and the right side is the host machine's player.
All added delays are artificial and are done through NGO's built-in tools.

## Over LAN network

https://github.com/Apollo99-Games/Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO/assets/163193765/f038bd56-6227-4078-ac1e-163c6b721015

## Over LAN network + 200 MS RTT delay

https://github.com/Apollo99-Games/Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO/assets/163193765/1d4feb48-3c41-4b7a-8b7c-8dfb1b1a06b4

## Over LAN network + 100 MS RTT delay + 5% Packet loss (both receiving and sending)
Note how the client's cube doesn't look like it's teleporting (unless there is a collision). This is because the program compensates for packet loss by sending redundant inputs.
However, the host (server) does not send redundant information about its player or any other player to the clients. 
This is to save on bandwidth, but results in  a lot of corrections, especially when there are collisions.

https://github.com/Apollo99-Games/Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO/assets/163193765/e219ebf5-1fdd-4362-b97f-2acecc84a7f6

# Programs Needed:
- All the scripts in this GitHub Repository 
- Unity Editor 2022.3.17f1
- Netcode for GameObjects 1.7.1

# Prerequisites For Creating a Demo Scene for Rigidbody Network Prediction and Reconciliation using a basic cube:
- In the Unity editor go to Edit -> Project Settings -> Physics -> Enable Enhanced Determinism
- Have the Network Manager from the NGO setup
- Add a plane with a box collider at coordinates (0, -1, 0) and with a scale of (1000, 1000, 1000)
- Create a box prefab with a box collider, Rigidbody with a mass of 5kg, and scale of (2, 2, 2). The values can be anything you want. It would be helpful if the box is a different colour than the plane.

https://github.com/Apollo99-Games/Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO/assets/163193765/de89529f-cfa7-4fb2-844e-5814591322af


# Setting up the Prediction Manager:
- Create an empty game object and call it "Prediction Manager."
- Add the PredictionTick and PredictionManager Components to the game object.
- We will leave the values as default, but if you would like more info on what they do, hover over them with your mouse in the editor

https://github.com/Apollo99-Games/Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO/assets/163193765/16c39ffc-f9b7-4d76-b498-dcfafd00d8da



# Setting up the InputPayload for the Cube:
The cube we created before will be our player. First, let's create the payloads that will be sent between the server and the client.
The Client will send inputs to the server. You can have anything here as the input. However, you must use the IPayLoad interface, which forces you to implement tick and ID methods.
Note: The objectID needs to be Serialized but not the tick.
Here is an example:

```cs
public struct BoxInputPayload: IPayLoad
{
    // This tick is important so the client knows at which time the desync happened in order to rewind and correct it
    private int tick;
    // This is our input from the arrow keys on the keyboard
    public float vertical;
    public float horizontal;
    // This is the objectID. Ensures the input is applied to the correct object
    private byte objectID;

    public int Tick { get => tick; set => this.tick = value; }

    public byte ObjectID { get => objectID; set => this.objectID = value; }
    
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref objectID);
        serializer.SerializeValue(ref vertical);
        serializer.SerializeValue(ref horizontal);
    }
}
```

# Setting up the StatePayload for the Cube:
This is the final state the server will send to the client to ensure they are synced. The methods required here are the same as the input method.
Here is an example of a state: as we are syncing a rigid body, we need to send over the velocity and angular velocity to the client:
```cs
public struct BoxStatePayload: IPayLoad
{
    private int tick;
    private byte objectID;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 velocity;
    public Vector3 angularVelocity;

    public int Tick { get => tick; set => this.tick = value; }

    public byte ObjectID { get => objectID; set => this.objectID = value; }

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
The PayLoad.cs file contains a Message class. This is used to compile all the inputs and states of all prediction objects into one class.
We must add a list containing our payloads for the program to add our inputs and states. For Example:

```cs

public class Message: EventArgs {
    // Our stuff
    public List<BoxStatePayload> states;
    public List<BoxInputPayload> inputs;

    // Required stuff
    public int tick;
    public ushort OwnerID;
    public ClientRpcParams sendParams;

    public StateMessage() {
        states = new List<BoxStatePayload>();
        inputs = new List<BoxInputPayload>();
        tick = 0;
        OwnerID = 0;
    }

    public Message(WorldStatePayload worldPayload) {
        this.states = new List<BoxStatePayload>(worldPayload.states);
        this.inputs = new List<BoxInputPayload>(worldPayload.inputs);
        this.tick = worldPayload.tick;
    }

    public Message(WorldInputPayload worldPayload) {
        this.inputs = new List<BoxInputPayload>(worldPayload.inputs);
        this.tick = worldPayload.tick;
    }
}
```
# Adding the input and state packets to the world packets:
Once the states and inputs have been compiled in the message, they are converted to structs so they can be sent over to the client/server:
Note that the PayLoads do not need an OwnerID; it is only needed in the message class.

```cs
public struct WorldInputPayload: INetworkSerializable
{
    public int tick;
    //The inputs to be sent over to the server
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
    //This stores the inputs and states all the objects needed to be sent from the server to all clients
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
Now that we have created the data to be sent, we can start writing the movement logic to control our cube. Let's create a new class called "CubeMove." 
We will ensure it inherits the PredictionObject<T, T>. The first generic will be our input payload; the next will be the state. For example:

```cs
// Unity Netcode for Gameobjects requires tags when working with generics
[GenerateSerializationForTypeAttribute(typeof(BoxInputPayload))]
[GenerateSerializationForTypeAttribute(typeof(BoxStatePayload))]
public class CubeMove : PredictionObject<BoxInputPayload, BoxStatePayload>
{
    // As we are working with a Rigid body, we will need a reference for that component
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

    // This is not needed in this case. In objects involving more complex physics, such as ray casts, it could help prevent jitter
    transform.position = statePayload.position;
    transform.rotation = statePayload.rotation;
}
```

# Defining the conditions for a Reconcile
We must define what conditions constitute a desync that requires a correction. We will do this by overriding the ShouldReconciliate() method.
It gives us two parameters: the latest received server State (which is in the past due to latency) and the client state that happened around that time.
```cs
public override bool ShouldReconcile(BoxStatePayload latestServerState, BoxStatePayload ClientState) {
    // We will get the error in rotation and position between the server and client states
    float positionError = Vector3.Distance(latestServerState.position, ClientState.position);
    float rotDif = 1f - Quaternion.Dot(latestServerState.rotation, ClientState.rotation);

    //If the error is above a certain threshold, we will tell the client to correct itself to sync with the server
    if (positionError > 0.01f || rotDif > 0.001f) {
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
    // We will add it to the client inputs, where it will eventually be applied 
    AddClientInputs(objectInputs, receiver.tick);
}
```

# Sending and receiving states
It is a similar process for states but requires one extra method called SortStateToSendToClient(). This extra method allows you to compare all the states of all objects at once.
This will give you more control over what you want to send, as the state payload costs a lot of bandwidth. We must only send what we need. 
For example, if one client is far away from another, there is no point of sending these two clients the state about each other.

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
    //Here, we just send all the states and inputs. Ideally, you would only want to send what is needed
    sender.states.AddRange(defaultWorldState.states);
    sender.inputs.AddRange(defaultWorldState.inputs);
}
```
Note that we can also send inputs to all the clients here. However, be careful when applying movement from other clients on devices that are not on the server.
As the clients already get the state, they will then apply the input on top of that, essentially doing the same thing twice. This can result in jitter. 
One potential fix is to only apply any input related to movement on the object owner or the server. For example:
```cs
public override BoxInputPayload SetInput(BoxInputPayload inputPayload)
{
    if (IsOwner || IsServer) {
        // the movement code
        _rigidbody.AddForce(new Vector3(inputPayload.horizontal, 0f, inputPayload.vertical) * 100f);
    }
}
```

Here is an example of using the SortStateToSendToClient() function to send only information about players that are close to each other:
```cs
public override void SortStateToSendToClient(Message defaultWorldState, Message sender) {
    for (int i = 0; i < defaultWorldState.states.Count; i++)
    {
        // If the object data is already in the sender, we don't need to do anything, so skip it so we don't have any duplicates.
        if (PayLoadBuffer<BoxStatePayload>.ContainsID(sender.states, defaultWorldState.states[i].ObjectID)) continue;

        //If we find this object's data, we will add it to the sender
        if (defaultWorldState.states[i].ObjectID == ObjectID ) {
            sender.states.Add(defaultWorldState.states[i]);
        }

        // For all the other objects, we will make sure they are within 100 meters; if not, we won't send their data as they are too far.
        else if (Vector3.Distance(defaultWorldState.states[i].position, GetState().position) < 100) {
            sender.states.Add(defaultWorldState.states[i]);
            sender.inputs.AddRange(PayLoadBuffer<BoxInputPayload>.FindObjectItems(defaultWorldState.inputs, defaultWorldState.states[i].ObjectID));
        }
    }
    }
```

# Interpolation
This demo provides a basic interpolator. You would probably want to write your own, as this one is meant for testing but works.
We will not be interpolating the actual rigid body but simply the visual. To do this:
- create a new cube in your cube prefab made earlier and call it "visual." Make sure both cubes are identical (colour and scale).
- Remove Mesh Renderer and filter scripts from the parent. Add the CubeMove script to the parent
- Add the Interpolator script to the "visual" and pass a reference of it to our CubeMove script
- Finally, we will write this logic that runs right after the physics simulation in the CubeMove script that sends the cube's new state to the interpolator for interpolation


https://github.com/Apollo99-Games/Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO/assets/163193765/bd4b4a4d-92b8-412a-a0b5-ef8a7389105f


```cs
public override void OnPostSimulation(bool DidRunPhysics)
{
    // No point in sending the position and rotation if the client is reconciliation as we won't see it anyway
    if (!PredictionManager.Singleton.IsReconciling && interpolator != null) {
        // NeededToReconcile would return a bool if the client did reconciliation sometime during this tick
        interpolator.AddPosition(GetState().position, NeededToReconcile);
        interpolator.AddRotation(GetState().rotation);
    }
}
```

# Compressing Redundant Inputs
Because of packet loss, we have to send redundant inputs to the server to mitigate this. This could increase bandwidth.
The input may often be the same as the last, so we can remove these redundant copies before sending them to the client and duplicate them once the server receives them.
To do this, we will have to modify our input payload and the receiver and sender methods for inputs:
```cs
public struct BoxInputPayload: ICompressible
{
    //The number of duplicates this input had
    private byte numberOfCopies;

    //Our previous input code goes here

    public byte NumberOfCopies { get => numberOfCopies; set => this.numberOfCopies = value; }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        //Our previous code goes here
        serializer.SerializeValue(ref numberOfCopies);
    }
}
```
For our Send and Receive functions, we will merely pass them into the compress function, and that's it:
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
Here is an example of a server receiving input from a client with no compression. Note redundant inputs is set to 9 in all the images:
![no compression](https://github.com/ApolloGames99/Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO/assets/163193765/2065738c-3a08-4815-8b43-ed82d650c9ab)

Here are the inputs above but with compression ON (the InputPayLoad is the same, but not necessarily the values). 
Note the peaks here will be higher as we need an extra byte to store the number of copies:
![Max bytes compression](https://github.com/ApolloGames99/Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO/assets/163193765/69e4d350-ca6a-4e02-b2ff-ca9d893a33a7)

However, the troughs will be a lot lower:
![Min bytes compression](https://github.com/ApolloGames99/Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO/assets/163193765/a9929459-1afd-4eab-a984-8299db6e9acf)

This is most useful when you have large input packets with lots of data and redundant inputs.







