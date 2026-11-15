namespace Vela.Core;

public sealed class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
}

public sealed class NoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Text { get; set; } = "";
    public DateTime Updated { get; set; } = DateTime.Now;

    public string Title
    {
        get
        {
            var first = Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(first) ? "New note" : first;
        }
    }

    public string Preview
    {
        get
        {
            var lines = Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 1 ? string.Join(" ", lines.Skip(1)).Trim() : "";
        }
    }
}

public sealed class UserData
{
    public List<TodoItem> Todos { get; set; } = new();
    public List<NoteItem> Notes { get; set; } = new();
}

/// <summary>Todos and notes. Local only (%LOCALAPPDATA%\Vela\data.json).</summary>
public sealed class DataStore
{
    readonly string _path = Paths.File("data.json");
    readonly Debouncer _saver;
    public UserData Data { get; }
    public event Action? Changed;

    public DataStore()
    {
        Data = Json.Load<UserData>(_path);
        _saver = new Debouncer(TimeSpan.FromMilliseconds(500), () => Json.SaveAtomic(_path, Data));
    }

    public void Touch()
    {
        _saver.Trigger();
        Changed?.Invoke();
    }

    public void Flush() => _saver.Flush();
}
