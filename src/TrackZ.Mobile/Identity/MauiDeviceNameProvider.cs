using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Identity;

public sealed class MauiDeviceNameProvider : IDeviceNameProvider
{
    public string DeviceName
    {
        get
        {
            var name = DeviceInfo.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return "iOS";
            return name.Length <= 100 ? name : name[..100];
        }
    }
}
