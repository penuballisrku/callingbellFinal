using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CallingBell.Application.Common;
using CallingBell.Domain.Entities;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Geo;

public sealed class PincodeDirectoryOptions
{
    public const string Section = "PincodeDirectory";
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// data.gov.in API key (free, from data.gov.in after signing in). A secret: keep it in user-secrets or an environment variable
    /// (PincodeDirectory__ApiKey). With neither this nor <see cref="CsvPath"/> the agent does nothing.
    /// </summary>
    public string? ApiKey { get; set; }
    /// <summary>The data.gov.in resource: All India Pincode Directory (with latitude and longitude).</summary>
    public string ResourceId { get; set; } = "5c2f62fe-5afa-4119-a499-fec9d604d5bd";
    public string ApiUrl { get; set; } = "https://api.data.gov.in/resource/";
    public int PageSize { get; set; } = 1000;
    /// <summary>Instead of the API: the directory's CSV file downloaded from data.gov.in (relative to the API's content root, or absolute).</summary>
    public string? CsvPath { get; set; }
    /// <summary>The directory is downloaded again after this many days (India Post updates it monthly).</summary>
    public int RefreshDays { get; set; } = 90;
    public string UserAgent { get; set; } = "CallingBell-pincodes/1.0 (+https://www.callinbell.com)";
}

/// <summary>
/// Keeps India Post's All India Pincode Directory in <c>dbo.PostOffices</c> and gives every area the district of its PIN code
/// (<c>Areas.District</c>). Runs shortly after start-up and then twice a day: the directory is downloaded when missing or older than
/// <see cref="PincodeDirectoryOptions.RefreshDays"/>; area districts are filled on every pass, so areas found since then get theirs.
/// The data is open (Government Open Data License - India); nothing is generated.
/// </summary>
internal sealed class PincodeDirectoryWorker(IServiceScopeFactory scopes, IOptions<PincodeDirectoryOptions> options, ILogger<PincodeDirectoryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Enabled) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<PincodeDirectoryRun>().ExecuteAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "PIN code directory: run failed");
                }
                await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}

internal sealed class PincodeDirectoryRun(ApplicationDbContext db, IHttpClientFactory httpFactory, IOptions<PincodeDirectoryOptions> options,
    IHostEnvironment env, IMemoryCache cache, ILogger<PincodeDirectoryRun> logger)
{
    public const string HttpClientName = "pincode-directory";
    private const string Agent = "agent:pincode-directory";
    private const int BatchSize = 2000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var o = options.Value;
        var csv = string.IsNullOrWhiteSpace(o.CsvPath) ? null
            : Path.IsPathRooted(o.CsvPath) ? o.CsvPath : Path.Combine(env.ContentRootPath, o.CsvPath);
        var configured = !string.IsNullOrWhiteSpace(o.ApiKey) || (csv is not null && File.Exists(csv));

        var newest = await db.PostOffices.MaxAsync(p => (DateTimeOffset?)p.CreatedOn, ct);
        if (configured && (newest is null || newest < DateTimeOffset.UtcNow.AddDays(-o.RefreshDays)))
        {
            var started = DateTimeOffset.UtcNow;
            var rows = csv is not null && File.Exists(csv) ? await ReadCsvAsync(csv, ct) : await ReadApiAsync(ct);
            if (rows.Count > 0) await ReplaceAsync(rows, ct);
            logger.LogInformation("PIN code directory: {Count} post offices from {Source} ({Seconds:0}s)", rows.Count,
                csv is not null && File.Exists(csv) ? "CSV" : "data.gov.in", (DateTimeOffset.UtcNow - started).TotalSeconds);
        }
        else if (!configured && newest is null)
        {
            logger.LogInformation("PIN code directory: not configured (set PincodeDirectory:ApiKey or PincodeDirectory:CsvPath); areas keep their city's district");
            return;
        }

        // Every area takes the district most of its PIN code's post offices are in.
        var filled = await db.Database.ExecuteSqlRawAsync("""
            UPDATE a SET District = d.District, ModifiedOn = SYSDATETIMEOFFSET(), ModifiedBy = {0}
            FROM dbo.Areas a
            CROSS APPLY (SELECT TOP (1) p.District FROM dbo.PostOffices p
                         WHERE p.Pincode = a.Pincode AND p.IsDeleted = 0
                         GROUP BY p.District ORDER BY COUNT(*) DESC, p.District) d
            WHERE a.IsDeleted = 0 AND a.Pincode <> N'' AND (a.District IS NULL OR a.District <> d.District)
            """, [Agent], ct);
        if (filled > 0)
        {
            ReferenceDataCache.Invalidate(cache); // cached area lists now carry districts
            logger.LogInformation("PIN code directory: set the district of {Count} areas", filled);
        }
    }

    /// <summary>
    /// Swaps the whole directory in one transaction, so searches never see it half loaded. The transaction runs inside the connection's
    /// retry strategy (transient SQL failures retry the whole swap), with fresh rows on each attempt.
    /// </summary>
    private async Task ReplaceAsync(List<PostOffice> rows, CancellationToken ct)
    {
        db.SuppressAuditLog = true;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.PostOffices.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
            foreach (var batch in rows.Chunk(BatchSize))
            {
                db.PostOffices.AddRange(batch.Select(Copy));
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
            await tx.CommitAsync(ct);
        });
    }

    private static PostOffice Copy(PostOffice p) => new()
    {
        OfficeName = p.OfficeName, Pincode = p.Pincode, OfficeType = p.OfficeType, Delivery = p.Delivery, Division = p.Division, Region = p.Region,
        Circle = p.Circle, District = p.District, StateName = p.StateName, Latitude = p.Latitude, Longitude = p.Longitude, CreatedBy = p.CreatedBy,
    };

    private async Task<List<PostOffice>> ReadApiAsync(CancellationToken ct)
    {
        var o = options.Value;
        var client = httpFactory.CreateClient(HttpClientName);
        if (!client.DefaultRequestHeaders.UserAgent.Any()) client.DefaultRequestHeaders.UserAgent.ParseAdd(o.UserAgent);
        var rows = new List<PostOffice>();
        for (var offset = 0; ; offset += o.PageSize)
        {
            var url = $"{o.ApiUrl.TrimEnd('/')}/{Uri.EscapeDataString(o.ResourceId)}?api-key={Uri.EscapeDataString(o.ApiKey!)}&format=json&offset={offset}&limit={o.PageSize}";
            var page = await client.GetFromJsonAsync<ApiPage>(url, Json, ct);
            var records = page?.Records ?? [];
            rows.AddRange(records.Select(r => ToRow(k => r.TryGetValue(k, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null))
                .OfType<PostOffice>());
            if (records.Count < o.PageSize) break;
        }
        return rows;
    }

    private static async Task<List<PostOffice>> ReadCsvAsync(string path, CancellationToken ct)
    {
        var rows = new List<PostOffice>();
        using var reader = new StreamReader(path, Encoding.UTF8);
        var header = await reader.ReadLineAsync(ct);
        if (header is null) return rows;
        var columns = SplitCsv(header).Select((name, i) => (Key: Key(name), i)).GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First().i);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) continue;
            var cells = SplitCsv(line);
            if (ToRow(k => columns.TryGetValue(k, out var i) && i < cells.Count ? cells[i] : null) is { } row) rows.Add(row);
        }
        return rows;
    }

    /// <summary>One post office from a record's fields (data.gov.in names: officename, pincode, district, statename…); null when incomplete.</summary>
    private static PostOffice? ToRow(Func<string, string?> field)
    {
        string? F(string key) => field(key) is { } v && !string.IsNullOrWhiteSpace(v) && v.Trim() is not ("NA" or "N/A" or "NULL") ? v.Trim() : null;
        var pin = new string((F("pincode") ?? "").Where(char.IsDigit).ToArray());
        var name = F("officename");
        var district = F("district") ?? F("districtname");
        var state = F("statename");
        if (pin.Length != 6 || name is null || district is null || state is null) return null;
        return new PostOffice
        {
            OfficeName = Clip(Title(name), 150)!, Pincode = pin, OfficeType = Clip(F("officetype"), 20), Delivery = Clip(F("delivery") ?? F("deliverystatus"), 30),
            Division = Clip(F("divisionname"), 150), Region = Clip(F("regionname"), 150), Circle = Clip(F("circlename"), 150),
            District = Clip(Title(district), 100)!, StateName = Clip(Title(state), 100)!,
            Latitude = Coordinate(F("latitude"), 90), Longitude = Coordinate(F("longitude"), 180), CreatedBy = Agent,
        };
    }

    /// <summary>"RANGA REDDY" → "Ranga Reddy"; names already in mixed case are kept.</summary>
    private static string Title(string text) =>
        text.Any(char.IsLower) ? text : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());

    private static decimal? Coordinate(string? text, int max) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v != 0 && Math.Abs(v) <= max ? Math.Round(v, 6) : null;

    private static string Key(string name) => new string(name.Where(char.IsLetter).ToArray()).ToLowerInvariant();

    private static string? Clip(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];

    /// <summary>One CSV line (quoted fields may contain commas and doubled quotes).</summary>
    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    private sealed record ApiPage(List<Dictionary<string, JsonElement>>? Records);
}
