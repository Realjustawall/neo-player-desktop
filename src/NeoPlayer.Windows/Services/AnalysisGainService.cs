using Microsoft.Data.Sqlite;
using NeoPlayer.Windows.Core;

namespace NeoPlayer.Windows.Services;

public sealed class AnalysisGainService
{
    private readonly string _connectionString;

    public AnalysisGainService(string? databasePath = null)
    {
        var path = databasePath ?? AppPaths.Database;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    public double GetGainDb(long songId, double targetLufs)
    {
        try
        {
            using var c = new SqliteConnection(_connectionString);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT lufs,peak FROM analysis WHERE song_id=$id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", songId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return 0;
            var lufs = r.GetDouble(0);
            var peak = Math.Max(0.000001, r.GetDouble(1));
            var requested = Math.Clamp(targetLufs - lufs, -18d, 6d);
            var peakHeadroomDb = 20d * Math.Log10(0.98d / peak);
            return Math.Clamp(Math.Min(requested, peakHeadroomDb), -18d, 6d);
        }
        catch { return 0; }
    }
}
