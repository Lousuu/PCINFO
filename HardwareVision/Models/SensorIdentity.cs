namespace HardwareVision.Models;

public readonly record struct SensorIdentity(
    SensorCategory Category,
    string DeviceName,
    SensorType Type,
    string SensorName);

internal sealed class SensorIdentityComparer : IEqualityComparer<SensorIdentity>
{
    public static SensorIdentityComparer Instance { get; } = new();

    public bool Equals(SensorIdentity x, SensorIdentity y)
    {
        return x.Category == y.Category
            && x.Type == y.Type
            && StringComparer.OrdinalIgnoreCase.Equals(x.DeviceName, y.DeviceName)
            && StringComparer.OrdinalIgnoreCase.Equals(x.SensorName, y.SensorName);
    }

    public int GetHashCode(SensorIdentity obj)
    {
        HashCode hash = new();
        hash.Add(obj.Category);
        hash.Add(obj.Type);
        hash.Add(obj.DeviceName, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.SensorName, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }
}
