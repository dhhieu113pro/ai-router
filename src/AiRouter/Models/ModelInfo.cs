namespace AiRouter.Models;

public sealed class ModelInfo
{
    public string Id { get; set; } = "";
    public string Object { get; set; } = "model";
    public long Created { get; set; }
    public string OwnedBy { get; set; } = "ai-router";
}
