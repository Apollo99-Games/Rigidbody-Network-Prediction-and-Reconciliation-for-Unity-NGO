using UnityEngine;
using System;
using Unity.Netcode;

public class PredictionTimer : NetworkBehaviour
{
    private float timer;
    [HideInInspector] public int tick {get; private set; }
    [HideInInspector] public float minTimeBetweenTicks {get; private set; }
    public int serverTickRate = 30;
    public Action OnTick;
    [HideInInspector] public static PredictionTimer Singleton { get; private set; }

    void Awake() {
        tick = 0;
        minTimeBetweenTicks = 1f / (float)serverTickRate;
    }

    void Start()
    {
        Physics.simulationMode = SimulationMode.Script;

        if (Singleton != null && Singleton != this) Destroy(this); 
        else Singleton = this; 
    }
    // Update is called once per frame
    void Update()
    {
        // Check to ensure the server or client has started
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer) return;
        // This loop ensures that each device has the same fixed time step.
        timer += Time.deltaTime;
        while (timer >= minTimeBetweenTicks)
        {
            timer -= minTimeBetweenTicks;
            if (OnTick != null) OnTick();
            tick++;
        }
    }

    /// <summary>
    /// Gets the ping value between server and client as a float
    /// </summary>
    /// <returns>The ping value as a float</returns>
    public static float GetPing() {
        return NetworkManager.Singleton.LocalTime.TimeAsFloat - NetworkManager.Singleton.ServerTime.TimeAsFloat;
    }

    /// <summary>
    /// Gets the ping value between server and client as a tick
    /// </summary>
    /// <returns>The ping value as a int</returns>
    public static int GetPingTick() {
        return (int)(GetPing()/Singleton.minTimeBetweenTicks);
    }
}
