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
- Add the Prediction Tick and Prediction Manager to the game object.
- We will leave the values as default, but if you would like more info on what they do, hover over them with your mouse in the editor

# Setting up the InputPayload for the Cube:
The cube we created before will be our player. First, let's create the packets that will be sent between the server and the client.
The Client will send inputs to the server. You can have anything here as the input. However, you must use the IPayLoad interface, which forces you to implement tick and ID methods.
Note: The objectID needs to be Serialized but not the tick.
Here is an example:

```cs
public struct BoxInputPayload: IPayLoad
{
    // This tick is important so the client knows at which time the desync happened inorder to rewind and correct it
    public int tick;
    // This is our input from the arrows keys on the keyboard
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
The cube we created before will be our player. First, let's create the packets that will be sent between the server and the client.
The Client will send inputs to the server. You can have anything here as the input. However, you must use the IPayLoad interface, which forces you to implement tick and ID methods.
Note: The objectID needs to be Serialized but not the tick.
Here is an example:
