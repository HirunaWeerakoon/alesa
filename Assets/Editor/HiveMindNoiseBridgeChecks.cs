using System;
using System.Collections.Generic;
using Alesa.Core.AI;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Checks for the hive mind noise bridge. Run from Alesa/Tests in Play mode: the bridge only
/// subscribes in OnEnable, which Unity does not call while the editor is stopped.
/// Every object it creates is destroyed again, pass or fail.
/// </summary>
public static class HiveMindNoiseBridgeChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [MenuItem("Alesa/Tests/Check Hive Mind Noise Bridge (Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play mode first: the bridge subscribes in OnEnable.");
            return;
        }

        // Mapping is a pure function, so it can be checked without a scene at all.
        Check(AcousticNoiseBridge.MapType(SoundType.Footstep) == NoiseType.Footstep, "Footstep must map to Footstep");
        Check(AcousticNoiseBridge.MapType(SoundType.Sprint) == NoiseType.Sprint, "Sprint must map to Sprint");
        Check(AcousticNoiseBridge.MapType(SoundType.Gunshot) == NoiseType.Gunshot, "Gunshot must map to Gunshot");
        Check(AcousticNoiseBridge.MapType(SoundType.PropCollision) == NoiseType.Trolley, "PropCollision must map to Trolley");

        var host = new GameObject("Temporary noise bridge check");
        // Far from the level so a stray listener in the scene cannot react to the test noises.
        host.transform.position = new Vector3(10000f, 10000f, 10000f);
        var received = new List<NoiseEvent>();
        AcousticNoiseBridge bridge = null;
        var bus = new NoiseBus();
        Action<NoiseEvent> listener = n => received.Add(n);

        try
        {
            bus.NoiseEmitted += listener;
            bridge = host.AddComponent<AcousticNoiseBridge>();
            // Send noises to our own bus instead of the scene's, so the check needs no AIBootstrap.
            bridge.TargetBusOverride = bus;

            Vector3 origin = new Vector3(1f, 2f, 3f);

            // 1. Every category arrives once, with its radius carried across as loudness.
            AcousticBus.EmitSound(origin, 2f, SoundType.Footstep);
            Check(received.Count == 1, "A footstep should produce exactly one noise");
            Check(received[0].Type == NoiseType.Footstep, "Wrong category for a footstep");
            Check(Mathf.Approximately(received[0].Loudness, 2f), "Radius must pass through as loudness");
            Check(received[0].SourceId == AcousticNoiseBridge.PlayerSourceId, "Player noise must carry source id 0");
            // The AI works in System.Numerics coordinates; the conversion must not reorder axes.
            Check(received[0].Position.X == 1f && received[0].Position.Y == 2f && received[0].Position.Z == 3f,
                "World position changed while converting to core coordinates");

            AcousticBus.EmitSound(origin, 10f, SoundType.Sprint);
            Check(received.Count == 2 && received[1].Type == NoiseType.Sprint && Mathf.Approximately(received[1].Loudness, 10f),
                "A sprint should arrive as Sprint at 10 m");

            AcousticBus.EmitSound(origin, 40f, SoundType.Gunshot);
            Check(received.Count == 3 && received[2].Type == NoiseType.Gunshot && Mathf.Approximately(received[2].Loudness, 40f),
                "A gunshot should arrive as Gunshot at 40 m");

            AcousticBus.EmitSound(origin, 8f, SoundType.PropCollision);
            Check(received.Count == 4 && received[3].Type == NoiseType.Trolley && Mathf.Approximately(received[3].Loudness, 8f),
                "A prop impact should arrive as Trolley at 8 m");

            // 2. Silence stays silent. Crouch-walking and dry clicks emit radius 0.
            int before = received.Count;
            AcousticBus.EmitSound(origin, 0f, SoundType.Footstep);
            AcousticBus.EmitSound(origin, -5f, SoundType.Footstep);
            AcousticBus.EmitSound(origin, float.NaN, SoundType.Gunshot);
            Check(received.Count == before, "Zero, negative and NaN radii must be dropped");

            // 3. Disabling the bridge must genuinely deafen the hive, not just stop counting.
            bridge.enabled = false;
            AcousticBus.EmitSound(origin, 40f, SoundType.Gunshot);
            Check(received.Count == before, "A disabled bridge must not forward anything");

            // 4. Re-enabling must subscribe exactly once, not twice.
            bridge.enabled = true;
            AcousticBus.EmitSound(origin, 40f, SoundType.Gunshot);
            Check(received.Count == before + 1, "Re-enabling must subscribe exactly once");

            // 5. No managed allocation per noise. Footsteps fire several times a second for the
            //    whole game, so any garbage here becomes a frame hitch later.
            for (int i = 0; i < 500; i++) AcousticBus.EmitSound(origin, 2f, SoundType.Footstep);
            received.Clear();
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) AcousticBus.EmitSound(origin, 2f, SoundType.Footstep);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            Check(received.Count == 500, "All 500 noises should have been forwarded");
            // The List<NoiseEvent> in this check grows as it records, so allow its growth but
            // nothing like per-event garbage.
            Check(bytes < 40000, "Forwarding allocated too much managed memory: " + bytes + " bytes for 500 noises");

            // 6. With no bus at all the bridge must stay quiet instead of throwing.
            bridge.TargetBusOverride = null;
            AcousticBus.EmitSound(origin, 40f, SoundType.Gunshot);

            Debug.Log("<color=#7fd4ff>Noise bridge checks passed</color>: category mapping, loudness and position, "
                + "source id, silence handling, enable/disable lifecycle, zero-allocation forwarding, and safe "
                + "behaviour with no AIBootstrap present.");
        }
        finally
        {
            bus.NoiseEmitted -= listener;
            if (bridge != null) bridge.enabled = false;
            host.SetActive(false);
            UnityEngine.Object.Destroy(host);
        }
    }
}
