using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Diagnostics;

namespace Inverse.OpenGL.Platform
{
    public sealed class Window : IWindow
    {
        // <summary>
        /// Frequency cap for Update/RenderFrame events.
        /// </summary>
        private const double MaxFrequency = 500.0;

        private const int IsRunningSlowlyRetries = 4;

        private readonly NativeWindow nativeWindow;

        private readonly Stopwatch watchUpdate = new();

        private readonly Stopwatch watchRender = new();

        private IGameEventHandler? handler;

        private double renderFrequency;

        private double updateFrequency;

        // quantization error for UpdateFrame events
        private double updateEpsilon;

        /// <summary>
        /// Gets a value indicating whether or not UpdatePeriod has consistently failed to reach TargetUpdatePeriod.
        /// This can be used to do things such as decreasing visual quality if the user's computer isn't powerful enough
        /// to handle the application.
        /// </summary>
        private bool isRunningSlowly;

        public Window(IGameEventHandler? handler = null)
        {
            this.SetHandler(handler);

            var nativeWindowSettings = new NativeWindowSettings
            {
                Flags = ContextFlags.ForwardCompatible,
                APIVersion = new Version(4, 1),
                Profile = ContextProfile.Core
            };

            this.nativeWindow = new NativeWindow(nativeWindowSettings);
        }

        public string Title
        {
            get => this.nativeWindow.Title;
            set => this.nativeWindow.Title = value;
        }

        public bool VSync
        {
            get => this.nativeWindow.VSync == VSyncMode.On;
            set => this.nativeWindow.VSync = value ? VSyncMode.On : VSyncMode.Off;
        }

        public int Width
        {
            get => this.nativeWindow.Size.X;
            set => this.nativeWindow.Size = (value, this.nativeWindow.Size.Y);
        }

        public int Height
        {
            get => this.nativeWindow.Size.Y;
            set => this.nativeWindow.Size = (this.nativeWindow.Size.X, value);
        }

        public float AspectRatio => (float)this.Width / this.Height;

        public bool IsActive => this.nativeWindow.IsFocused;

        public double RenderFrequency
        {
            get => this.renderFrequency;

            set
            {
                if (value <= 1.0)
                {
                    this.renderFrequency = 0.0;
                }
                else if (value <= MaxFrequency)
                {
                    this.renderFrequency = value;
                }
                else
                {
                    Debug.Print($"Target render frequency clamped to {MaxFrequency}Hz.");

                    this.renderFrequency = MaxFrequency;
                }
            }
        }

        /// <summary>
        /// Gets or sets a double representing the update frequency, in hertz.
        /// </summary>
        /// <remarks>
        ///  <para>
        /// A value of 0.0 indicates that UpdateFrame events are generated at the maximum possible frequency (i.e. only
        /// limited by the hardware's capabilities).
        ///  </para>
        ///  <para>Values lower than 1.0Hz are clamped to 0.0. Values higher than 500.0Hz are clamped to 500.0Hz.</para>
        /// </remarks>
        public double UpdateFrequency
        {
            get => this.updateFrequency;

            set
            {
                if (value < 1.0)
                {
                    this.updateFrequency = 0.0;
                }
                else if (value <= MaxFrequency)
                {
                    this.updateFrequency = value;
                }
                else
                {
                    Debug.Print($"Target render frequency clamped to {MaxFrequency}Hz.");

                    this.updateFrequency = MaxFrequency;
                }
            }
        }

        /// <summary>
        /// Gets a double representing the time spent in the RenderFrame function, in seconds.
        /// </summary>
        public double RenderTime { get; private set; }

        /// <summary>
        /// Gets a double representing the time spent in the UpdateFrame function, in seconds.
        /// </summary>
        public double UpdateTime { get; private set; }

        public void SetHandler(IGameEventHandler? handler)
        {
            if (this.handler != null)
            {
                this.nativeWindow.Resize -= args => this.handler.Resize(args.Width, args.Height);
                this.nativeWindow.MouseMove -= args => this.handler.MouseMove(args.Position, args.Delta);
                this.nativeWindow.KeyDown -= args => this.handler.KeyDown(args);
            }

            this.handler = handler;

            if (handler != null)
            {
                this.nativeWindow.Resize += args => handler.Resize(args.Width, args.Height);
                this.nativeWindow.MouseMove += args => handler.MouseMove(args.Position, args.Delta);
                this.nativeWindow.KeyDown += args => handler.KeyDown(args);
            }
        }

        public void Run()
        {
            // Make sure that the GL context is current for OnLoad and the initial OnResize
            this.nativeWindow.Context?.MakeCurrent();

            // Send the OnLoad event, to load all user code.
            this.handler?.Load();

            // Send a dummy OnResize event, to make sure any listening user code has the correct values.
            this.handler?.Resize(this.Width, this.Height);

            this.watchRender.Start();
            this.watchUpdate.Start();

            while (!this.WindowShouldClose)
            {
                double timeToNextUpdateFrame = this.DispatchUpdateFrame();
                double timeToNextRenderFrame = this.DispatchRenderFrame();

                double sleepTime = System.Math.Min(timeToNextUpdateFrame, timeToNextRenderFrame);

                if (sleepTime > 0)
                {
                    Thread.Sleep((int)System.Math.Floor(sleepTime * 1000));
                }
            }

            this.handler?.Unload();
        }

        /// <summary>
        /// Swaps the front and back buffers of the current GraphicsContext, presenting the rendered scene to the user.
        /// </summary>
        private void SwapBuffers()
        {
            if (this.nativeWindow.Context == null)
            {
                throw new InvalidOperationException("Cannot use SwapBuffers when running with ContextAPI.NoAPI.");
            }

            this.nativeWindow.Context.SwapBuffers();
        }

        private unsafe bool WindowShouldClose => GLFW.WindowShouldClose(this.nativeWindow.WindowPtr);

        private double DispatchUpdateFrame()
        {
            var isRunningSlowlyRetries = IsRunningSlowlyRetries;
            var elapsed = this.watchUpdate.Elapsed.TotalSeconds;

            var updatePeriod = this.UpdateFrequency == 0 ? 0 : 1 / this.UpdateFrequency;

            while (elapsed > 0 && elapsed + this.updateEpsilon >= updatePeriod)
            {
                // Update input state for next frame
                this.nativeWindow.ProcessInputEvents();

                // Handle events for this frame
                NativeWindow.ProcessWindowEvents(this.nativeWindow.IsEventDriven);

                this.watchUpdate.Restart();

                this.UpdateTime = elapsed;

                this.handler?.UpdateFrame(elapsed);

                // Calculate difference (positive or negative) between actual elapsed time and target elapsed time. We must compensate for this difference
                this.updateEpsilon += elapsed - updatePeriod;

                if (this.UpdateFrequency <= Double.Epsilon)
                {
                    // An UpdateFrequency of zero means we will raise UpdateFrame events as fast as possible (one event per ProcessEvents() call)
                    break;
                }

                this.isRunningSlowly = this.updateEpsilon >= updatePeriod;

                if (this.isRunningSlowly && --isRunningSlowlyRetries == 0)
                {
                    // If UpdateFrame consistently takes longer than TargetUpdateFrame stop raising events to avoid hanging inside the UpdateFrame loop
                    this.updateEpsilon = 0;

                    break;
                }

                elapsed = this.watchUpdate.Elapsed.TotalSeconds;
            }

            return this.UpdateFrequency == 0 ? 0 : updatePeriod - elapsed;
        }

        private double DispatchRenderFrame()
        {
            var elapsed = this.watchRender.Elapsed.TotalSeconds;
            var renderPeriod = this.RenderFrequency == 0 ? 0 : 1 / this.RenderFrequency;

            if (elapsed > 0 && elapsed >= renderPeriod)
            {
                this.watchRender.Restart();

                this.RenderTime = elapsed;

                if (this.handler != null)
                {
                    this.handler.RenderFrame(elapsed);
                    this.SwapBuffers();
                }

                // Update VSync if set to adaptive
                if (this.nativeWindow.VSync == VSyncMode.Adaptive)
                {
                    GLFW.SwapInterval(this.isRunningSlowly ? 0 : 1);
                }
            }

            return this.RenderFrequency == 0 ? 0 : renderPeriod - elapsed;
        }

        public void Close()
        {
            if (this.handler != null)
            {
                this.nativeWindow.Resize -= args => this.handler.Resize(args.Width, args.Height);
                this.nativeWindow.MouseMove -= args => this.handler.MouseMove(args.Position, args.Delta);
                this.nativeWindow.KeyDown -= args => this.handler.KeyDown(args);
            }

            this.nativeWindow.Close();
        }

        public void Dispose()
        {
            this.Close();

            this.nativeWindow.Dispose();
        }
    }
}