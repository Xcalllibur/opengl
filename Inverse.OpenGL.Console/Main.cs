namespace Inverse.OpenGL.Console
{
    public sealed class Main : IDisposable
    {
        private readonly Engine engine;

        public Main(Engine engine)
        {
            this.engine = engine;
        }

        public void Run()
        {
            this.engine.Initialise();
            this.engine.Run();

            this.Shutdown();
        }

        public void Dispose()
        {
            this.Shutdown();
        }

        private void Shutdown()
        {
            try
            {
                this.engine.Stop();
            }
            catch (Exception)
            {
                // TODO - Log
                throw;
            }
            finally
            {
                this.engine.Dispose();
            }
        }
    }
}