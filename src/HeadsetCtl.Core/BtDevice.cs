namespace HeadsetCtl.Core;

public sealed record BtDevice(string Name, ulong Address, bool Connected, uint ClassOfDevice)
{
    private const uint MajorClassAudioVideo = 0x04;

    public bool IsAudio => ((ClassOfDevice >> 8) & 0x1F) == MajorClassAudioVideo;
    public string Mac => MacAddress.Format(Address);
}
