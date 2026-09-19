using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public sealed class TrackExperienceService(NeoDatabase db, ArtworkService artwork)
{
    public Task<string> ImportAssetAsync(long songId, string source, string kind)
    {
        if (!File.Exists(source)) throw new FileNotFoundException(source);
        AppPaths.Ensure();
        var ext = Path.GetExtension(source);
        var destination = Path.Combine(AppPaths.Assets, $"{kind}-{songId}{ext}");
        File.Copy(source, destination, true);
        return Task.FromResult(destination);
    }

    public Task SetArtworkAsync(Song song, string source) => artwork.SetCustomArtworkAsync(song, source);

    public Task<TrackVisualProfile?> LoadAsync(long songId) => db.GetTrackVisualAsync(songId);
    public Task SaveAsync(TrackVisualProfile profile) => db.SaveTrackVisualAsync(profile);
}
