namespace Inverse.OpenGL.Client
{
    public sealed class WaveFunc
    {
        public enum FuncType       // various periodic functions
        {
            FUNC_SIN = 0,
            FUNC_TRIANGLE,
            FUNC_SQUARE,
            FUNC_SAWTOOTH
        };

        public FuncType func;
        public float amp;       // amplitude
        public float freq;      // frequency
        public float phase;     // horizontal shift
        public float offset;    // vertical shift
        public float output;    // result at given time

        // default constructor, initialize all members
        public WaveFunc()
        {
            this.func = FuncType.FUNC_SIN;
            this.amp = 1.0f;
            this.freq = 1.0f;
            this.phase = 0.0f;
            this.offset = 0.0f;
            this.output = 0.0f;
        }

        public float Update(float time)
        {
            // compute time factor between 0 and 1 from (freq*(time - phase))
            float timeFact = this.freq * (time - this.phase);
            timeFact -= (int)timeFact;

            switch (this.func)
            {
                case FuncType.FUNC_SIN:
                    this.output = (float)Math.Sin(Math.PI / 2 * timeFact);

                    break;

                case FuncType.FUNC_TRIANGLE:
                    if (timeFact < 0.25f)            // 0 ~ 0.25
                    {
                        this.output = 4 * timeFact;
                    }
                    else if (timeFact < 0.75f)       // 0.25 ~ 0.75
                    {
                        this.output = 2 - (4 * timeFact);
                    }
                    else                             // 0.75 ~ 1
                    {
                        this.output = (4 * timeFact) - 4;
                    }

                    break;

                case FuncType.FUNC_SQUARE:
                    if (timeFact < 0.5f)
                    {
                        this.output = 1;
                    }
                    else
                    {
                        this.output = -1;
                    }

                    break;

                case FuncType.FUNC_SAWTOOTH:
                    this.output = (2 * timeFact) - 1;

                    break;

                default:
                    this.output = 1; // no function defined

                    break;
            }

            // apply amplitude and offset
            this.output = (this.amp * this.output) + this.offset;

            return this.output;
        }
    }
}