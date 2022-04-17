using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace NativeJunk
{
    public class PointRegistration
    {
        [DllImport("__Internal", EntryPoint = "NativeJunk_SayHello")]
        public static extern void SayHello();

        [DllImport("__Internal", EntryPoint = "IterativeClosestPoint")]
        static extern unsafe int IterativeClosestPoint(float* set1Data, int set1NumPoints, float* set2Data, int set2NumPoints);
        public static unsafe void IterativeClosestPoint(ReadOnlySpan<Vector3> set1, Span<Vector3> set2)
        {
            if (Marshal.SizeOf<Matrix4x4>() != 4 * 4 * 4)
                throw new Exception("Matrix is the wrong size!");
            if (Marshal.SizeOf<Vector3>() != 3 * 4)
                throw new Exception("Vector3 is the wrong size!");
            float score = 0.0f;
            int error = 0;
            Matrix4x4 myMat = Matrix4x4.Identity;

            Console.WriteLine($"SET1 MEAN: " + GetMeanPoint(set1));
            Console.WriteLine($"SET2 MEAN: " + GetMeanPoint(set2));

            fixed (Vector3* pset1 = set1)
            {
                fixed (Vector3* pset2 = set2)
                {
                    error = IterativeClosestPoint(&pset1->X, set1.Length, &pset2->X, set2.Length);
                }
            }
            if (error != 0)
                throw new Exception($"OpenGR failed with code: {error}");
            Console.WriteLine($"+SET2 MEAN: " + GetMeanPoint(set2));
            //mat = myMat;
            //return score;
        }

        static Vector3 GetMeanPoint(ReadOnlySpan<Vector3> points)
        {
            Vector3 mean = Vector3.Zero;
            foreach (var point in points)
            {
                mean += point;
            }
            mean /= points.Length;
            return mean;
        }

        [DllImport("__Internal", EntryPoint = "OpenGRMain")]
        static extern unsafe int OpenGRMain(float* set1Data, int set1NumPoints, float* set2Data, int set2NumPoints, float* outputMat, float* outputScore);

        public static unsafe float OpenGR(ReadOnlySpan<Vector3> set1, Span<Vector3> set2, out Matrix4x4 mat)
        {
            if (Marshal.SizeOf<Matrix4x4>() != 4 * 4 * 4)
                throw new Exception("Matrix is the wrong size!");
            if (Marshal.SizeOf<Vector3>() != 3 * 4)
                throw new Exception("Vector3 is the wrong size!");
            float score = 0.0f;
            int error = 0;
            Matrix4x4 myMat = Matrix4x4.Identity;
            fixed (Vector3* pset1 = set1)
            {
                fixed (Vector3* pset2 = set2)
                {
                    error = OpenGRMain(&pset1->X, set1.Length, &pset2->X, set2.Length, &myMat.M11, &score);
                }
            }
            if (error != 0)
                throw new Exception($"OpenGR failed with code: {error}");
            mat = myMat;
            return score;
        }
    }
}
