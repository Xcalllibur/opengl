using OpenTK.Mathematics;

namespace Inverse.OpenGL.Client
{
    public sealed class Matrix4Extensions
    {
        public static void RotateX(ref Matrix4 input, float angle)
        {
            float r = MathHelper.DegreesToRadians(angle);
            float c = (float)Math.Cos(r);
            float s = (float)Math.Sin(r);

            float m1 = input.M12;
            float m2 = input.M13;
            float m5 = input.M22;
            float m6 = input.M23;
            float m9 = input.M32;
            float m10 = input.M33;
            float m13 = input.M42;
            float m14 = input.M43;

            input.M12 = (m1 * c) + (m2 * -s);
            input.M13 = (m1 * s) + (m2 * c);
            input.M22 = (m5 * c) + (m6 * -s);
            input.M23 = (m5 * s) + (m6 * c);
            input.M32 = (m9 * c) + (m10 * -s);
            input.M33 = (m9 * s) + (m10 * c);
            input.M42 = (m13 * c) + (m14 * -s);
            input.M43 = (m13 * s) + (m14 * c);
        }

        public static void Translate(ref Matrix4 input, float x, float y, float z)
        {
            input.M11 += input.M14 * x; input.M21 += input.M24 * x; input.M31 += input.M34 * x; input.M41 += input.M44 * x;
            input.M12 += input.M14 * y; input.M22 += input.M24 * y; input.M32 += input.M34 * y; input.M42 += input.M44 * y;
            input.M13 += input.M14 * z; input.M23 += input.M24 * z; input.M33 += input.M34 * z; input.M43 += input.M44 * z;
        }

        public static void SetColumn(ref Matrix4 input, float x, float y, float z, float w)
        {
            input.M14 = x; input.M24 = y; input.M34 = z; input.M44 = w;
        }
    }
}