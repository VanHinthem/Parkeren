namespace Parkeren.Api;

internal static class DiagnosticActionCleanup
{
    internal static async Task StopAsync(
        Func<Task> stopAction,
        ILogger logger,
        string warningMessage,
        string providerActionId)
    {
        try
        {
            await stopAction();
        }
        catch (Exception)
        {
            logger.LogWarning(warningMessage, providerActionId);
        }
    }
}