# Rigidbody-Network-Prediction-and-Reconciliation-for-Unity-NGO
This is an implementation of Rigidbody Network Prediction and Reconciliation for Unity Netcode for Gameobjects. Prediction and Reconciliation help hide input latency over the network as they allow the client to apply their inputs instantly and then send them to the server. If the client and server desync, the server's state will override the clients. However, this is a bit difficult to do with Rigidbodies over the network, so I made a few scripts to simplify the process. This is made with Unity's Netcode for Game Objects in mind; however, it can be easily modified to work in any networking solution that is capable of sending RPCs with generic types.

## Programs Needed:
- All the scripts in this GitHub Repository 
- Unity Editor 2022.3.17f1
- Netcode for GameObjects 1.7.1
