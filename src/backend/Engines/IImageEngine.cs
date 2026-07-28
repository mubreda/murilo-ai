namespace MuriloAI.Backend.Engines;

public interface IImageEngine
{
    Task<bool> EnhanceAsync(CancellationToken cancellationToken = default);
}
