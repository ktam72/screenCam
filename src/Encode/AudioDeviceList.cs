// REQ-005: WASAPI loopback の対象 render デバイスを列挙する (設定 UI から参照)
namespace ScreenCam.Encode;

using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using ScreenCam.Logging;

public sealed record AudioDevice(string Id, string Name)
{
    // ComboBox の表示用 (record の ToString だと Id が出るため Name を出す)
    public override string ToString() => Name;
}

public static class AudioDeviceList
{
    // loopback は render デバイスを録る。既定 endpoint を先頭に置く (Config.AudioDeviceId は device Id)
    public static List<AudioDevice> RenderDevices()
    {
        List<AudioDevice> devices = new();

        try
        {
            using MMDeviceEnumerator enumerator = new();

            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                Log.Warn("REQ-005: 既定の render デバイスが見つからない");
                return devices;
            }

            MMDeviceCollection active = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (MMDevice device in active)
                devices.Add(new AudioDevice(device.ID, device.FriendlyName));
        }
        catch (Exception ex)
        {
            Log.Warn($"REQ-005: render デバイスを列挙できない: {ex.Message}");
        }

        return devices;
    }
}
