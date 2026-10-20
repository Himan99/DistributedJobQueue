namespace DistributedJobQueue.Worker;

public class HandlerRegistry
{
    private readonly Dictionary<string, Func<string, Task<string>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string type, Func<string, Task<string>> handler)
    {
        _handlers[type] = handler;
    }

    public bool TryGet(string type, out Func<string, Task<string>>? handler)
    {
        return _handlers.TryGetValue(type, out handler);
    }
}
