namespace CallingBell.Application.Common;

/// <summary>
/// An immutable in-memory spatial index of points: a uniform latitude/longitude grid whose cells hold the points inside them. A nearest-
/// point query reads only the cells within the search radius instead of every point, and compares candidates with a cheap flat-earth
/// distance (accurate at city scale), so it costs microseconds for thousands of points. Safe for concurrent reads.
/// </summary>
/// <typeparam name="T">What each point stands for (a city, an area).</typeparam>
public sealed class GeoGrid<T> where T : class
{
    private const double KmPerDegree = 111.32;
    private readonly double _cellDeg;
    private readonly Dictionary<long, Point[]> _cells;
    private readonly Point[] _all;

    private readonly record struct Point(double Lat, double Lon, T Item);

    /// <param name="cellDegrees">Cell size: about the radius usually searched (0.05° ≈ 5.5 km for areas, 0.5° ≈ 55 km for cities).</param>
    public GeoGrid(IEnumerable<(double Lat, double Lon, T Item)> points, double cellDegrees)
    {
        _cellDeg = cellDegrees;
        _all = points.Where(p => double.IsFinite(p.Lat) && double.IsFinite(p.Lon) && Math.Abs(p.Lat) <= 90 && Math.Abs(p.Lon) <= 180)
            .Select(p => new Point(p.Lat, p.Lon, p.Item)).ToArray();
        _cells = _all.GroupBy(p => Key(Cell(p.Lat), Cell(p.Lon))).ToDictionary(g => g.Key, g => g.ToArray());
    }

    public int Count => _all.Length;

    /// <summary>The point nearest to (<paramref name="lat"/>, <paramref name="lon"/>) within <paramref name="maxKm"/>, with its distance; null when none.</summary>
    public (T Item, double Km)? Nearest(double lat, double lon, double maxKm, Func<T, bool>? where = null)
    {
        if (_all.Length == 0 || maxKm <= 0) return null;
        var cos = Math.Max(Math.Cos(lat * Math.PI / 180), 0.01);
        var dLat = maxKm / KmPerDegree;
        var dLon = Math.Min(maxKm / (KmPerDegree * cos), 180);
        int y0 = Cell(lat - dLat), y1 = Cell(lat + dLat), x0 = Cell(lon - dLon), x1 = Cell(lon + dLon);

        Point? best = null;
        var bestD2 = double.MaxValue;
        void Consider(Point p)
        {
            if (where is not null && !where(p.Item)) return;
            var dy = (p.Lat - lat) * KmPerDegree;
            var dx = (p.Lon - lon) * KmPerDegree * cos;
            var d2 = dx * dx + dy * dy;
            if (d2 < bestD2) { bestD2 = d2; best = p; }
        }

        // A very large radius covers more cells than there are points: scanning the points is then cheaper.
        if ((long)(y1 - y0 + 1) * (x1 - x0 + 1) > _cells.Count)
        {
            foreach (var p in _all) Consider(p);
        }
        else
        {
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    if (_cells.TryGetValue(Key(y, x), out var cell))
                        foreach (var p in cell) Consider(p);
        }

        if (best is not { } b) return null;
        var km = GeoMath.HaversineKm(lat, lon, b.Lat, b.Lon);
        return km <= maxKm ? (b.Item, km) : null;
    }

    private int Cell(double degrees) => (int)Math.Floor(degrees / _cellDeg);

    private static long Key(int y, int x) => ((long)y << 32) | (uint)x;
}
