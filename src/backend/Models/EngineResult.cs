namespace MuriloAI.Backend.Models;

public class EngineResult
{
    public bool Success { get; set; }
    public string? Engine { get; set; }
    public string? InputPath { get; set; }
    public string? OutputPath { get; set; }
    public double Duration { get; set; }
    public string? Message { get; set; }
}
