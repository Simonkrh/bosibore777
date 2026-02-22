using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public static class NetworkRuntimeConfig
{
    public const string DefaultLoopbackAddress = "127.0.0.1";
    public const string ListenOnAllInterfaces = "0.0.0.0";

    public static string ReadAddress(string fallbackAddress)
    {
        string safeFallback = string.IsNullOrWhiteSpace(fallbackAddress) ? DefaultLoopbackAddress : fallbackAddress.Trim();
        string[] args = Environment.GetCommandLineArgs();

        if (TryGetArgValue(args, "-address", out string value) ||
            TryGetArgValue(args, "-ip", out value) ||
            TryGetArgValue(args, "--address", out value) ||
            TryGetArgValue(args, "--ip", out value))
        {
            return value.Trim();
        }

        return safeFallback;
    }

    public static ushort ReadPort(int fallbackPort)
    {
        ushort safeFallback = (ushort)Mathf.Clamp(fallbackPort, 1, 65535);
        string[] args = Environment.GetCommandLineArgs();

        if ((TryGetArgValue(args, "-port", out string value) || TryGetArgValue(args, "--port", out value)) &&
            int.TryParse(value, out int parsedPort) &&
            parsedPort >= 1 &&
            parsedPort <= 65535)
        {
            return (ushort)parsedPort;
        }

        return safeFallback;
    }

    public static bool TryConfigureClient(NetworkManager manager, string serverAddress, ushort port)
    {
        if (!TryGetUnityTransport(manager, out UnityTransport transport))
        {
            return false;
        }

        transport.SetConnectionData(serverAddress, port);
        return true;
    }

    public static bool TryConfigureHost(NetworkManager manager, ushort port)
    {
        if (!TryGetUnityTransport(manager, out UnityTransport transport))
        {
            return false;
        }

        transport.SetConnectionData(DefaultLoopbackAddress, port, ListenOnAllInterfaces);
        return true;
    }

    public static bool TryConfigureDedicatedServer(NetworkManager manager, ushort port)
    {
        if (!TryGetUnityTransport(manager, out UnityTransport transport))
        {
            return false;
        }

        transport.SetConnectionData(DefaultLoopbackAddress, port, ListenOnAllInterfaces);
        return true;
    }

    private static bool TryGetArgValue(string[] args, string argName, out string value)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], argName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = args[i + 1];
            return !string.IsNullOrWhiteSpace(value);
        }

        value = null;
        return false;
    }

    private static bool TryGetUnityTransport(NetworkManager manager, out UnityTransport transport)
    {
        transport = manager != null && manager.NetworkConfig != null
            ? manager.NetworkConfig.NetworkTransport as UnityTransport
            : null;
        if (transport != null)
        {
            return true;
        }

        Debug.LogError("[NetworkRuntimeConfig] UnityTransport is required on the NetworkManager.");
        return false;
    }
}
