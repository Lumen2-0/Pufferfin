namespace Pufferfin.Server.Storage;

/// <summary>
/// Something an administrator should look at on the storage screen.
/// </summary>
/// <param name="Code">A stable code, for the client to translate.</param>
/// <param name="Message">An English sentence.</param>
public sealed record StorageAdvice(StorageAdviceCode Code, string Message);
