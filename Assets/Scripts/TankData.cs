using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A struct for movement inputs, used in client-side prediction + server authority.
/// Must implement INetworkSerializable to work with Netcode RPCs.
/// </summary>
[System.Serializable]
public struct MovementInput : INetworkSerializable
{
    public float moveInput;
    public float rotationInput;
    public int inputSequence;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref moveInput);
        serializer.SerializeValue(ref rotationInput);
        serializer.SerializeValue(ref inputSequence);
    }
}

/// <summary>
/// A struct for sending authoritative server state back to the client,
/// also must be INetworkSerializable for Netcode RPCs.
/// </summary>
[System.Serializable]
public struct ServerState : INetworkSerializable
{
    public Vector2 position;
    public float rotation;
    public int lastProcessedInput;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref position);
        serializer.SerializeValue(ref rotation);
        serializer.SerializeValue(ref lastProcessedInput);
    }
}
