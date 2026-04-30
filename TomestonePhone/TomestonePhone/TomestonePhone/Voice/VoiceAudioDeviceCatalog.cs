using System.Runtime.InteropServices;

namespace TomestonePhone.Voice;

public sealed record VoiceAudioDeviceInfo(int DeviceNumber, string DisplayName, string PreferenceKey);

public sealed record VoiceAudioDeviceResolution(
    int DeviceNumber,
    string DisplayName,
    bool UsesWindowsDefault,
    bool SavedPreferenceMissing,
    string? MissingDeviceName);

public static class VoiceAudioDeviceCatalog
{
    private const int MaxProductNameLength = 32;
    private const string WindowsDefaultLabel = "Windows Default";

    public static IReadOnlyList<VoiceAudioDeviceInfo> GetInputDevices()
    {
        var deviceCount = checked((int)waveInGetNumDevs());
        var devices = new List<VoiceAudioDeviceInfo>(deviceCount);
        for (var deviceNumber = 0; deviceNumber < deviceCount; deviceNumber++)
        {
            if (waveInGetDevCaps((UIntPtr)deviceNumber, out var caps, (uint)Marshal.SizeOf<WaveInCaps2>()) != 0)
            {
                continue;
            }

            var displayName = NormalizeDisplayName(caps.ProductName, $"Input Device {deviceNumber + 1}");
            devices.Add(new VoiceAudioDeviceInfo(deviceNumber, displayName, BuildPreferenceKey(displayName, caps.NameGuid, caps.ProductGuid, caps.ManufacturerGuid)));
        }

        return devices;
    }

    public static IReadOnlyList<VoiceAudioDeviceInfo> GetOutputDevices()
    {
        var deviceCount = checked((int)waveOutGetNumDevs());
        var devices = new List<VoiceAudioDeviceInfo>(deviceCount);
        for (var deviceNumber = 0; deviceNumber < deviceCount; deviceNumber++)
        {
            if (waveOutGetDevCaps((UIntPtr)deviceNumber, out var caps, (uint)Marshal.SizeOf<WaveOutCaps2>()) != 0)
            {
                continue;
            }

            var displayName = NormalizeDisplayName(caps.ProductName, $"Output Device {deviceNumber + 1}");
            devices.Add(new VoiceAudioDeviceInfo(deviceNumber, displayName, BuildPreferenceKey(displayName, caps.NameGuid, caps.ProductGuid, caps.ManufacturerGuid)));
        }

        return devices;
    }

    public static VoiceAudioDeviceResolution ResolveInputDevice(string? preferredKey, string? preferredName)
    {
        return ResolveDevice(GetInputDevices(), preferredKey, preferredName);
    }

    public static VoiceAudioDeviceResolution ResolveOutputDevice(string? preferredKey, string? preferredName)
    {
        return ResolveDevice(GetOutputDevices(), preferredKey, preferredName);
    }

    public static VoiceAudioDeviceResolution ResolveInputDevice(IReadOnlyList<VoiceAudioDeviceInfo> devices, string? preferredKey, string? preferredName)
    {
        return ResolveDevice(devices, preferredKey, preferredName);
    }

    public static VoiceAudioDeviceResolution ResolveOutputDevice(IReadOnlyList<VoiceAudioDeviceInfo> devices, string? preferredKey, string? preferredName)
    {
        return ResolveDevice(devices, preferredKey, preferredName);
    }

    private static VoiceAudioDeviceResolution ResolveDevice(IReadOnlyList<VoiceAudioDeviceInfo> devices, string? preferredKey, string? preferredName)
    {
        var normalizedKey = NormalizePreference(preferredKey);
        var normalizedName = NormalizePreference(preferredName);
        if (string.IsNullOrWhiteSpace(normalizedKey) && string.IsNullOrWhiteSpace(normalizedName))
        {
            return new VoiceAudioDeviceResolution(-1, WindowsDefaultLabel, true, false, null);
        }

        var exactMatch = devices.FirstOrDefault(device => string.Equals(device.PreferenceKey, normalizedKey, StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null)
        {
            return new VoiceAudioDeviceResolution(exactMatch.DeviceNumber, exactMatch.DisplayName, false, false, null);
        }

        var nameMatch = devices.FirstOrDefault(device => string.Equals(device.DisplayName, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (nameMatch is not null)
        {
            return new VoiceAudioDeviceResolution(nameMatch.DeviceNumber, nameMatch.DisplayName, false, false, null);
        }

        return new VoiceAudioDeviceResolution(-1, WindowsDefaultLabel, true, true, normalizedName ?? normalizedKey);
    }

    private static string BuildPreferenceKey(string displayName, Guid nameGuid, Guid productGuid, Guid manufacturerGuid)
    {
        var normalizedName = NormalizePreference(displayName) ?? displayName;
        if (nameGuid != Guid.Empty)
        {
            return $"name:{nameGuid:N}|label:{normalizedName}";
        }

        if (productGuid != Guid.Empty || manufacturerGuid != Guid.Empty)
        {
            return $"product:{productGuid:N}|manufacturer:{manufacturerGuid:N}|label:{normalizedName}";
        }

        return $"label:{normalizedName}";
    }

    private static string NormalizeDisplayName(string? value, string fallback)
    {
        var normalized = value?
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string? NormalizePreference(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint waveInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint waveOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int waveInGetDevCaps(UIntPtr deviceNumber, out WaveInCaps2 capabilities, uint capabilitiesSize);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int waveOutGetDevCaps(UIntPtr deviceNumber, out WaveOutCaps2 capabilities, uint capabilitiesSize);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WaveInCaps2
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxProductNameLength)]
        public string ProductName;

        public uint SupportedFormats;
        public ushort Channels;
        public ushort Reserved;
        public Guid ManufacturerGuid;
        public Guid ProductGuid;
        public Guid NameGuid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WaveOutCaps2
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxProductNameLength)]
        public string ProductName;

        public uint SupportedFormats;
        public ushort Channels;
        public ushort Reserved;
        public uint Support;
        public Guid ManufacturerGuid;
        public Guid ProductGuid;
        public Guid NameGuid;
    }
}
