using SerialLoops.Lib.Util;

namespace ChokuRando;

public class ConsoleProgressTracker : IProgressTracker
{
    public int Finished { get; set; }
    public int Total { get; set; }
    public string CurrentlyLoading { get; set; } = string.Empty;
}