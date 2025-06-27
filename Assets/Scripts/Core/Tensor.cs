using System;
using UnityEngine;
using Unity.AI.Inference;
using Tensor = Unity.AI.Inference.Tensor;

namespace Traversify.AI {
    public static class TensorUtils {
        /// <summary>Create a tensor from a Unity texture, resizing as needed.</summary>
        public static Tensor Preprocess(Texture2D tex, int maxDimension) {
            int w = tex.width, h = tex.height;
            float scale = 1f;
            if (Mathf.Max(w, h) > maxDimension)
                scale = maxDimension / (float)Mathf.Max(w, h);
            int tw = Mathf.RoundToInt(w * scale), th = Mathf.RoundToInt(h * scale);

            // Resize
            RenderTexture rt = RenderTexture.GetTemporary(tw, th);
            Graphics.Blit(tex, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D resized = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            resized.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            resized.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            // Create float buffer (NHWC)
            float[] data = new float[1 * th * tw * 3];
            Color32[] pix = resized.GetPixels32();
            for (int i = 0; i < pix.Length; i++) {
                data[i * 3 + 0] = pix[i].r / 255f;
                data[i * 3 + 1] = pix[i].g / 255f;
                data[i * 3 + 2] = pix[i].b / 255f;
            }
            UnityEngine.Object.Destroy(resized);

            return new Tensor(1, th, tw, 3, data);
        }

        /// <summary>Decode a binary mask tensor into a Texture2D.</summary>
        public static Texture2D DecodeMask(Tensor maskTensor) {
            int h = maskTensor.shape[1], w = maskTensor.shape[2];
            var tex = new Texture2D(w, h, TextureFormat.R8, false);
            Color[] cols = new Color[w * h];
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    float v = maskTensor[0, y, x, 0];
                    cols[y * w + x] = new Color(v, v, v, 1f);
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }
    }
}
