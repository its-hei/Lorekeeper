using System;

namespace Lorekeeper;

public interface ILorekeeperLogger
{
    void Information(string message);

    void Warning(string message);

    void Error(Exception exception, string message);
}
