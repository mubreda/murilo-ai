namespace MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;

public interface IProcessImageWithEngineUseCase
{
    Task<ProcessImageWithEngineResult> ExecuteAsync(ProcessImageWithEngineCommand command, CancellationToken cancellationToken = default);
}
