using System.Collections.Generic;
using UnityEditor.ShaderGraph.Drawing.Controls;
using UnityEditor.Graphing;
using UnityEngine;

namespace UnityEditor.ShaderGraph
{
    [Title("Input", "Basic", "Color Ramp")]
    public class ColorRampNode : AbstractMaterialNode, IGeneratesBodyCode, IGeneratesFunction
    {
        const int kInputSlotId = 0;
        const int kOutputSlotId = 1;

        const string kInputSlotName = "In";
        const string kOutputSlotName = "Out";

        public ColorRampNode()
        {
            name = "Color Ramp";
            UpdateNodeAfterDeserialization();
        }

        string GetFunctionName()
        {
            return "Unity_ColorRamp_" + precision;
        }

        [SerializeField]
        Gradient m_Ramp = new Gradient();

        [GradientControl("")]
        public Gradient gradient
        {
            get { return m_Ramp; }
            set
            {
                if (value == m_Ramp) return;
                m_Ramp = value;
                Dirty(ModificationScope.Node);
            }
        }

        Vector4 KeyToVector(GradientColorKey key)
        {
            var c = key.color;
            return new Vector4(c.r, c.g, c.b, key.time);
        }

        Vector2 KeyToVector(GradientAlphaKey key)
        {
            return new Vector2(key.alpha, key.time);
        }

        string KeyToString(GradientColorKey key)
        {
            var c = key.color;
            return string.Format("{0}3({1},{2},{3})", precision, c.r, c.g, c.b);
        }

        public sealed override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new Vector1MaterialSlot(kInputSlotId, kInputSlotName, kInputSlotName, SlotType.Input, 0));
            AddSlot(new Vector4MaterialSlot(kOutputSlotId, kOutputSlotName, kOutputSlotName, SlotType.Output, Vector4.zero));
            RemoveSlotsNameNotMatching(new[] { kInputSlotId, kOutputSlotId });
        }

        public void GenerateNodeFunction(FunctionRegistry registry, GenerationMode generationMode)
        {
            if (generationMode != GenerationMode.Preview) return;

            // Provides 8-keys ramp function for previews.
            registry.ProvideFunction(GetFunctionName() + "_color", s => {
                s.AppendLine("void {1}_color({0} x, out {0}3 Out, {0}4 key0, {0}4 key1, {0}4 key2, {0}4 key3, {0}4 key4, {0}4 key5, {0}4 key6, {0}4 key7)", precision, GetFunctionName());
                using (s.BlockScope()) {
                    s.AppendLine("Out = key0.rgb;");
                    s.AppendLine("Out = lerp(Out, key1.rgb, saturate((x - key0.w) / (key1.w - key0.w)));");
                    s.AppendLine("Out = lerp(Out, key2.rgb, saturate((x - key1.w) / (key2.w - key1.w)));");
                    s.AppendLine("Out = lerp(Out, key3.rgb, saturate((x - key2.w) / (key3.w - key2.w)));");
                    s.AppendLine("Out = lerp(Out, key4.rgb, saturate((x - key3.w) / (key4.w - key3.w)));");
                    s.AppendLine("Out = lerp(Out, key5.rgb, saturate((x - key4.w) / (key5.w - key4.w)));");
                    s.AppendLine("Out = lerp(Out, key6.rgb, saturate((x - key5.w) / (key6.w - key5.w)));");
                    s.AppendLine("Out = lerp(Out, key7.rgb, saturate((x - key6.w) / (key7.w - key6.w)));");
                    s.AppendLine("#ifndef UNITY_COLORSPACE_GAMMA");
                    s.AppendLine("Out = SRGBToLinear(Out);");
                    s.AppendLine("#endif");
                }
            });

            registry.ProvideFunction(GetFunctionName() + "_alpha", s => {
                s.AppendLine("void {1}_alpha({0} x, out {0} Out, {0}2 key0, {0}2 key1, {0}2 key2, {0}2 key3, {0}2 key4, {0}2 key5, {0}2 key6, {0}2 key7)", precision, GetFunctionName());
                using (s.BlockScope()) {
                    s.AppendLine("Out = key0.x;");
                    s.AppendLine("Out = lerp(Out, key1.x, saturate((x - key0.y) / (key1.y - key0.y)));");
                    s.AppendLine("Out = lerp(Out, key2.x, saturate((x - key1.y) / (key2.y - key1.y)));");
                    s.AppendLine("Out = lerp(Out, key3.x, saturate((x - key2.y) / (key3.y - key2.y)));");
                    s.AppendLine("Out = lerp(Out, key4.x, saturate((x - key3.y) / (key4.y - key3.y)));");
                    s.AppendLine("Out = lerp(Out, key5.x, saturate((x - key4.y) / (key5.y - key4.y)));");
                    s.AppendLine("Out = lerp(Out, key6.x, saturate((x - key5.y) / (key6.y - key5.y)));");
                    s.AppendLine("Out = lerp(Out, key7.x, saturate((x - key6.y) / (key7.y - key6.y)));");
                }
            });
        }

        public void GenerateNodeCode(ShaderGenerator visitor, GenerationMode generationMode)
        {
            var sb = new ShaderStringBuilder();

            var inValue = GetSlotValue(kInputSlotId, generationMode);
            var outName = GetVariableNameForSlot(kOutputSlotId);

            sb.AppendLine("{0}4 {1};", precision, outName);

            if (generationMode == GenerationMode.ForReals)
            {
                // Real mode: Inline math ops and keys into the code.
                var colorKeys = m_Ramp.colorKeys;
                var alphaKeys = m_Ramp.alphaKeys;

                sb.AppendLine("{0}.rgb = {1};", outName, KeyToString(colorKeys[0]));
                sb.AppendLine("{0}.a = {1};", outName, alphaKeys[0].alpha);

                for (var i = 1; i < colorKeys.Length; i++)
                {
                    sb.AppendLine(
                        "{0}.rgb = lerp({0}.rgb, {1}, saturate(({2} - {3}) / ({4} - {3})));",
                        outName, KeyToString(colorKeys[i]), inValue,
                        colorKeys[i - 1].time, colorKeys[i].time
                    );
                }

                for (var i = 1; i < alphaKeys.Length; i++)
                {
                    sb.AppendLine(
                        "{0}.a = lerp({0}.a, {1}, saturate(({2} - {3}) / ({4} - {3})));",
                        outName, alphaKeys[i].alpha, inValue,
                        alphaKeys[i - 1].time, alphaKeys[i].time
                    );
                }

                sb.AppendLine("#ifndef UNITY_COLORSPACE_GAMMA");
                sb.AppendLine("{0}.rgb = SRGBToLinear({0}.rgb);", outName);
                sb.AppendLine("#endif");
            }
            else
            {
                // Preview mode: Call the ramp function with uniforms.
                sb.AppendLine(
                    "{0}_color({1}, {2}.rgb, {3}_c0, {3}_c1, {3}_c2, {3}_c3, {3}_c4, {3}_c5, {3}_c6, {3}_c7);",
                    GetFunctionName(), inValue, outName, GetVariableNameForNode()
                );

                sb.AppendLine(
                    "{0}_alpha({1}, {2}.a, {3}_a0, {3}_a1, {3}_a2, {3}_a3, {3}_a4, {3}_a5, {3}_a6, {3}_a7);",
                    GetFunctionName(), inValue, outName, GetVariableNameForNode()
                );
            }

            visitor.AddShaderChunk(sb.ToString(), false);
        }

        public override void CollectShaderProperties(PropertyCollector properties, GenerationMode generationMode)
        {
            base.CollectShaderProperties(properties, generationMode);

            if (generationMode != GenerationMode.Preview) return;

            var colorKeys = m_Ramp.colorKeys;
            var alphaKeys = m_Ramp.alphaKeys;
            var name = GetVariableNameForNode();

            for (var i = 0; i < 8; i++)
            {
                properties.AddShaderProperty(new Vector4ShaderProperty()
                {
                    overrideReferenceName = name + "_c" + i,
                    generatePropertyBlock = false,
                    value = KeyToVector(colorKeys[Mathf.Min(i, colorKeys.Length - 1)])
                });

                properties.AddShaderProperty(new Vector2ShaderProperty()
                {
                    overrideReferenceName = name + "_a" + i,
                    generatePropertyBlock = false,
                    value = KeyToVector(alphaKeys[Mathf.Min(i, alphaKeys.Length - 1)])
                });
            }
        }

        public override void CollectPreviewMaterialProperties(List<PreviewProperty> properties)
        {
            base.CollectPreviewMaterialProperties(properties);

            var colorKeys = m_Ramp.colorKeys;
            var alphaKeys = m_Ramp.alphaKeys;
            var name = GetVariableNameForNode();

            for (var i = 0; i < 8; i++)
            {
                properties.Add(new PreviewProperty(PropertyType.Vector4)
                {
                    name = name + "_c" + i,
                    vector4Value = KeyToVector(colorKeys[Mathf.Min(i, colorKeys.Length - 1)])
                });

                properties.Add(new PreviewProperty(PropertyType.Vector2)
                {
                    name = name + "_a" + i,
                    vector4Value = KeyToVector(alphaKeys[Mathf.Min(i, alphaKeys.Length - 1)])
                });
            }
        }
    }
}
