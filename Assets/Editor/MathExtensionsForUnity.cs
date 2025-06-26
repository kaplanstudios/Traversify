#if !UNITY_2021_2_OR_NEWER
using UnityEngine;
using System;

namespace MathExtensions
{
    // Structures compatible with Unity.Mathematics for older Unity versions
    [Serializable]
    public struct float4
    {
        public float x, y, z, w;

        public float4(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    case 2: return z;
                    case 3: return w;
                    default: throw new IndexOutOfRangeException("Invalid float4 index!");
                }
            }
        }

        public static float4 operator -(float4 a, float4 b) => new float4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
    }

    [Serializable]
    public struct float4x4
    {
        public float4 c0, c1, c2, c3;

        public float4x4(float m00, float m01, float m02, float m03,
                       float m10, float m11, float m12, float m13,
                       float m20, float m21, float m22, float m23,
                       float m30, float m31, float m32, float m33)
        {
            c0 = new float4(m00, m10, m20, m30);
            c1 = new float4(m01, m11, m21, m31);
            c2 = new float4(m02, m12, m22, m32);
            c3 = new float4(m03, m13, m23, m33);
        }
    }

    public static class math
    {
        public static float lerp(float a, float b, float t) => Mathf.Lerp(a, b, t);
        public static float clamp(float value, float min, float max) => Mathf.Clamp(value, min, max);

        public static float dot(float4 a, float4 b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        }

        public static float4 mul(float4x4 m, float4 v)
        {
            return new float4(
                dot(new float4(m.c0.x, m.c1.x, m.c2.x, m.c3.x), v),
                dot(new float4(m.c0.y, m.c1.y, m.c2.y, m.c3.y), v),
                dot(new float4(m.c0.z, m.c1.z, m.c2.z, m.c3.z), v),
                dot(new float4(m.c0.w, m.c1.w, m.c2.w, m.c3.w), v)
            );
        }
    }
}
#endif
