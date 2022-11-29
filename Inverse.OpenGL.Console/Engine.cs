using Inverse.OpenGL.Client;
using Inverse.OpenGL.Platform;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using Serilog;
using StbImageSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Buffer = System.Buffer;

namespace Inverse.OpenGL.Console
{
    public sealed class Engine : IGameEventHandler, IDisposable, IWindowProvider
    {
        private const float CAMERA_DISTANCE = 8f;

        private const int Stride = 3 + 3 + 3 + 2;

        // Blinn-phong shading with colour + texture
        // NOTE - Position defined as vec4 - GLSL will insert value of 1 as w component - refer https://www.khronos.org/opengl/wiki/Vertex_Specification#Vertex_format
        private const string vsSource =
        @"
            # version 330 core

            uniform mat4 matrixModelView;
            uniform mat4 matrixProjection;
            uniform mat4 matrixNormal;

            in vec4 vertexPosition;
            in vec3 vertexNormal;
            in vec3 vertexColor;
            in vec2 vertexTexCoord;

            out vec3 esVertex;
            out vec3 esNormal;
            out vec3 color;
            out vec2 texCoord0;

            void main()
            {
                gl_Position = matrixProjection * matrixModelView * vertexPosition;

                vec4 esVertex4 = matrixModelView * vertexPosition;
                esVertex = vec3(esVertex4) / esVertex4.w;

                esNormal = vec3(matrixNormal * vec4(vertexNormal, 0.0));

                color = vertexColor;
                texCoord0 = vertexTexCoord;
            }
        ";

        private const string fsSource =
        @"
            # version 330 core

            uniform vec4 lightPosition;
            uniform vec4 lightAmbient;
            uniform vec4 lightDiffuse;
            uniform vec4 lightSpecular;
            uniform sampler2D map0;

            in vec3 esVertex;
            in vec3 esNormal;
            in vec3 color;
            in vec2 texCoord0;

            out vec4 fragColorOut;

            void main()
            {
                vec3 normal = normalize(esNormal);
                vec3 light;

                if (lightPosition.w == 0.0)
                {
                    light = normalize(lightPosition.xyz);
                }
                else
                {
                    light = normalize(lightPosition.xyz - esVertex);
                }

                vec3 view = normalize(-esVertex);
                vec3 halfv = normalize(light + view);

                vec3 fragColor = lightAmbient.rgb * color;                  // begin with ambient
                float dotNL = max(dot(normal, light), 0.0);
                fragColor += lightDiffuse.rgb * color * dotNL;              // add diffuse
                fragColor *= texture2D(map0, texCoord0).rgb;                // modulate texture map
                float dotNH = max(dot(normal, halfv), 0.0);
                fragColor += pow(dotNH, 128.0) * lightSpecular.rgb * color; // add specular

                fragColorOut = vec4(fragColor, 1.0);  // keep opaque=1
            }
        ";

        private Matrix4 matrixProjection;

        private int vaoId;
        private int vboId;
        private int iboId;
        private int texId;
        private int progId;

        private int uniformMatrixModel;
        private int uniformMatrixView;
        private int uniformMatrixModelView;
        private int uniformMatrixProjection;
        private int uniformMatrixModelViewProjection;
        private int uniformMatrixNormal;
        private int uniformLightPosition;
        private int uniformLightAmbient;
        private int uniformLightDiffuse;
        private int uniformLightSpecular;
        private int uniformMap0;
        private int attribVertexPosition;
        private int attribVertexNormal;
        private int attribVertexColor;
        private int attribVertexTexCoord;

        private Stopwatch timer;
        private int frameCount;

        private float[] GetStripedData(float[] vertices, float[] normals, float[] colors, float[] texCoords)
        {
            int vertexCount = vertices.Length / 3;

            var data = new float[vertexCount * Stride];

            for (int i = 0; i < vertexCount; i++)
            {
                int offset = i * Stride;

                // Vertex
                data[offset + 0] = vertices[(i * 3) + 0];
                data[offset + 1] = vertices[(i * 3) + 1];
                data[offset + 2] = vertices[(i * 3) + 2];

                // Normal
                data[offset + 3] = normals[(i * 3) + 0];
                data[offset + 4] = normals[(i * 3) + 1];
                data[offset + 5] = normals[(i * 3) + 2];

                // Color
                data[offset + 6] = colors[0];//(i % 24 * 3) + 0];
                data[offset + 7] = colors[1];//(i % 24 * 3) + 1];
                data[offset + 8] = colors[2];//(i % 24 * 3) + 2];

                // Texture Coordinate
                data[offset + 9] = texCoords[(i % 24 * 2) + 0];
                data[offset + 10] = texCoords[(i % 24 * 2) + 1];
            }

            return data;
        }

        public static readonly bool IsMacOS = OperatingSystem.IsMacOS();

        private readonly IWindowProvider windowProvider;

        private IWindow window;

        private int screenWidth;
        private int screenHeight;

        private float cameraAngleX;
        private float cameraAngleY;
        private float cameraDistance;
        private int commandCount;

        public Engine(IWindowProvider windowProvider)
        {
            this.windowProvider = windowProvider;
        }

        public IWindow GetWindow()
        {
            return this.window;
        }

        public void Initialise()
        {
            this.window = this.windowProvider.GetWindow();
            this.window.SetHandler(this);
        }

        public void Run()
        {
            this.window.Run();
        }

        public void Stop()
        {
            this.window.Close();
        }

        public void Dispose()
        {
            this.window.Dispose();

            GL.DeleteVertexArray(this.vaoId);
            GL.DeleteBuffer(this.vboId);
            GL.DeleteBuffer(this.iboId);
            GL.DeleteTexture(this.texId);
            GL.DeleteProgram(this.progId);
        }

        public void Load()
        {
            //GL.DebugMessageCallback(OnDebugMessage, IntPtr.Zero);
            //GL.Enable(EnableCap.DebugOutput);
            //GL.Enable(EnableCap.DebugOutputSynchronous);

            this.cameraAngleX = this.cameraAngleY = 0.0f;
            this.cameraDistance = CAMERA_DISTANCE;

            this.InitGL();
            this.InitGLSL();

            var data = this.GetStripedData(Teapot.Vertices, Teapot.Normals, new float[] { 0.929524f, 0.796542f, 0.178823f }, Cube.TexCoords);

            this.vaoId = GL.GenVertexArray();
            this.vboId = GL.GenBuffer();
            this.iboId = GL.GenBuffer();

            GL.BindVertexArray(this.vaoId);

            // Immutable buffers - refer https://www.khronos.org/opengl/wiki/Buffer_Object#Immutable_Storage
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.vboId);
            GL.BufferData(BufferTarget.ArrayBuffer, Buffer.ByteLength(data), data, BufferUsageHint.DynamicDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, this.iboId);
            GL.BufferData(BufferTarget.ElementArrayBuffer, Buffer.ByteLength(Teapot.Indices), Teapot.Indices, BufferUsageHint.StaticDraw);

            this.commandCount = Teapot.Indices.Length;

            int stride = Stride * sizeof(float);

            // Vertex
            GL.VertexAttribPointer(this.attribVertexPosition, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(this.attribVertexPosition);

            // Normal
            GL.VertexAttribPointer(this.attribVertexNormal, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(this.attribVertexNormal);

            // Color
            GL.VertexAttribPointer(this.attribVertexColor, 3, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
            GL.EnableVertexAttribArray(this.attribVertexColor);

            // Texture coordinate
            GL.VertexAttribPointer(this.attribVertexTexCoord, 2, VertexAttribPointerType.Float, false, stride, 9 * sizeof(float));
            GL.EnableVertexAttribArray(this.attribVertexTexCoord);

            GL.BindVertexArray(0);

            using var stream = File.OpenRead("grid512.bmp");
            var bmp = ImageResult.FromStream(stream);

            // determine texture format based on # of bits per pixel
            PixelFormat format;
            PixelInternalFormat internalFormat;

            switch (bmp.Comp)
            {
                case ColorComponents.Grey:
                    format = PixelFormat.Luminance;
                    internalFormat = PixelInternalFormat.Luminance;
                    break;
                case ColorComponents.RedGreenBlue:
                    format = PixelFormat.Rgb;
                    internalFormat = PixelInternalFormat.Rgb8;
                    break;
                case ColorComponents.RedGreenBlueAlpha:
                    format = PixelFormat.Rgba;
                    internalFormat = PixelInternalFormat.Rgba8;
                    break;
                default:
                    format = PixelFormat.Rgba;
                    internalFormat = PixelInternalFormat.Rgba8;
                    break;
            }

            // copy the texture to OpenGL
            this.texId = GL.GenTexture();

            // set active texture and configure it
            GL.BindTexture(TextureTarget.Texture2D, this.texId);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // copy bitmap data to texture object
            GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, bmp.Width, bmp.Height, 0, format, PixelType.UnsignedByte, bmp.Data);

            // TODO - I added this - refer https://opentk.net/learn/chapter1/5-textures.html?tabs=load-texture-opentk3
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

            // unbind
            GL.BindTexture(TextureTarget.Texture2D, 0);

            this.timer = Stopwatch.StartNew();
        }

        private void InitGL()
        {
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4); // 4-byte pixel alignment

            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);

            GL.ClearColor(0, 0, 0, 0);                   // background color
            GL.ClearStencil(0);                          // clear stencil buffer
            GL.ClearDepth(1.0f);                         // 0 is near, 1 is far
            GL.DepthFunc(DepthFunction.Lequal);

            //GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
        }

        public void MouseMove(Vector2 position, Vector2 delta)
        {
            this.cameraAngleX += delta.Y / 100;
            this.cameraAngleY += delta.X / 100;
        }

        public void KeyDown(KeyboardKeyEventArgs args)
        {
            if (args.Key == OpenTK.Windowing.GraphicsLibraryFramework.Keys.Down)
            {
                this.commandCount++;
            }

            if (args.Key == OpenTK.Windowing.GraphicsLibraryFramework.Keys.Up)
            {
                this.commandCount--;
            }
        }

        public void UpdateFrame(double elapsed)
        {
            //this.cameraAngleX += 0.005f;
            //this.cameraAngleY += 0.005f;

            // Map the buffer object into client's memory
            // Note that glMapBuffer() causes sync issue.
            // If GPU is working with this buffer, glMapBufferARB() will wait(stall) for GPU to finish its job.
            var ptr = GL.MapBuffer(BufferTarget.ArrayBuffer, BufferAccess.WriteOnly);

            if (ptr != IntPtr.Zero)
            {
                // wobble vertex in and out along normal
                this.UpdateVertices(ptr, Teapot.Vertices, Teapot.Normals, Teapot.VertexCount, (float)this.timer.Elapsed.TotalSeconds);

                GL.UnmapBuffer(BufferTarget.ArrayBuffer);
            }

            this.frameCount++;

            if (this.frameCount % 100 == 0)
            {
                double fps = this.frameCount / this.timer.Elapsed.TotalSeconds;

                Log.Debug($"Frame={this.frameCount} FPS={(int)fps}");
            }
        }

        public void RenderFrame(double elapsed)
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

            var matrixModel = Matrix4.CreateTranslation(0, 0, -this.cameraDistance);
            var matrixView = Matrix4.CreateRotationX(this.cameraAngleX) * Matrix4.CreateRotationY(this.cameraAngleY);
            var matrixModelView = matrixView * matrixModel;
            var matrixNormal = matrixView * matrixModel;

            // TODO - Not sure this is required
            //Matrix4Extensions.SetColumn(ref matrixNormal, 0, 0, 0, 1);

            GL.UseProgram(this.progId);

            GL.UniformMatrix4(this.uniformMatrixModelView, false, ref matrixModelView);
            GL.UniformMatrix4(this.uniformMatrixProjection, false, ref this.matrixProjection);
            GL.UniformMatrix4(this.uniformMatrixNormal, false, ref matrixNormal);

            GL.BindTexture(TextureTarget.Texture2D, this.texId);

            GL.BindVertexArray(this.vaoId);

            this.commandCount = Teapot.Draw(this.commandCount);

            GL.BindVertexArray(0);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            GL.UseProgram(0);
        }

        public void Resize(int width, int height)
        {
            this.screenWidth = width;
            this.screenHeight = height;

            this.ToPerspective();
        }

        private void ToPerspective()
        {
            // set viewport to be the entire window
            GL.Viewport(0, 0, this.screenWidth, this.screenHeight);

            this.matrixProjection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(45.0f), this.screenWidth / (float)this.screenHeight, 0.1f, 1000.0f);
            //this.matrixProjection = Matrix4.CreateOrthographic(this.screenWidth, this.screenHeight, 0.1f, 100.0f);
        }

        public void Unload()
        {
            this.Dispose();
        }

        private static void OnDebugMessage(
                    DebugSource source,     // Source of the debugging message.
                    DebugType type,         // Type of the debugging message.
                    int id,                 // ID associated with the message.
                    DebugSeverity severity, // Severity of the message.
                    int length,             // Length of the string in pMessage.
                    IntPtr pMessage,        // Pointer to message string.
                    IntPtr pUserParam       // The pointer you gave to OpenGL, explained later.
                )
        {
            string message = $"[{severity} source={source} type={type} id={id}] {Marshal.PtrToStringAnsi(pMessage, length)}";

            if (type == DebugType.DebugTypeError)
            {
                Log.Error(message);
            }
            else
            {
                Log.Debug(message);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////
        // create glsl programs
        ///////////////////////////////////////////////////////////////////////////////
        private void InitGLSL()
        {
            // create shader and program
            int vsId = GL.CreateShader(ShaderType.VertexShader);
            int fsId = GL.CreateShader(ShaderType.FragmentShader);
            this.progId = GL.CreateProgram();

            // load shader sources
            GL.ShaderSource(vsId, vsSource);
            GL.ShaderSource(fsId, fsSource);

            // compile shader sources
            GL.CompileShader(vsId);
            GL.CompileShader(fsId);

            //@@ debug
            GL.GetShader(vsId, ShaderParameter.CompileStatus, out int vsStatus);

            if (vsStatus == 0)
            {
                Debug.Fail($"===== Vertex Shader Log ===== {GL.GetShaderInfoLog(vsId)}");
            }

            GL.GetShader(fsId, ShaderParameter.CompileStatus, out int fsStatus);

            if (fsStatus == 0)
            {
                Debug.Fail($"===== Fragment Shader Log ===== {GL.GetShaderInfoLog(fsId)}");
            }

            // attach shaders to the program
            GL.AttachShader(this.progId, vsId);
            GL.AttachShader(this.progId, fsId);

            // link program
            GL.LinkProgram(this.progId);

            // get uniform/attrib locations
            GL.UseProgram(this.progId);

            this.uniformMatrixModel = GL.GetUniformLocation(this.progId, "matrixModel");
            this.uniformMatrixView = GL.GetUniformLocation(this.progId, "matrixView");
            this.uniformMatrixProjection = GL.GetUniformLocation(this.progId, "matrixProjection");
            this.uniformMatrixModelView = GL.GetUniformLocation(this.progId, "matrixModelView");
            this.uniformMatrixModelViewProjection = GL.GetUniformLocation(this.progId, "matrixModelViewProjection");
            this.uniformMatrixNormal = GL.GetUniformLocation(this.progId, "matrixNormal");
            this.uniformLightPosition = GL.GetUniformLocation(this.progId, "lightPosition");
            this.uniformLightAmbient = GL.GetUniformLocation(this.progId, "lightAmbient");
            this.uniformLightDiffuse = GL.GetUniformLocation(this.progId, "lightDiffuse");
            this.uniformLightSpecular = GL.GetUniformLocation(this.progId, "lightSpecular");
            this.uniformMap0 = GL.GetUniformLocation(this.progId, "map0");

            this.attribVertexPosition = GL.GetAttribLocation(this.progId, "vertexPosition");
            this.attribVertexNormal = GL.GetAttribLocation(this.progId, "vertexNormal");
            this.attribVertexColor = GL.GetAttribLocation(this.progId, "vertexColor");
            this.attribVertexTexCoord = GL.GetAttribLocation(this.progId, "vertexTexCoord");

            // set uniform values
            float[] lightPosition = { 0, 0, 3, 0 };
            float[] lightAmbient = { 0.3f, 0.3f, 0.3f, 1 };
            float[] lightDiffuse = { 0.7f, 0.7f, 0.7f, 1 };
            float[] lightSpecular = { 1.0f, 1.0f, 1.0f, 1 };

            GL.Uniform4(this.uniformLightPosition, 1, lightPosition);
            GL.Uniform4(this.uniformLightAmbient, 1, lightAmbient);
            GL.Uniform4(this.uniformLightDiffuse, 1, lightDiffuse);
            GL.Uniform4(this.uniformLightSpecular, 1, lightSpecular);
            GL.Uniform1(this.uniformMap0, 0);

            // unbind GLSL
            GL.UseProgram(0);

            // check GLSL status
            GL.GetProgram(this.progId, GetProgramParameterName.LinkStatus, out int linkStatus);

            if (linkStatus == 0)
            {
                Debug.Fail($"===== GLSL Program Log ===== {GL.GetProgramInfoLog(this.progId)}");
            }
        }

        ///////////////////////////////////////////////////////////////////////////////
        // Wobble the vertex in and out along normal
        ///////////////////////////////////////////////////////////////////////////////
        private unsafe void UpdateVertices(IntPtr dstVertices, float[] srcVertices, float[] srcNormals, int count, float time)
        {
            var wave = new WaveFunc
            {
                func = WaveFunc.FuncType.FUNC_SIN, // sine wave function
                amp = 0.08f,                       // amplitude
                freq = 1.0f,                       // cycles/sec
                phase = 0,                         // horizontal shift
                offset = 0                         // vertical shift
            };

            float waveLength = 1.5f;
            float height;
            float x, y, z;

            int srcOffset = 0;
            int dstOffset = 0;
            float* dstPointer = (float*)dstVertices.ToPointer();

            for (int i = 0; i < count; ++i, srcOffset += 3, dstOffset += Stride)
            {
                // get source from original vertex array
                x = srcVertices[srcOffset + 0];
                y = srcVertices[srcOffset + 1];
                z = srcVertices[srcOffset + 2];

                // compute phase (horizontal shift)
                wave.phase = (x + y + z) / waveLength;

                height = wave.Update(time);

                // update vertex coords
                dstPointer[dstOffset + 0] = x + (height * srcNormals[srcOffset + 0]); // x
                dstPointer[dstOffset + 1] = y + (height * srcNormals[srcOffset + 1]); // y
                dstPointer[dstOffset + 2] = z + (height * srcNormals[srcOffset + 2]); // z
            }
        }
    }
}