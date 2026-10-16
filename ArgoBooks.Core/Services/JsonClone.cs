using System.Text.Json;

namespace ArgoBooks.Core.Services;

/// <summary>Deep copies made through the save format, so a copy holds exactly what a save and reopen would.</summary>
internal static class JsonClone
{
    public static T Of<T>(T source) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(source, FileService.JsonOptions), FileService.JsonOptions)!;
}
