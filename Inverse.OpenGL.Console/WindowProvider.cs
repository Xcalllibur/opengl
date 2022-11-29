namespace Inverse.OpenGL.Platform
{
    public class WindowProvider : IWindowProvider
    {
        private const string ApplicationName = "OpenGL 4 VAO Shader Example";

        private readonly Window window;

        public WindowProvider()
        {
            this.window = new Window()
            {
                Title = ApplicationName,
                Width = 800,
                Height = 600,
                VSync = true
            };
        }

        public IWindow GetWindow()
        {
            return this.window;
        }
    }
}