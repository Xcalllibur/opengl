using Inverse.OpenGL.Platform;
using Serilog;

namespace Inverse.OpenGL.Console
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File("Teapot.log")
                .CreateLogger();

            var windowProvider = new WindowProvider();
            var engine = new Engine(windowProvider);
            var main = new Main(engine);

            main.Run();
        }
    }
}