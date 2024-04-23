using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PredictionManager : NetworkBehaviour
{
    public const int BUFFER_SIZE = 512;
    [Tooltip("Over the network packets can be lost, so extra redundant packets are sent to compensate. This sets the maximum number of extra packets a prediction object can send")]
    public int MaxRedundentInputs = 10;
    
    [Tooltip("The maximum time in milliseconds the client can spend running the physics simulation when reconciling.")]
    [SerializeField] float maxPhysicsSimulationTime = 10f;

    [Tooltip("The maximum time in milliseconds the client can spend reconciling.")]
    [SerializeField] float maxReconciliationTime = 10f;
    /// <summary>
    /// Returns if the prediction manager is currently reconciling
    /// </summary>
    [HideInInspector] public bool IsReconciling { get; private set; }
    /// <summary>
    /// Returns if the prediction manager did reconcile sometime during this tick
    /// </summary>
    [HideInInspector] public bool DidReconcile { get; private set; }
    /// <summary>
    /// Returns if the prediction manager received an input packet this tick
    /// </summary>
    [HideInInspector] public bool DidReceiveInputPacket { get; private set; }
    /// <summary>
    /// Returns if the prediction manager received a state packet this tick
    /// </summary>
    [HideInInspector] public bool DidReceiveStatePacket { get; private set; }

    [Tooltip("If left checked, will reconcile all clients, which allows for better collisons and extrapolation at the cost of performance.")]
    public bool PredictAllClients = true;

    [HideInInspector] public static PredictionManager Singleton { get; private set; }

    // Inputs and states
    [HideInInspector] public event Action OnInputs;
    [HideInInspector] public event Action<int> OnState;

    // Reconciliation
    [HideInInspector] public event Action<int> OnReconcileInputs;
    [HideInInspector] public event Func<int> OnShouldRecon;
    [HideInInspector] public event Action<int> OnSetState;
    /// <summary>
    /// Runs before the Reconciliation process
    /// </summary>
    [HideInInspector] public event Action OnPreReconcile;
    /// <summary>
    /// Runs after the Reconciliation process
    /// </summary>
    [HideInInspector] public event Action OnPostReconcile;

    // Simulation

    /// <summary>
    /// Runs before the physics simulation
    /// </summary>
    /// <param name="DidRunPhysics">A boolean value indicating if physics will run after this event</param>
    [HideInInspector] public event Action<bool> OnPreSimulation;

    /// <summary>
    /// Runs after the physics simulation
    /// </summary>
    /// <param name="DidRunPhysics">A boolean value indicating if physics ran before this event</param>
    [HideInInspector] public event Action<bool> OnPostSimulation;

    // Sending and Receiving Packets
    [HideInInspector] public event Action<Message> OnComplieState;
    [HideInInspector] public event Action<List<Message>> OnSendState;
    [HideInInspector] public event Action<Message> OnSendInput;

    void Awake() {
        IsReconciling = false;
        DidReconcile = false;
        DidReceiveInputPacket = false;
        DidReceiveStatePacket = false;

        if (Singleton != null && Singleton != this) Destroy(this); 
        else Singleton = this;
    }

    void Start() {
        PredictionTimer.Singleton.OnTick += RunPrediction;
    }

    public override void OnDestroy() {
        PredictionTimer.Singleton.OnTick -= RunPrediction;
        base.OnDestroy();
    }

    /// <summary>
    /// Runs the main prediction loop at a fixed time step. Calls the:
    /// 1) Reconciliation functions,   
    /// 2) Compiles and sends inputs/states,  
    /// 3) Actions to get inputs, save states,  
    /// 4) Runs Physics,  
    /// </summary>
    /// <returns>void</returns>
    void RunPrediction() {
        DidReconcile = false;
        if (!IsServer && IsClient) {
            //Check if we need to Reconcile with the server
            int tickToProcess = DoReconcile();
            ServerReconcile(tickToProcess);
        }
        //Get and Apply all player inputs
        if (OnInputs != null) OnInputs();

        // Run the Physics simulation and store the client states
        if (OnPreSimulation != null) OnPreSimulation(true);
        Physics.Simulate(PredictionTimer.Singleton.minTimeBetweenTicks);
        
        if (OnState != null) OnState(PredictionTimer.Singleton.tick);
        if (OnPostSimulation != null) OnPostSimulation(true);

        // Compile All the States and send them to the clients for Reconciliation
        if (IsServer) CompileStates();
        //Compile all the inputs and send them to the server
        else if (IsClient) CompileInputs();

        DidReceiveStatePacket = false;
        DidReceiveInputPacket = false;
    }

    /// <summary>
    /// Reconciles the client state with the server states from a specified tick.
    /// </summary>
    /// <param name="tickToProcess">The tick to reconcile from.</param>
    /// <returns>void</returns>
    void ServerReconcile(int tickToProcess) {
        if(tickToProcess != -1) {
            
            if (OnPreReconcile != null) OnPreReconcile();
            IsReconciling = true;
            float startTime = Time.realtimeSinceStartup;

            // Revert all players back to the latest received server state
            if (OnSetState != null) OnSetState(tickToProcess);
            tickToProcess += 1;

            // resimulate all clients back to one tick before the current tick
            // This is because the current tick's input will be known after Reconciliation 
            while (tickToProcess < PredictionTimer.Singleton.tick)
            {
                // Apply the previous inputs
                if (OnReconcileInputs != null) OnReconcileInputs(tickToProcess);

                // Check to ensure if have not crossed our target reconcilation time to maintain performace
                // if so terminate the reconciliation
                if (Time.realtimeSinceStartup - startTime > maxReconciliationTime/1000f) {
                    // Debug.Log("Recon fail");
                    break;
                }

                // Check to ensure if have not crossed our target simulation time to maintain performace
                bool CanRunPhysics = Time.realtimeSinceStartup - startTime <= maxPhysicsSimulationTime/1000f;
                
                // Run the simulation if we can.
                if (OnPreSimulation != null) OnPreSimulation(CanRunPhysics);
                if (CanRunPhysics) {
                    Physics.Simulate(PredictionTimer.Singleton.minTimeBetweenTicks);
                }

                // Update with the new corrected states
                if (OnState != null) OnState(tickToProcess);

                if (OnPostSimulation != null) OnPostSimulation(CanRunPhysics);

                tickToProcess++;
            }

            // Debug.Log("Time: " + (Time.realtimeSinceStartup - startTime));
            DidReconcile = true;
            IsReconciling = false;
            if (OnPostReconcile != null) OnPostReconcile();
        }
    }

    /// <summary>
    /// Compiles All the inputs for each player
    /// </summary>
    /// <returns> void </returns>
    void CompileInputs() {
        // First we compile all the inputs of all the clients
        Message holder = new Message();
        if (OnSendInput != null) OnSendInput(holder);
        // We set the current tick and the ping between client and server
        holder.tick = PredictionTimer.Singleton.tick;

        SendToServerRPC(WorldInputPayload.Create(holder));
    }

    /// <summary>
    /// Compiles All the states into customized packets for each player
    /// </summary>
    /// <returns> void </returns>
    void CompileStates() {
        // First we compile all the states of all the clients
        Message defaultWorldState = new Message();
        if (OnComplieState != null) OnComplieState(defaultWorldState);

        // We then sort to customize the packets for each client to minimize bandwidth
        List<Message> AllStates = new List<Message>();
        if (OnSendState != null) OnSendState(AllStates);

        // iterate through all the packets to send them to the clients
        foreach (var state in AllStates)
        {
            SendToClientRPC(WorldPayload.Create(state), state.sendParams);
        }
    }

    /// <summary>
    /// Sends the states to the client
    /// </summary>
    /// <param name="worldStatePayLoad">The state payload to send to the client.</param>
    /// <param name="clientRpcParams">The client Params - to limit which client you send the state to.</param>
    /// <returns> void </returns>
    [ClientRpc]
    public void SendToClientRPC(WorldPayload worldStatePayLoad, ClientRpcParams clientRpcParams = default)
    { 
        if (OnComplieState != null) OnComplieState(new Message(worldStatePayLoad));
        DidReceiveStatePacket = true;
    }

    /// <summary>
    /// Sends the inputs to the server
    /// </summary>
    /// /// <param name="worldInputPayload">The input payload to send to the server.</param>
    /// <returns> void </returns>
    [ServerRpc(RequireOwnership = false)]
    public void SendToServerRPC(WorldInputPayload worldInputPayload)
    { 
        if (OnSendInput != null) OnSendInput(new Message(worldInputPayload));
        DidReceiveInputPacket = true;
    }

    /// <summary>
    /// Checks to see if reconciliation is needed and returns the reconciled tick value.
    /// </summary>
    /// <returns>The reconciled tick value</returns>
    private int DoReconcile() {
        int rewindTick = -1; 
        if (OnShouldRecon == null) return rewindTick;
        foreach (Func<int> funcCall in OnShouldRecon.GetInvocationList())
        {
            int tick = funcCall();

            if (tick != -1) {
                rewindTick = tick;
            }
        }
        return rewindTick;
    }
}
