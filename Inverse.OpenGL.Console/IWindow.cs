namespace Inverse.OpenGL.Platform
{
    public interface IWindow : IDisposable
    {
        float AspectRatio { get; }
        int Height { get; set; }
        bool IsActive { get; }
        double RenderFrequency { get; set; }
        double RenderTime { get; }
        string Title { get; set; }
        double UpdateFrequency { get; set; }
        double UpdateTime { get; }
        bool VSync { get; set; }
        int Width { get; set; }

        void Close();
        void Run();

        void SetHandler(IGameEventHandler handler);
    }
}