namespace DistributedJobQueue.Worker;

public static class Handlers
{
    public static Func<string, Task<string>> SuccessHandler => async payload =>
    {
        await Task.Delay(50);
        return $"file:///output/{Guid.NewGuid():N}.pdf";
    };

    public static Func<string, Task<string>> FailureHandler => async payload =>
    {
        await Task.Delay(50);
        throw new InvalidOperationException("Simulated handler failure");
    };

    public static Func<string, Task<string>> SlowHandler(int delayMs) => async payload =>
    {
        await Task.Delay(delayMs);
        return $"file:///output/{Guid.NewGuid():N}.pdf";
    };
}
