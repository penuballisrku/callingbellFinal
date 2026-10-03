namespace CallingBell.Application.Common;

public static class GeoMath
{
    /// <summary>Great-circle distance in kilometres between two latitude/longitude points.</summary>
    public static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 6371 * 2 * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
