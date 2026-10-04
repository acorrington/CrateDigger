namespace CrateDigger.Core.Models;

/// <summary>
/// A track that actually exists in the music library.
/// <see cref="Id"/> is the stable identifier of the backing library item
/// (Emby: the audio item's Guid, kept as string so Core stays server-agnostic).
/// </summary>
public sealed record LibraryTrack(string Id, TrackRef Track);