using OpenTK.Mathematics;
using OpenTK.Windowing.Common;

namespace Inverse.OpenGL.Platform
{
    public interface IGameEventHandler
    {
        /// <summary>
        /// Run immediately after Run() is called.
        /// </summary>
        void Load();

        /// <summary>
        /// Run when the window is about to close.
        /// </summary>
        void Unload();

        /// <summary>
        /// Run when the window is resized.
        /// </summary>
        /// <param name="width">New width of renderable area in pixels.</param>
        /// <param name="height">New height of renderable area in pixels.</param>
        void Resize(int width, int height);

        /// <summary>
        /// Run when the update thread is ready to update.
        /// </summary>
        /// <param name="elapsed">Time that has elapsed since the last update frame in seconds.</param>
        void UpdateFrame(double elapsed);

        /// <summary>
        /// Run when the render thread is ready to render.
        /// </summary>
        /// <param name="elapsed">Time that has elapsed since the last render frame in seconds.</param>
        void RenderFrame(double elapsed);

        /// <summary>
        /// Run when the mouse cursor moves within the window.
        /// </summary>
        /// <param name="position">This cursor position relative to the top-left corner of the contents of the window.</param>
        /// <param name="delta">The change in the cursor position since the last event.</param>
        void MouseMove(Vector2 position, Vector2 delta);

        void KeyDown(KeyboardKeyEventArgs args);
    }
}