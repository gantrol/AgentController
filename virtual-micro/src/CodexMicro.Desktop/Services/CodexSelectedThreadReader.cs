using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

internal sealed class CodexSelectedThreadReader
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly string _indexPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "session_index.jsonl");
    private DateTime _indexModified;
    private long _indexLength = -1;
    private Dictionary<string, string> _titles = new(StringComparer.Ordinal);
    private nint _lastWindow;

    internal async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var window = CodexWindowActivator.FindSelectionWindow(_lastWindow);
                _lastWindow = window;
                if (window == nint.Zero) return null;
                RefreshTitles();
                return Resolve(ReadSelectedTitles(window), _titles);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is COMException or IOException or
            UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
        finally { _gate.Release(); }
    }

    internal static string? Resolve(IEnumerable<string> selectedTitles, IReadOnlyDictionary<string, string> titles)
    {
        var selected = selectedTitles.ToHashSet(StringComparer.Ordinal);
        var matches = titles.Where(pair => selected.Contains(pair.Value))
            .Select(pair => pair.Key).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        // Duplicate titles, a hidden sidebar, and a blank draft are unknown targets.
        return matches.Length == 1 ? matches[0] : null;
    }

    private void RefreshTitles()
    {
        var file = new FileInfo(_indexPath);
        if (!file.Exists) { _titles.Clear(); return; }
        if (file.LastWriteTimeUtc == _indexModified && file.Length == _indexLength) return;
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = new FileStream(_indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(id.GetString(), out _) &&
                    root.TryGetProperty("thread_name", out var title) && title.ValueKind == JsonValueKind.String)
                    titles[id.GetString()!] = title.GetString()!;
            }
            catch (JsonException) { }
        }
        _titles = titles;
        _indexModified = file.LastWriteTimeUtc;
        _indexLength = file.Length;
    }

    internal static string[] ReadSelectedTitles(nint window)
    {
        IAutomation? client = null;
        IElement? root = null;
        ICondition? condition = null;
        IElements? selected = null;
        try
        {
            client = (IAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"))!)!;
            root = client.ElementFromHandle(window);
            condition = client.CreatePropertyConditionEx(30102, "current=page", 2);
            selected = root.FindAll(4, condition);
            var titles = new List<string>();
            for (var index = 0; index < selected.Length; index++)
            {
                var node = selected.GetElement(index);
                try
                {
                    if (node.GetCurrentPropertyValue(30012) is string css &&
                        css.Split(' ').Contains("sidebar-item", StringComparer.Ordinal) &&
                        node.GetCurrentPropertyValue(30005) is string title && title.Length > 0)
                        titles.Add(title);
                }
                finally { Marshal.ReleaseComObject(node); }
            }
            return titles.ToArray();
        }
        finally
        {
            if (selected is not null) Marshal.ReleaseComObject(selected);
            if (condition is not null) Marshal.ReleaseComObject(condition);
            if (root is not null) Marshal.ReleaseComObject(root);
            if (client is not null) Marshal.ReleaseComObject(client);
        }
    }

    // The Windows SDK IUIAutomation vtable. Only read operations are exposed.
    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAutomation
    {
        void Slot0(); void Slot1(); void Slot2();
        IElement ElementFromHandle(nint handle);
        void Slot4(); void Slot5(); void Slot6(); void Slot7(); void Slot8(); void Slot9();
        void Slot10(); void Slot11(); void Slot12(); void Slot13(); void Slot14(); void Slot15(); void Slot16(); void Slot17();
        void Slot18(); void Slot19(); void Slot20();
        ICondition CreatePropertyConditionEx(int property, [MarshalAs(UnmanagedType.Struct)] object value, int flags);
    }
    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IElement
    {
        void Slot0(); void Slot1(); void Slot2();
        IElements FindAll(int scope, ICondition condition);
        void Slot4(); void Slot5(); void Slot6();
        [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int property);
    }
    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICondition { }
    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IElements { int Length { get; } IElement GetElement(int index); }
}
