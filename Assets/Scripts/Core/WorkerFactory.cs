using UnityEngine;
using Unity.AI.Inference;

namespace Traversify.Core {
    public static class WorkerFactory {
        /// <summary>Create an inference session from a ModelAsset.</summary>
        public static IInferenceSession CreateSession(ModelAsset model, bool useGPU) {
            var options = new InferenceOptions {
                Device = useGPU ? InferenceDevice.GPU : InferenceDevice.CPU
            };
            return new ModelImporter(model).LoadSession(options);
        }
    }
}