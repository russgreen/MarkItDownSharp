using MarkItDownSharp.Presentation;
using Microsoft.UI.Dispatching;

namespace MarkItDownSharp.Desktop.Services;

public sealed class DispatcherQueueAdapter(DispatcherQueue queue) : IUiDispatcher
{
    public void Post(Action action)
    {
        if (queue.HasThreadAccess)
        {
            action();
        }
        else
        {
            queue.TryEnqueue(() => action());
        }
    }
}
