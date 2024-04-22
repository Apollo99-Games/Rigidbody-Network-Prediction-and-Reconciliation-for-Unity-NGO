using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;

public class PredictionObject<InputPayload, StatePayload>: NetworkBehaviour where InputPayload : struct, IPayLoad where StatePayload : struct, IPayLoad
{
    // Shared
    [HideInInspector] public byte ObjectID {get; private set;} = 0;
    [HideInInspector] public ushort OwnerID {get; private set;} = 0;
    private PayLoadBuffer<InputPayload> inputQueue;
    private StatePayload[] stateBuffer;
    private InputPayload[] inputBuffer;

    //Client
    private StatePayload latestServerState;
    public StatePayload lastProcessedState;

    public bool NeededToReconcile {get; private set;}
    public bool DidReceiveState {get; private set;}

    //Server
    [HideInInspector] public bool DidReceiveInput {get; private set;}
    private Message currentDefaultWorldState;
    public ClientRpcParams sendParams {get; private set;}

    //Interpolation
    public Interpolator interpolator;


// =========== Setup ==============

    public override void OnNetworkSpawn()
    {
        stateBuffer = new StatePayload[PredictionManager.BUFFER_SIZE];
        inputBuffer = new InputPayload[PredictionManager.BUFFER_SIZE];

        inputQueue = new PayLoadBuffer<InputPayload>(25, 5, 3, 100);

        DidReceiveInput = false;
        NeededToReconcile = false;
        OwnerID = Convert.ToUInt16(OwnerClientId);
        ObjectID = (byte)NetworkObjectId;

        sendParams = new ClientRpcParams {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[]{OwnerID}
            }
        };

        if(IsServer) {
            PredictionManager.Singleton.OnInputs += ServerHandleTick;
            PredictionManager.Singleton.OnSendState += OnSendState;
        }
        else {
            PredictionManager.Singleton.OnInputs += ClientHandleTick;
            PredictionManager.Singleton.OnReconcileInputs += ForceSetInput;
            PredictionManager.Singleton.OnSetState += SetToPast;
            PredictionManager.Singleton.OnShouldRecon += NeedToReconcile;
        }
        PredictionManager.Singleton.OnState += StoreClientState;
        PredictionManager.Singleton.OnComplieState += ReceiveCompileState;
        PredictionManager.Singleton.OnSendInput += SendReceiveInput;
        PredictionManager.Singleton.OnPreSimulation += OnPreSimulation;
        PredictionManager.Singleton.OnPostSimulation += OnPostSimulation;

        if (interpolator != null) {
            interpolator.IsOwner = IsOwner;
            interpolator.IsServer = IsServer;
        }

        OnPredictionSpawn();
        base.OnNetworkSpawn();
    }

    /// <summary>
    /// Runs when the object spawns
    /// </summary>
    public virtual void OnPredictionSpawn() {}

    /// <summary>
    /// Runs when the object is destroyed
    /// </summary>
    public virtual void OnPredictionDespawn() {}

    public override void OnNetworkDespawn() {
        if(IsServer) {
            PredictionManager.Singleton.OnInputs -= ServerHandleTick;
            PredictionManager.Singleton.OnSendState -= OnSendState;
        }
        else {
            PredictionManager.Singleton.OnInputs -= ClientHandleTick;
            PredictionManager.Singleton.OnReconcileInputs -= ForceSetInput;
            PredictionManager.Singleton.OnSetState -= SetToPast;
            PredictionManager.Singleton.OnShouldRecon -= NeedToReconcile;
        }
        PredictionManager.Singleton.OnState -= StoreClientState;
        PredictionManager.Singleton.OnComplieState -= ReceiveCompileState;
        PredictionManager.Singleton.OnSendInput -= SendReceiveInput;
        PredictionManager.Singleton.OnPreSimulation -= OnPreSimulation;
        PredictionManager.Singleton.OnPostSimulation -= OnPostSimulation;

        OnPredictionDespawn();
        base.OnNetworkDespawn();
    }

// ============ Simulation Short Cuts =============

    /// <summary>
    /// Runs before the physics simulation
    /// </summary>
    /// <param name="DidRunPhysics">if the physics was able to run</param>
    public virtual void OnPreSimulation(bool DidRunPhysics) {}

    /// <summary>
    /// Runs after the physics simulation
    /// </summary>
    /// <param name="DidRunPhysics">if the physics was able to run</param>
    public virtual void OnPostSimulation(bool DidRunPhysics) {}

// ============ Client Logic =============

    /// <summary>
    /// Gets the new client input, stores it and applies the input
    /// </summary>
    /// <returns>void</returns>
    void ClientHandleTick()
    {
        if (IsOwner) {
            int bufferIndex = PredictionTimer.Singleton.tick % PredictionManager.BUFFER_SIZE;
            DidReceiveInput = true;

            InputPayload newInput = GetClientInput();

            inputBuffer[bufferIndex] = newInput;
            SetInput(newInput);
        }
        else {
            ClientHandleOtherClients();
        }
    }

    /// <summary>
    /// Applies and stores all the other client inputs (the one that are owned by other clients)
    /// </summary>
    /// <returns>void</returns>
    void ClientHandleOtherClients() {
        while(inputQueue.Size() > 0) {
            if (inputQueue.Size() > 0) SetInput(inputQueue.Dequeue());
        }
        inputBuffer[PredictionTimer.Singleton.tick % PredictionManager.BUFFER_SIZE] = inputQueue.GetLastItem();
    }


// =========== Client Logic Section 2: Reconcile Logic ============

    /// <summary>
    /// Decides if this object needs to be reconciled
    /// If Reconciliation is needed returns the tick to rewind to otherwise returns -1.
    /// </summary>
    /// <returns>int - the tick to rewind to</returns>
    int NeedToReconcile() {
        NeededToReconcile = false;
        DidReceiveState = false;

        // Ensure that the new state is not equal to the default state and doesn't equal the last state
        if (!latestServerState.Equals(default(StatePayload)) &&
        (lastProcessedState.Equals(default(StatePayload)) ||
        !latestServerState.Equals(lastProcessedState)))
        {
            // Enure the new state is not in the past
            if (lastProcessedState.Tick > latestServerState.Tick) return -1;
            DidReceiveState = true;

            int serverStateBufferIndex = latestServerState.Tick % PredictionManager.BUFFER_SIZE;
            lastProcessedState = latestServerState;

            // Run only if prediction of all Clients is enabled (if this is enabled it is very expensive)
            if (!PredictionManager.Singleton.PredictAllClients && !IsOwner) {
                SetState(lastProcessedState);
                return -1;
            }

            // A user defined function sets the criteria of what defines an desync and when Reconciliation is needed
            bool isRecon = ShouldReconcile(lastProcessedState, stateBuffer[serverStateBufferIndex]);

            if (isRecon) {
                NeededToReconcile = true;
                return lastProcessedState.Tick;
            }
        }
        return -1;
    }

    /// <summary>
    /// A user defined method that determines the criteria for Reconciliation. 
    /// This can be done by comparing the latestServerState with the ClientState at that time.
    /// </summary>
    /// <param name="latestServerState"> the latest server state received from the server</param>
    /// <param name="ClientState"> the client state that happened at a similar time to the received server state</param>
    /// <returns>boolean - should the server Reconcile</returns>
    public virtual bool ShouldReconcile(StatePayload latestServerState, StatePayload ClientState) {
        return true;
    }

    /// <summary>
    /// Sets the latestServerState to the current state and saves it to the buffer at the specified tick
    /// </summary>
    /// <param name="tick">the tick to save the latestServerState at the corresponding index in the buffer</param>
    /// <returns>void</returns>
    public void SetToPast(int tick)
    {
        int bufferIndex = tick % PredictionManager.BUFFER_SIZE;

        if (!latestServerState.Equals(default(StatePayload)) &&
        !lastProcessedState.Equals(default(StatePayload)))
        {   
            stateBuffer[bufferIndex] = lastProcessedState;
            SetState(lastProcessedState);
        }
        if (IsServer) {
            SetState(stateBuffer[bufferIndex]);
        }
    }

    /// <summary>
    /// Forces setting the input to a past one by a specified tick.
    /// </summary>
    /// <param name="tick">The tick for which to set the currnet input to.</param>
    /// <returns>void</returns>
    void ForceSetInput(int tick) {
        if (IsOwner) {
            SetInput(inputBuffer[tick % PredictionManager.BUFFER_SIZE]);
        }
        else {
            ClientHandleOtherClients();
        }
    }

// ========== Server Logic ==========

    /// <summary>
    /// Run's the server logic every tick
    /// </summary>
    /// <returns>void</returns>
    void ServerHandleTick()
    {
        if (IsOwner) {
            HandleServerPlayer();
        }
        else {
            HandleOtherClients();
        }
    }

    /// <summary>
    /// Empties the buffer and Applies the inputs of all the clients (except the server's) in a way that stops 
    /// the buffer from increasing which reduces latency.
    /// </summary>
    /// <returns>void</returns>
    void HandleOtherClients() {
        List<InputPayload> newInputs = inputQueue.DequeueToMaintain();
        DidReceiveInput = newInputs.Count > 0? true: false;

        for (int i = 0; i < newInputs.Count; i++)
        {
            SetInput(newInputs[i]);
        }
        inputBuffer[PredictionTimer.Singleton.tick % PredictionManager.BUFFER_SIZE] = inputQueue.GetLastItem();
    }

    /// <summary>
    /// Gets the new input for the server player and applies the input
    /// </summary>
    /// <returns>void</returns>
    void HandleServerPlayer() {
        DidReceiveInput = true;
        InputPayload newInput = GetClientInput();
        inputBuffer[PredictionTimer.Singleton.tick % PredictionManager.BUFFER_SIZE] = newInput;

        SetInput(newInput);

        inputQueue.Enqueue(newInput);
        inputQueue.Dequeue();
    }


// ========= Setting and Getting Inputs =========

    /// <summary>
    /// Gets the player inputs. This will only run on the client that owns the object
    /// </summary>
    /// <returns>InputPayload</returns>
    public virtual InputPayload GetInput() {
        return new InputPayload();
    }

    /// <summary>
    /// Gets the current client state.
    /// Guaranteed to set the tick and object ID
    /// </summary>
    /// <returns>StatePayload - the current state</returns>
    InputPayload GetClientInput() {
        InputPayload inputPayload = GetInput();
        inputPayload.Tick = PredictionTimer.Singleton.tick;
        inputPayload.ObjectID = ObjectID;
        return inputPayload;
    }


    /// <summary>
    /// Sets the player inputs. This will run on all machines. 
    /// NOTE: it is only guaranteed to run on the client that owns the object
    /// </summary>
    /// <returns>void</returns>
    public virtual void SetInput(InputPayload input) {}

    /// <summary>
    /// Gets the player input at a specified tick
    /// </summary>
    /// <param name="tick">The tick to get the input at</param>
    /// <returns>InputPayload</returns>
    public InputPayload GetInputAtTick(int tick) {
        return inputBuffer[tick % PredictionManager.BUFFER_SIZE];
    }


// ========= Sending and Receiving Inputs ==========

    /// <summary>
    /// If the functions is called on the server it will call a user defined function to apply the client inputs
    /// On the client it will get the current and previous inputs and add them to the input message through a user defined function
    /// </summary>
    /// <param name="sender">The object calling the function (this can be null)</param>
    /// <param name="message">Where the inputs are added and applied from</param>
    /// <returns>void</returns>
    void SendReceiveInput(Message message) {
        if (IsServer) {
            ReceiveClientInputs(message);
        }
        else if (IsOwner) {
            // redundent inputs and from the past as because of packet loss the server may have not gotten a previous input
            // to save bandwidth we don't send redundent inputs confirmed by the server
            int RedundentInputsCount = PredictionTimer.Singleton.tick - latestServerState.Tick;

            // to save bandwidth we ensure that the redundent inputs don't surpass the MaxRedundentInputs
            if (RedundentInputsCount > PredictionManager.Singleton.MaxRedundentInputs) {
                RedundentInputsCount = PredictionManager.Singleton.MaxRedundentInputs;
            }

            // add all redundent inputs to a list then send them to a client defined functions to be added to the input message
            List<InputPayload> RedundentInputs = new List<InputPayload>();
            for (int i = 0; i < RedundentInputsCount; i++)
            {
                RedundentInputs.Add(inputBuffer[(PredictionTimer.Singleton.tick - i) % PredictionManager.BUFFER_SIZE]);
            }

            SendClientInputsToServer(RedundentInputs, message);
        }
    }

    /// <summary>
    /// Adds the client inputs to a buffer where they can be applied over time
    /// </summary>
    /// <param name="inputs">The inputs to be added to the buffer</param>
    /// <param name="tick">the tick received from the client</param>
    /// <param name="serverDeltaTime">the ping between client and server in ticks</param>
    /// <returns>void</returns>
    public void AddClientInputs(List<InputPayload> inputs, int tick) {
        inputQueue.EnqueueRedundentItems(inputs, tick, PredictionManager.Singleton.MaxRedundentInputs);
    }

    /// <summary>
    /// Adds the client inputs to the input message
    /// </summary>
    /// <param name="inputs">The inputs to be added to the message</param>
    /// <param name="sender">the message that will be sent to the server</param>
    /// <returns>void</returns>
    public virtual void SendClientInputsToServer(List<InputPayload> inputs, Message sender) {}

    /// <summary>
    /// Applies the inputs using the AddClientInputs() method
    /// </summary>
    /// <param name="receiver">the input message received from the client</param>
    /// <returns>void</returns>
    public virtual void ReceiveClientInputs(Message receiver) {}

    // ========= Setting and Getting States ========= 

    /// <summary>
    /// Gets the state at a specified tick
    /// </summary>
    /// <param name="tick">the input message received from the client</param>
    /// <returns>StatePayload - The state at that tick</returns>
    public StatePayload GetStateAtTick(int tick) {
        return stateBuffer[tick % PredictionManager.BUFFER_SIZE];
    }

    /// <summary>
    /// sets the current state to a previous state a specified tick
    /// Basically rewinds to a previous tick 
    /// </summary>
    /// <param name="tick">the tick to rewind to</param>
    /// <returns>void</returns>
    void StoreClientState(int tick) {
        stateBuffer[tick % PredictionManager.BUFFER_SIZE] = GetState();
        stateBuffer[tick % PredictionManager.BUFFER_SIZE].Tick = tick;
        stateBuffer[tick % PredictionManager.BUFFER_SIZE].ObjectID = ObjectID;
        SetState(stateBuffer[tick % PredictionManager.BUFFER_SIZE]);
    }

    /// <summary>
    /// Gets the current client state.
    /// Guaranteed to set the tick and object ID
    /// </summary>
    /// <returns>StatePayload - the current state</returns>
    StatePayload GetClientState() {
        StatePayload statePayload = GetState();
        statePayload.Tick = inputQueue.GetLastItem().Tick;
        statePayload.ObjectID = ObjectID;
        return statePayload;
    }

    /// <summary>
    /// Gets the current client state 
    /// </summary>
    /// <returns>StatePayload - the current state</returns>
    public virtual StatePayload GetState() {
        return new StatePayload();
    }

    /// <summary>
    /// Sets the current state
    /// </summary>
    /// <param name="statePayload">The state to set the current state to</param>
    /// <returns>void</returns>
    public virtual void SetState(StatePayload statePayload) {}

    // ========= Sending and Receiving States ==========

    /// <summary>
    /// On the server the function will compile all the states and inputs into one state message
    /// On the client it will apply the states received to the ojects through a user defined function 
    /// </summary>
    /// <param name="sender">The object calling the function (this can be null)</param>
    /// <param name="message">Where the states are compiled and applied from</param>
    /// <returns>void</returns>
    void ReceiveCompileState(Message message) {
        if (IsServer) {
            CompileServerState(GetClientState(), inputQueue.GetLastItems(), message);
            currentDefaultWorldState = message;
        }
        else {
            ReceiveServerState(message);
        }
    }

    /// <summary>
    /// Creates customized messages defined by the user that are sent to the client.
    /// This helps save bandwidth each client gets only what they need
    /// </summary>
    /// <param name="sender">The object calling the function (this can be null)</param>
    /// <param name="messages">Customized state Messages for each client</param>
    /// <returns>void</returns>
    void OnSendState(List<Message> messages) {
        // Loop through the current state message list to see if this object's Client ID is in it.
        // If it is call the user defined function SortState() to customize the medthod to lower bandwidth
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].OwnerID == OwnerID) {
                SortStateToSendToClient(currentDefaultWorldState, messages[i]);
                return;
            }
        }

        // We the state message does nor exist for this object's Client, we create it and add it to the list
        Message holder = new Message
        {
            OwnerID = OwnerID,
            tick = inputQueue.GetLastItem().Tick,
            sendParams = sendParams
        };
        SortStateToSendToClient(currentDefaultWorldState, holder);
        messages.Add(holder);
    }

    /// <summary>
    /// Adds only relevant information from the defaultWorldState to the sender to save bandwidth.
    /// For example is if a prediction object is out of sight of a client, don't send state information for that object.
    /// </summary>
    /// <param name="defaultWorldState">The defualt state containing all the states/inputs of every prediction object</param>
    /// <param name="sender">The customized state to send to the client to save bandwidth</param>
    /// <returns>void</returns>
    public virtual void SortStateToSendToClient(Message defaultWorldState, Message sender) {}

    /// <summary>
    /// Adds the client inputs to the state or/and inputs to the state message
    /// </summary>
    /// <param name="statePayload">The state to be added to the message</param>
    /// <param name="inputPayloads">The inputs to be added to the message</param>
    /// <param name="sender">the message that will be sent to the clients</param>
    /// <returns>void</returns>
    public virtual void CompileServerState(StatePayload statePayload, List<InputPayload> inputPayloads, Message sender) {}

    /// <summary>
    /// Applies the states using the ApplyServerState() method
    /// Applies the inputs using the ApplyServerInputs() method
    /// </summary>
    /// <param name="receiver">the state message received from the client</param>
    /// <returns>void</returns>
    public virtual void ReceiveServerState(Message receiver) {}

    /// <summary>
    /// Applies the new server state if the client has desynced 
    /// </summary>
    /// <param name="state">the state to apply</param>
    /// <param name="tick">the server tick</param>
    /// <returns>void</returns>
    public void ApplyServerState(StatePayload state, int tick) {
        if (!state.Equals(default(StatePayload))) {
            state.Tick = tick;
            latestServerState = state;
        }
    }

    /// <summary>
    /// Applies the new server input from the server for objects that this client doesn't own
    /// </summary>
    /// <param name="state">the input to apply</param>
    /// <param name="tick">the server tick</param>
    /// <returns>void</returns>
    public void ApplyServerInputs(List<InputPayload> inputPayloads, int tick) {
        if (IsServer || IsOwner) return;

        for (int i = 0; i < inputPayloads.Count; i++)
        {
            InputPayload newInput = inputPayloads[i];
            newInput.Tick = tick;
            inputQueue.Enqueue(newInput);
        }
    }
} 