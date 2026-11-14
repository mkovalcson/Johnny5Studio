using System.IO;
using System.Threading;

namespace ServoAnimator;

// Constructors retain their historical folder discovery, while explicit loads are scoped
// to the requested Config. AsyncLocal keeps concurrent model preparations independent.
internal static class ConfigurationContext
{
    private static readonly AsyncLocal<string> Root = new();
    internal static string CurrentRoot => Root.Value;
    internal static IDisposable Use(string root)
    {
        var previous = Root.Value;
        Root.Value = Path.GetFullPath(root);
        return new Scope(previous);
    }
    private sealed class Scope(string previous) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            Root.Value = previous;
            disposed = true;
        }
    }
}
