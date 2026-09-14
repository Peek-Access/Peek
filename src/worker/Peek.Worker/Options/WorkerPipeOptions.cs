namespace Peek.Worker.Options;

public sealed class WorkerPipeOptions
{
    public const string SectionName = "Pipe";

    public string PipeName { get; set; } = "peek-worker";
}
