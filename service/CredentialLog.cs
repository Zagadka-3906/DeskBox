namespace DeskBox;

// The shared Windows credential store uses DeskBox.App.Log for its diagnostics.
internal static class App
{
    public static void Log(string message) => Console.Error.WriteLine(message);
}
