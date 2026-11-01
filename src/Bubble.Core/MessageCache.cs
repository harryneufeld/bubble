using System.Text.Json;

namespace Bubble.Core;

/// <summary>
/// Remembers the messages last loaded from the relay, so the app can show them right away and offline.
/// Only what the relay holds is stored: still encrypted, nothing in plain text.
/// </summary>
public interface IMessageCache
{
    List<RelayMessageOut>? Load(string mailboxId);
    void Save(string mailboxId, List<RelayMessageOut> messages);
    void Delete(string mailboxId);
}

/// <summary>One file per bubble in <paramref name="directory"/>. Problems with the files are never fatal: it is only a cache.</summary>
public sealed class FileMessageCache(string directory) : IMessageCache
{
    string PathFor(string mailboxId) => Path.Combine(directory, mailboxId + ".json");

    public List<RelayMessageOut>? Load(string mailboxId)
    {
        try
        {
            var path = PathFor(mailboxId);
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllBytes(path), CoreJson.Default.ListRelayMessageOut) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public void Save(string mailboxId, List<RelayMessageOut> messages)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = PathFor(mailboxId);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(messages, CoreJson.Default.ListRelayMessageOut));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Delete(string mailboxId)
    {
        try { File.Delete(PathFor(mailboxId)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Clear()
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
