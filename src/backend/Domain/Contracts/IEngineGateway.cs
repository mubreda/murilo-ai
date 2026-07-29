namespace MuriloAI.Backend.Domain.Contracts;

public interface IEngineGateway
{
    Task<EngineProcessResult> ProcessAsync(EngineProcessRequest request, CancellationToken cancellationToken = default);
}
