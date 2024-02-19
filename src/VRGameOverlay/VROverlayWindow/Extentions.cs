using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SharpDX;

namespace VRGameOverlay.VROverlayWindow
{
    public static class Extentions
    {

        public static void FromHmdMatrix(ref HmdMatrix34_t source, ref Matrix destination)
        {
            destination.M11 = source.m0;
            destination.M21 = source.m1;
            destination.M31 = source.m2;
            destination.M41 = source.m3;
            destination.M12 = source.m4;
            destination.M22 = source.m5;
            destination.M32 = source.m6;
            destination.M42 = source.m7;
            destination.M13 = source.m8;
            destination.M23 = source.m9;
            destination.M33 = source.m10;
            destination.M43 = source.m11;
            //destination.M14 = 0.0f;
            //destination.M24 = 0.0f;
            //destination.M34 = 0.0f;
            //destination.M44 = 1.0f;
        }

        public static Matrix FromHmdMatrix(HmdMatrix34_t source)
        {
            var destination = new Matrix();

            destination.M11 = source.m0;
            destination.M21 = source.m1;
            destination.M31 = source.m2;
            destination.M41 = source.m3;
            destination.M12 = source.m4;
            destination.M22 = source.m5;
            destination.M32 = source.m6;
            destination.M42 = source.m7;
            destination.M13 = source.m8;
            destination.M23 = source.m9;
            destination.M33 = source.m10;
            destination.M43 = source.m11;
            destination.M14 = 0.0f;
            destination.M24 = 0.0f;
            destination.M34 = 0.0f;
            destination.M44 = 1.0f;

            return destination;
        }
        public static Matrix FromHmdMatrix(this Matrix destination, HmdMatrix34_t source)
        {
            destination.M11 = source.m0;
            destination.M21 = source.m1;
            destination.M31 = source.m2;
            destination.M41 = source.m3;
            destination.M12 = source.m4;
            destination.M22 = source.m5;
            destination.M32 = source.m6;
            destination.M42 = source.m7;
            destination.M13 = source.m8;
            destination.M23 = source.m9;
            destination.M33 = source.m10;
            destination.M43 = source.m11;
            //destination.M14 = 0.0f;
            //destination.M24 = 0.0f;
            //destination.M34 = 0.0f;
            //destination.M44 = 1.0f;

            return destination;
        }
        public static Matrix FromHmdMatrix(HmdMatrix44_t source)
        {
            var destination = new Matrix();

            destination.M11 = source.m0;
            destination.M21 = source.m1;
            destination.M31 = source.m2;
            destination.M41 = source.m3;
            destination.M12 = source.m4;
            destination.M22 = source.m5;
            destination.M32 = source.m6;
            destination.M42 = source.m7;
            destination.M13 = source.m8;
            destination.M23 = source.m9;
            destination.M33 = source.m10;
            destination.M43 = source.m11;
            destination.M14 = source.m12;
            destination.M24 = source.m13;
            destination.M34 = source.m14;
            destination.M44 = source.m15;

            return destination;
        }
        public static Matrix FromHmdMatrix(this Matrix destination, HmdMatrix44_t source)
        {
            destination.M11 = source.m0;
            destination.M21 = source.m1;
            destination.M31 = source.m2;
            destination.M41 = source.m3;
            destination.M12 = source.m4;
            destination.M22 = source.m5;
            destination.M32 = source.m6;
            destination.M42 = source.m7;
            destination.M13 = source.m8;
            destination.M23 = source.m9;
            destination.M33 = source.m10;
            destination.M43 = source.m11;
            destination.M14 = source.m12;
            destination.M24 = source.m13;
            destination.M34 = source.m14;
            destination.M44 = source.m15;

            return destination;
        }

        public static HmdMatrix34_t ToHmdMatrix34(this Matrix m)
        {
            //var m = Matrix.Scaling(new Vector3(1.0f, 1.0f, 1.0f)) * Matrix.Translation(pos) * Matrix.RotationQuaternion(rot);

            var pose = new HmdMatrix34_t
            {
                m0 = m[0, 0],
                m1 = m[0, 1],
                m2 = m[0, 2],
                m3 = m[0, 3],

                m4 = m[1, 0],
                m5 = m[1, 1],
                m6 = m[1, 2],
                m7 = m[1, 3],

                m8 = m[2, 0],
                m9 = m[2, 1],
                m10 = m[2, 2],
                m11 = m[2, 3]

            };
            return pose;
        }
        public static float sqrMagnitude(this Vector3 v)
        {
            return v.X * v.X + v.Y * v.Y + v.Z * v.Z;
        }

        // Returns the angle in degrees between /from/ and /to/. This is always the smallest
        public static float Angle(this Vector3 from, Vector3 to)
        {
            var normalFrom = Vector3.Normalize(from);
            var normalTo = Vector3.Normalize(to);
            // sqrt(a) * sqrt(b) = sqrt(a * b) -- valid for real numbers
            float denominator = (float)Math.Sqrt(normalFrom.sqrMagnitude() * normalTo.sqrMagnitude());
            if (denominator < MathUtil.kEpsilonNormalSqrt)
                return 0F;


            float dot = MathUtil.Clamp(Vector3.Dot(normalFrom, normalTo) / denominator, -1F, 1F);
            return (float)Math.Acos(dot) * MathUtil.Rad2Deg;
        }
    }

}
