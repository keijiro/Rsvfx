using UnityEngine;
using UnityEditor.VFX;
using UnityEngine.Experimental.VFX;

namespace VfxExtra
{
    [VFXInfo(category = "Random")]
    class RandomDirection : VFXOperator
    {
        override public string name { get { return "Random Direction"; } }

        public class InputProperties
        {
            [Tooltip("The length of the output vector.")]
            public float length = 1.0f;
            [Tooltip("Seed to compute the constant random.")]
            public uint seed = 0u;
        }

        public class OutputProperties
        {
            [Tooltip("A random 3D vector.")]
            public Vector3 v = Vector3.right;
        }

        protected override sealed VFXExpression[] BuildExpression(VFXExpression[] inputExpression)
        {
            var t = new VFXExpressionSharedRandom(inputExpression[1] * VFXValue.Constant(2u) + VFXValue.Constant(1u)) * VFXValue.Constant(Mathf.PI * 2);
            var z = new VFXExpressionSharedRandom(inputExpression[1] * VFXValue.Constant(2u)) * VFXValue.Constant(2.0f) - VFXValue.Constant(1.0f);
            var w = VFXOperatorUtility.Sqrt(VFXValue.Constant(1.0f) - z * z);
            return new[] { new VFXExpressionCombine(new VFXExpressionCos(t) * w, new VFXExpressionSin(t) * w, z) * VFXOperatorUtility.CastFloat(inputExpression[0], VFXValueType.Float3) };
        }
    }
}
